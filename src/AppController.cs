using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CuteClash
{
    public sealed class AppController : IDisposable
    {
        public AppSettings Settings { get; private set; }
        public string DataDirectory { get; private set; }
        public bool IsRunning { get { try { return core != null && !core.HasExited && ready; } catch { return false; } } }
        public bool IsAdmin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
        public string CoreVersion { get; private set; }
        public string LastError { get; private set; }
        public event Action<string> Log;
        public event Action StateChanged;
        private readonly ProfileStore store;
        private readonly SystemProxy systemProxy;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly object logLock = new object();
        private readonly string corePath;
        private readonly string coreHome;
        private readonly string secret;
        private readonly HttpClient api;
        private Process core;
        private ProcessJob job;
        private bool ready;
        private bool stopping;
        private bool disposed;
        private bool watchdogStarted;
        private int activeControllerPort;
        private int activeMixedPort;
        private volatile string tunStartupError;
        public static string DefaultDataDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string current = Path.Combine(root, "cute-clash");
            string legacy = Path.Combine(root, "win7-clash");
            // Reuse existing profiles and recovery journals without a destructive migration.
            return !File.Exists(Path.Combine(current, "settings.json")) && File.Exists(Path.Combine(legacy, "settings.json")) ? legacy : current;
        }
        public AppController() : this(DefaultDataDirectory(), null) { }
        public AppController(string dataDirectory, string explicitCorePath)
        {
            DataDirectory = Path.GetFullPath(dataDirectory);
            Directory.CreateDirectory(DataDirectory);
            RestrictDataDirectory(DataDirectory);
            coreHome = Path.Combine(DataDirectory, "core"); Directory.CreateDirectory(coreHome);
            corePath = explicitCorePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core", "mihomo.exe");
            store = new ProfileStore(DataDirectory);
            systemProxy = new SystemProxy(DataDirectory);
            systemProxy.Restore();
            Settings = store.LoadSettings();
            Localization.Language = Settings.Language;
            // A launch never changes routing until the user explicitly connects.
            Settings.SystemProxyEnabled = false;
            byte[] token = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(token);
            secret = Convert.ToBase64String(token);
            api = new HttpClient(new HttpClientHandler { UseProxy = false });
            api.Timeout = TimeSpan.FromSeconds(9);
            api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
            CoreVersion = "Mihomo v1.19.31";
        }
        private static void RestrictDataDirectory(string path)
        {
            var current = WindowsIdentity.GetCurrent().User;
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(current, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
#if NET6_0
            new DirectoryInfo(path).SetAccessControl(security);
#else
            Directory.SetAccessControl(path, security);
#endif
        }
        public void SaveSettings()
        {
            if (IsRunning && (Settings.MixedPort != activeMixedPort || Settings.ControllerPort != activeControllerPort))
                throw new InvalidOperationException(Localization.T("修改端口前请先断开连接。", "Disconnect before changing ports."));
            store.SaveSettings(Settings); Changed();
        }
        private async Task Exclusive(Func<Task> action)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { if (disposed) throw new ObjectDisposedException("AppController"); await action().ConfigureAwait(false); LastError = null; }
            catch (Exception ex) { LastError = Redact(ex.Message); WriteLog(Localization.T("错误：", "Error: ") + LastError); throw new InvalidOperationException(LastError, ex); }
            finally { gate.Release(); Changed(); }
        }
        public Task StartAsync() { return Exclusive(StartInternalAsync); }
        public Task StopAsync() { return Exclusive(delegate { StopInternal(); return Task.FromResult(0); }); }
        public Task RestartAsync() { return Exclusive(async delegate { StopInternal(); await StartInternalAsync().ConfigureAwait(false); }); }
        private void EnsureCore()
        {
            if (!File.Exists(corePath)) throw new FileNotFoundException(Localization.T("未找到 core\\mihomo.exe，请重新解压完整的软件包。", "core\\mihomo.exe was not found. Extract the complete application package again."));
        }
        private async Task StartInternalAsync()
        {
            if (IsRunning) return;
            if (core != null || job != null) StopInternal();
            EnsureCore();
            if (String.IsNullOrEmpty(Settings.SelectedProfileId)) throw new InvalidOperationException(Localization.T("请先在配置页面导入并选择 Clash / Mihomo YAML 配置。", "Import and select a Clash / Mihomo YAML profile on the Profiles page first."));
            if (Settings.TunEnabled && !IsAdmin) throw new InvalidOperationException(Localization.T("TUN 需要管理员权限。请断开连接后使用设置页的管理员重启。", "TUN requires administrator privileges. Disconnect, then restart as administrator from Settings."));
            if (Settings.TunEnabled && !File.Exists(Path.Combine(Path.GetDirectoryName(corePath), "wintun.dll")))
                throw new FileNotFoundException(Localization.T("缺少与内核架构匹配的 wintun.dll，请重新解压完整软件包。", "wintun.dll matching the core architecture is missing. Extract the complete application package again."));
            string runtime = store.BuildRuntimeConfig(Settings, secret, Path.Combine(coreHome, "runtime.yaml"));
            await ValidateConfigFileAsync(runtime).ConfigureAwait(false);
            CheckPort(Settings.MixedPort); CheckPort(Settings.ControllerPort);
            activeControllerPort = Settings.ControllerPort; activeMixedPort = Settings.MixedPort;
            stopping = false; ready = false; tunStartupError = null;
            try
            {
                job = new ProcessJob();
                core = new Process { StartInfo = CoreStartInfo("-d " + Quote(coreHome) + " -f " + Quote(runtime)), EnableRaisingEvents = true };
                core.OutputDataReceived += CoreOutput;
                core.ErrorDataReceived += CoreOutput;
                core.Exited += CoreExited;
                core.Start(); job.Add(core); core.BeginOutputReadLine(); core.BeginErrorReadLine();
                DateTime deadline = DateTime.UtcNow.AddSeconds(25);
                Exception last = null; bool apiReady = false;
                while (DateTime.UtcNow < deadline)
                {
                    if (core.HasExited) throw new InvalidOperationException(Localization.T("内核启动失败，请查看运行日志。TUN 驱动安装失败时请检查 Windows 7 的 SHA-2 补丁和管理员权限。", "The core failed to start. Check the logs. If the TUN driver failed to install, check Windows 7 SHA-2 updates and administrator privileges."));
                    try
                    {
                        var version = await ApiAsync("GET", "/version", null).ConfigureAwait(false);
                        CoreVersion = "Mihomo " + ValueString(version, "version"); apiReady = true; break;
                    }
                    catch (Exception ex) { last = ex; }
                    await Task.Delay(200).ConfigureAwait(false);
                }
                if (!apiReady) throw new InvalidOperationException(Localization.T("内核控制接口启动超时。", "The core control API did not start in time. ") + (last == null ? "" : last.Message));
                bool inboundReady = false; DateTime inboundDeadline = DateTime.UtcNow.AddSeconds(35);
                while (DateTime.UtcNow < inboundDeadline)
                {
                    if (Settings.TunEnabled && tunStartupError != null) throw new InvalidOperationException(Localization.T("TUN 初始化失败：", "TUN initialization failed: ") + tunStartupError);
                    if (core.HasExited) throw new InvalidOperationException(Localization.T("内核在网络初始化时退出。", "The core exited during network initialization."));
                    var actual = await ApiAsync("GET", "/configs", null).ConfigureAwait(false);
                    var tun = actual.ContainsKey("tun") ? actual["tun"] as Dictionary<string, object> : null;
                    bool mixedReady = actual.ContainsKey("mixed-port") && Convert.ToInt32(actual["mixed-port"]) == activeMixedPort;
                    bool tunReady = !Settings.TunEnabled || (tun != null && tun.ContainsKey("enable") && Convert.ToBoolean(tun["enable"]));
                    if (mixedReady && tunReady) { inboundReady = true; break; }
                    await Task.Delay(250).ConfigureAwait(false);
                }
                if (!inboundReady) throw new InvalidOperationException(Localization.T("代理端口或 TUN 网卡初始化超时，请查看日志并检查端口、Wintun、Windows 更新和管理员权限。", "The proxy port or TUN adapter did not initialize in time. Check the logs, ports, Wintun, Windows updates, and administrator privileges."));
                ready = true;
                if (Settings.SystemProxyEnabled) { StartWatchdog(); systemProxy.Enable(activeMixedPort); }
                store.SaveSettings(Settings);
                WriteLog(Settings.TunEnabled ? Localization.T("已连接，TUN 已请求启用。请结合日志及实际联网确认网卡和路由工作正常。", "Connected; TUN activation was requested. Check the logs and test connectivity to confirm the adapter and routing work correctly.") : Localization.T("已连接，HTTP / SOCKS5 监听 127.0.0.1:", "Connected. HTTP / SOCKS5 listening on 127.0.0.1:") + activeMixedPort);
            }
            catch { StopInternal(); throw; }
            Changed();
        }
        private void CoreOutput(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;
            if (Object.ReferenceEquals(sender, core) && args.Data.IndexOf("Start TUN listening error:", StringComparison.OrdinalIgnoreCase) >= 0)
                tunStartupError = Redact(args.Data);
            WriteLog(args.Data);
        }
        private void CoreExited(object sender, EventArgs args)
        {
            Task.Run(async delegate
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (stopping || disposed || !Object.ReferenceEquals(sender, core)) return;
                    try { StopInternal(); } catch (Exception ex) { WriteLog(Localization.T("系统代理恢复失败：", "System proxy recovery failed: ") + ex.Message); }
                    LastError = Localization.T("内核已退出。请查看日志后重新连接。", "The core exited. Check the logs, then reconnect.");
                    WriteLog(LastError); Changed();
                }
                finally { gate.Release(); }
            });
        }
        private void StopInternal()
        {
            stopping = true; ready = false;
            Exception restoreError = null;
            try { systemProxy.Restore(); } catch (Exception ex) { restoreError = ex; }
            if (core != null)
            {
                core.Exited -= CoreExited;
                try { if (!core.HasExited) { core.Kill(); core.WaitForExit(5000); } } catch (InvalidOperationException) { }
                finally { core.Dispose(); core = null; }
            }
            if (job != null) { job.Dispose(); job = null; }
            string runtime = Path.Combine(coreHome, "runtime.yaml");
            if (File.Exists(runtime)) File.Delete(runtime);
            if (File.Exists(runtime + ".bak")) File.Delete(runtime + ".bak");
            if (restoreError != null) throw new InvalidOperationException(Localization.T("无法恢复系统代理，恢复记录已保留。请重新启动程序恢复，或在系统 Internet 选项中手动检查。", "The system proxy could not be restored. The recovery record was preserved. Restart the app to recover, or check Windows Internet Options manually."), restoreError);
        }
        private ProcessStartInfo CoreStartInfo(string arguments)
        {
            var info = new ProcessStartInfo(corePath, arguments) { WorkingDirectory = coreHome, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            // Do not inherit an expanded filesystem allow-list from an unrelated shell.
            info.EnvironmentVariables.Remove("SAFE_PATHS");
            foreach (string key in info.EnvironmentVariables.Keys.Cast<string>().ToArray())
                if (key.StartsWith("CLASH_", StringComparison.OrdinalIgnoreCase)) info.EnvironmentVariables.Remove(key);
            // Provider cache directory is explicitly managed by this application.
            info.EnvironmentVariables["SAFE_PATHS"] = Path.Combine(DataDirectory, "providers");
            return info;
        }
        internal async Task ValidateConfigFileAsync(string config)
        {
            EnsureCore();
            using (var process = new Process { StartInfo = CoreStartInfo("-t -d " + Quote(coreHome) + " -f " + Quote(config)) })
            using (var validationJob = new ProcessJob())
            {
                process.Start();
                try { validationJob.Add(process); }
                catch { try { process.Kill(); } catch { } throw; }
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                bool exited = await Task.Run(delegate { return process.WaitForExit(45000); }).ConfigureAwait(false);
                if (!exited) { process.Kill(); throw new TimeoutException(Localization.T("配置校验超时；如使用 GEO 数据库或远程规则，请检查下载连接。", "Profile validation timed out. If the profile uses GEO databases or remote rules, check their download connections.")); }
                string output = (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false));
                if (process.ExitCode != 0) throw new InvalidDataException(Localization.T("Mihomo 配置校验失败：\r\n", "Mihomo profile validation failed:\r\n") + Redact(output));
            }
        }
        public Task ValidateProfileAsync(string id) { return Exclusive(delegate { return ValidateCandidateAsync(id); }); }
        private async Task ValidateCandidateAsync(string id)
        {
            var temporarySettings = CloneSettings(); temporarySettings.SelectedProfileId = id;
            if (!temporarySettings.Profiles.Any(p => p.Id == id)) temporarySettings.Profiles.Add(new ProfileInfo { Id = id, Name = Localization.T("待校验配置", "Profile to validate"), UpdatedAt = DateTime.UtcNow });
            // -t checks the TUN schema but does not open a TUN adapter.
            string path = Path.Combine(coreHome, "check-" + Guid.NewGuid().ToString("N") + ".yaml");
            try { store.BuildRuntimeConfig(temporarySettings, secret, path); await ValidateConfigFileAsync(path).ConfigureAwait(false); }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        private AppSettings CloneSettings() { var json = new JavaScriptSerializer(); return json.Deserialize<AppSettings>(json.Serialize(Settings)); }
        public Task ImportFileAsync(string path)
        {
            return Exclusive(async delegate
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length > 8 * 1024 * 1024) throw new InvalidDataException(Localization.T("配置文件不存在或超过 8 MiB。", "The profile file does not exist or exceeds 8 MiB."));
                await ImportInternalAsync(Path.GetFileNameWithoutExtension(path), null, File.ReadAllText(path, Encoding.UTF8)).ConfigureAwait(false);
            });
        }
        public Task ImportUrlAsync(string name, string url)
        {
            return Exclusive(async delegate { string text = await DownloadSubscriptionAsync(url).ConfigureAwait(false); await ImportInternalAsync(name, url, text).ConfigureAwait(false); });
        }
        private async Task ImportInternalAsync(string name, string url, string text)
        {
            ProfileInfo profile = store.ImportText(name, url, text);
            string oldSelection = Settings.SelectedProfileId;
            try
            {
                await ValidateCandidateAsync(profile.Id).ConfigureAwait(false);
                Settings.Profiles.Add(profile);
                if (String.IsNullOrEmpty(Settings.SelectedProfileId)) Settings.SelectedProfileId = profile.Id;
                store.SaveSettings(Settings); WriteLog(Localization.T("已导入配置：", "Imported profile: ") + profile.Name);
            }
            catch { Settings.Profiles.Remove(profile); Settings.SelectedProfileId = oldSelection; store.Delete(profile.Id); throw; }
        }
        public Task UpdateProfileAsync(string id)
        {
            return Exclusive(async delegate
            {
                ProfileInfo original = FindProfile(id);
                if (String.IsNullOrEmpty(original.SourceUrl)) throw new InvalidOperationException(Localization.T("这是本地文件配置；请重新导入更新后的 YAML。", "This profile comes from a local file. Import the updated YAML again."));
                string content = await DownloadSubscriptionAsync(original.SourceUrl).ConfigureAwait(false);
                ProfileInfo candidate = store.ImportText(original.Name, null, content);
                try { await ValidateCandidateAsync(candidate.Id).ConfigureAwait(false); }
                finally { store.Delete(candidate.Id); }
                string previous = File.ReadAllText(store.GetProfilePath(id)); DateTime oldDate = original.UpdatedAt;
                bool restart = IsRunning && Settings.SelectedProfileId == id;
                try
                {
                    if (restart) StopInternal();
                    store.ReplaceText(id, content); original.UpdatedAt = DateTime.UtcNow;
                    if (restart) await StartInternalAsync().ConfigureAwait(false);
                    store.SaveSettings(Settings); WriteLog(Localization.T("已更新配置：", "Updated profile: ") + original.Name);
                }
                catch
                {
                    store.ReplaceText(id, previous); original.UpdatedAt = oldDate; store.SaveSettings(Settings);
                    if (restart) { try { StartInternalAsync().GetAwaiter().GetResult(); } catch (Exception ex) { WriteLog(Localization.T("旧配置恢复连接失败：", "Could not reconnect using the previous profile: ") + ex.Message); } }
                    throw;
                }
            });
        }
        public Task SelectProfileAsync(string id)
        {
            return Exclusive(async delegate
            {
                FindProfile(id); await ValidateCandidateAsync(id).ConfigureAwait(false);
                string old = Settings.SelectedProfileId; bool restart = IsRunning;
                try { if (restart) StopInternal(); Settings.SelectedProfileId = id; if (restart) await StartInternalAsync().ConfigureAwait(false); store.SaveSettings(Settings); }
                catch { Settings.SelectedProfileId = old; store.SaveSettings(Settings); if (restart) { try { StartInternalAsync().GetAwaiter().GetResult(); } catch (Exception ex) { WriteLog(ex.Message); } } throw; }
            });
        }
        public Task DeleteProfileAsync(string id)
        {
            return Exclusive(delegate
            {
                var profile = FindProfile(id);
                if (IsRunning && id == Settings.SelectedProfileId) throw new InvalidOperationException(Localization.T("请先断开连接，再删除正在使用的配置。", "Disconnect before deleting the active profile."));
                Settings.Profiles.Remove(profile);
                if (Settings.SelectedProfileId == id) Settings.SelectedProfileId = Settings.Profiles.Count > 0 ? Settings.Profiles[0].Id : null;
                store.SaveSettings(Settings); store.Delete(id); return Task.FromResult(0);
            });
        }
        private ProfileInfo FindProfile(string id)
        {
            var profile = Settings.Profiles.FirstOrDefault(p => p.Id == id);
            if (profile == null) throw new InvalidOperationException(Localization.T("所选配置不存在。", "The selected profile does not exist.")); return profile;
        }
        public Task SetSystemProxyAsync(bool enabled)
        {
            return Exclusive(delegate
            {
                if (enabled && IsRunning) { StartWatchdog(); systemProxy.Enable(activeMixedPort); }
                else if (!enabled) systemProxy.Restore();
                Settings.SystemProxyEnabled = enabled; store.SaveSettings(Settings); return Task.FromResult(0);
            });
        }
        public Task SetTunAsync(bool enabled)
        {
            return Exclusive(async delegate
            {
                if (enabled && !IsAdmin) throw new InvalidOperationException(Localization.T("TUN 需要管理员权限。请使用设置页中的管理员重启后开启。", "TUN requires administrator privileges. Restart as administrator from Settings before enabling TUN."));
                bool old = Settings.TunEnabled, restart = IsRunning;
                try { if (restart) StopInternal(); Settings.TunEnabled = enabled; if (restart) await StartInternalAsync().ConfigureAwait(false); store.SaveSettings(Settings); }
                catch { Settings.TunEnabled = old; store.SaveSettings(Settings); if (restart) { try { StartInternalAsync().GetAwaiter().GetResult(); } catch (Exception ex) { WriteLog(ex.Message); } } throw; }
            });
        }
        public Task SetModeAsync(string mode)
        {
            return Exclusive(async delegate
            {
                if (mode != "rule" && mode != "global" && mode != "direct") throw new ArgumentException(Localization.T("无效的代理模式。", "Invalid proxy mode."));
                if (IsRunning) await ApiAsync("PATCH", "/configs", new { mode = mode }).ConfigureAwait(false);
                Settings.Mode = mode; store.SaveSettings(Settings);
            });
        }
        public async Task<IList<ProxyGroup>> GetGroupsAsync()
        {
            if (!IsRunning) return new List<ProxyGroup>();
            var data = await ApiAsync("GET", "/proxies", null).ConfigureAwait(false);
            var groups = new List<ProxyGroup>();
            var proxies = data["proxies"] as Dictionary<string, object>;
            if (proxies != null) foreach (var item in proxies)
            {
                var p = item.Value as Dictionary<string, object>;
                if (p == null || !p.ContainsKey("all")) continue;
                var nodes = new List<string>(); var all = p["all"] as IEnumerable;
                if (all != null) foreach (var node in all) nodes.Add(Convert.ToString(node));
                groups.Add(new ProxyGroup { Name = item.Key, Type = ValueString(p, "type"), Current = ValueString(p, "now"), Nodes = nodes });
            }
            return groups.OrderBy(g => g.Name == "GLOBAL" ? 1 : 0).ThenBy(g => g.Name).ToList();
        }
        public async Task SelectProxyAsync(string group, string node)
        {
            if (!IsRunning) throw new InvalidOperationException(Localization.T("请先连接内核。", "Connect to the core first."));
            await ApiAsync("PUT", "/proxies/" + Uri.EscapeDataString(group), new { name = node }).ConfigureAwait(false);
            Changed();
        }
        public async Task<int> TestDelayAsync(string node)
        {
            if (!IsRunning) throw new InvalidOperationException(Localization.T("请先连接内核。", "Connect to the core first."));
            var result = await ApiAsync("GET", "/proxies/" + Uri.EscapeDataString(node) + "/delay?timeout=5000&url=" + Uri.EscapeDataString("https://www.gstatic.com/generate_204"), null).ConfigureAwait(false);
            return Convert.ToInt32(result["delay"]);
        }
        public async Task<RuntimeSnapshot> GetSnapshotAsync()
        {
            if (!IsRunning) return new RuntimeSnapshot { Version = CoreVersion };
            var data = await ApiAsync("GET", "/connections", null).ConfigureAwait(false);
            var connections = data.ContainsKey("connections") ? data["connections"] as ICollection : null;
            return new RuntimeSnapshot { Version = CoreVersion, UploadTotal = Convert.ToInt64(data["uploadTotal"]), DownloadTotal = Convert.ToInt64(data["downloadTotal"]), Connections = connections == null ? 0 : connections.Count };
        }
        private async Task<Dictionary<string, object>> ApiAsync(string method, string path, object payload)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            using (var request = new HttpRequestMessage(new HttpMethod(method), "http://127.0.0.1:" + activeControllerPort + path))
            {
                if (payload != null) request.Content = new StringContent(json.Serialize(payload), Encoding.UTF8, "application/json");
                using (var response = await api.SendAsync(request).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Localization.Format("内核 API {0}：{1}", "Core API {0}: {1}", (int)response.StatusCode, Redact(body)));
                    return String.IsNullOrWhiteSpace(body) ? new Dictionary<string, object>() : json.Deserialize<Dictionary<string, object>>(body);
                }
            }
        }
        private static async Task<string> DownloadSubscriptionAsync(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new ArgumentException(Localization.T("订阅地址必须是 HTTPS 或 HTTP URL。", "The subscription address must be an HTTPS or HTTP URL."));
            using (var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }))
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("cute-clash/0.2.0 Clash.Meta");
                try
                {
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) throw new InvalidDataException(Localization.T("订阅超过 8 MiB。", "The subscription exceeds 8 MiB."));
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var memory = new MemoryStream())
                        {
                            byte[] buffer = new byte[16384]; int count;
                            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation.Token).ConfigureAwait(false)) > 0)
                            {
                                if (memory.Length + count > 8 * 1024 * 1024) throw new InvalidDataException(Localization.T("订阅超过 8 MiB。", "The subscription exceeds 8 MiB."));
                                memory.Write(buffer, 0, count);
                            }
                            return Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF');
                        }
                    }
                }
                catch (Exception ex) { throw new InvalidOperationException(Localization.T("订阅下载失败。请检查地址、网络和 Windows 的 TLS 1.2 / 根证书更新。", "Could not download the subscription. Check its address, your network, and Windows TLS 1.2 / root certificate updates. ") + (ex is InvalidDataException ? ex.Message : "")); }
            }
        }
        private void StartWatchdog()
        {
            if (watchdogStarted) return;
            using (var self = Process.GetCurrentProcess())
            {
                var info = new ProcessStartInfo(WatchdogExecutablePath(System.Reflection.Assembly.GetEntryAssembly().Location),
                    "--watchdog " + self.Id + " " + self.StartTime.ToUniversalTime().Ticks + " " + Quote(DataDirectory))
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                using (var guard = Process.Start(info)) { if (guard == null) throw new InvalidOperationException(Localization.T("无法启动代理恢复保护进程。", "Could not start the proxy recovery watchdog.")); }
            }
            watchdogStarted = true;
        }
        internal static string WatchdogExecutablePath(string entryAssemblyLocation)
        {
#if NET6_0
            // The managed entry assembly is a DLL in a self-contained publish.
            // Relaunch its adjacent apphost so recovery does not require an
            // installed dotnet command or a DLL shell association.
            return Path.ChangeExtension(entryAssemblyLocation, ".exe");
#else
            return entryAssemblyLocation;
#endif
        }
        public static void RunWatchdog(int parentId, long parentTicks, string dataDirectory)
        {
            try
            {
                using (var parent = Process.GetProcessById(parentId))
                { if (parent.StartTime.ToUniversalTime().Ticks == parentTicks) parent.WaitForExit(); }
            }
            catch (ArgumentException) { }
            // Single-instance mutex keeps a new GUI from racing this recovery.
            using (var mutex = new Mutex(false, Program.MutexName))
            {
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(15000); } catch (AbandonedMutexException) { owned = true; }
                    if (owned) new SystemProxy(dataDirectory).Restore();
                }
                catch { /* Journal remains for next GUI launch; never show a hidden-process dialog. */ }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }
        public void OpenDataFolder() { Process.Start(new ProcessStartInfo("explorer.exe", Quote(DataDirectory)) { UseShellExecute = true }); }
        internal static string Quote(string text) { return "\"" + text.Replace("\"", "") + "\""; }
        internal static string Redact(string text)
        {
            if (String.IsNullOrEmpty(text)) return "";
            text = Regex.Replace(text, "https?://[^\\s\\\"'<>]+", Localization.T("[URL 已隐藏]", "[URL hidden]"), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "(?i)(password|secret|token|uuid|private-key)([\\s:=]+)[^\\s,}]+", Localization.T("$1$2[已隐藏]", "$1$2[hidden]"));
            return text.Length > 12000 ? text.Substring(0, 12000) + "…" : text;
        }
        private void WriteLog(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + Redact(message);
            lock (logLock)
            {
                try
                {
                    string path = Path.Combine(DataDirectory, "app.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) { string old = path + ".1"; if (File.Exists(old)) File.Delete(old); File.Move(path, old); }
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch { }
            }
            var handler = Log; if (handler != null) { try { handler(line); } catch { } }
        }
        private void Changed() { var handler = StateChanged; if (handler != null) { try { handler(); } catch { } } }
        private static string ValueString(Dictionary<string, object> values, string key) { return values.ContainsKey(key) ? Convert.ToString(values[key]) : ""; }
        private static void CheckPort(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try { listener.Server.ExclusiveAddressUse = true; listener.Start(); }
            catch (SocketException) { throw new InvalidOperationException(Localization.Format("本机端口 {0} 已被占用，请在设置中改用其他端口。", "Local port {0} is already in use. Choose another port in Settings.", port)); }
            finally { listener.Stop(); }
        }
        public void Dispose()
        {
            if (disposed) return;
            gate.Wait();
            try { if (disposed) return; disposed = true; StopInternal(); api.Dispose(); }
            finally { gate.Release(); }
        }
    }
}
