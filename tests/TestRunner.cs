using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CuteClash.Tests
{
    internal static class TestRunner
    {
        private static int passed;
        [STAThread]
        public static int Main(string[] args)
        {
            string project = args[0];
            string architecture = IntPtr.Size == 8 ? "x64" : "x86";
            string scratch = Path.Combine(project, "artifacts", "tests", "run-" + architecture + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(scratch);
            try
            {
                passed += ProfileTests.Run(Path.Combine(scratch, "profiles"));
                Console.WriteLine("PASS profile parsing, bounds, normalization, persistence: " + passed + " groups");
                passed += LocalizationTests.Run(Path.Combine(scratch, "localization"));
                Console.WriteLine("PASS Chinese/English normalization and persisted language settings");
                TestProxyRecovery(Path.Combine(scratch, "recovery"));
                var native = new WinInetProxyBackend().Read();
                Assert(native != null, "Native WinINet snapshot can be read (no write)");
                Task runtime = TestRuntime(project, Path.Combine(scratch, "runtime"));
                if (!runtime.Wait(90000)) throw new TimeoutException("Integration test exceeded 90 seconds");
                runtime.GetAwaiter().GetResult();
                Console.WriteLine("PASS all " + passed + " assertions/groups; TUN activation and Windows7 guest NOT tested.");
                File.WriteAllText(Path.Combine(project, "artifacts", "tests", "result-" + architecture + ".txt"), "Passed " + passed + " assertions/groups for " + architecture + " at " + DateTime.Now.ToString("O") + " on " + Environment.OSVersion + ".\r\nReal core HTTP/SOCKS5, API, config checks, process cleanup; fake-backend proxy recovery. No host proxy changes or TUN activation. Windows7 guest not tested.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void Assert(bool value, string name)
        {
            if (!value) throw new Exception("FAIL " + name);
            passed++; Console.WriteLine("PASS " + name);
        }
        private static void TestProxyRecovery(string directory)
        {
            var original = new ProxySnapshot { Flags = 13, Server = "old-proxy:8080", Bypass = "*.example", Pac = "https://example.test/proxy.pac" };
            var backend = new FakeProxy { Current = original };
            var proxy = new SystemProxy(directory, backend);
            proxy.Enable(34567);
            Assert(backend.Current.Server == "127.0.0.1:34567" && backend.Current.Flags == 3 && proxy.HasJournal, "Apply proxy records PAC/autodetect backup first");
            Assert(new SystemProxy(directory, backend).Restore() && backend.Current.SameAs(original), "Crash recovery restores prior PAC, flags and proxy");
            proxy.Enable(34567);
            backend.Current = new ProxySnapshot { Flags = 3, Server = "other-app:8000" };
            Assert(!proxy.Restore() && backend.Current.Server == "other-app:8000", "Recovery preserves another application's settings");
            backend.Current = original; backend.FailNextWrite = true;
            try { proxy.Enable(34567); throw new Exception("Expected simulated write failure"); } catch (IOException) { }
            Assert(proxy.HasJournal && backend.Current.SameAs(original), "Failed proxy write keeps recovery journal");
            proxy.Restore();
            Assert(!proxy.HasJournal, "Idempotent recovery clears journal");
        }
        private sealed class FakeProxy : IProxyBackend
        {
            public ProxySnapshot Current; public bool FailNextWrite;
            public ProxySnapshot Read() { return Current; }
            public void Write(ProxySnapshot snapshot) { if (FailNextWrite) { FailNextWrite = false; throw new IOException("simulated failure"); } Current = snapshot; }
        }
        private static async Task TestRuntime(string project, string scratch)
        {
            string core = Path.Combine(project, "dependencies", "mihomo", IntPtr.Size == 8 ? "x64" : "x86", "mihomo.exe");
            using (var controller = new AppController(scratch, core))
            {
                controller.Log += delegate(string s) { Console.WriteLine("  " + s); };
                controller.Settings.MixedPort = FreePort(); controller.Settings.ControllerPort = FreePort();
                Environment.SetEnvironmentVariable("CLASH_OVERRIDE_SECRET", "inherited-unsafe-secret");
                var startInfo = (System.Diagnostics.ProcessStartInfo)typeof(AppController).GetMethod("CoreStartInfo", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { "-v" });
                Environment.SetEnvironmentVariable("CLASH_OVERRIDE_SECRET", null);
                Assert(!startInfo.EnvironmentVariables.ContainsKey("CLASH_OVERRIDE_SECRET"), "Inherited core overrides removed");
                while (controller.Settings.MixedPort == controller.Settings.ControllerPort) controller.Settings.ControllerPort = FreePort();
                await controller.ImportFileAsync(Path.Combine(project, "examples", "direct-test.yaml"));
                Assert(controller.Settings.Profiles.Count == 1, "Validated file import commits profile");
                string invalid = Path.Combine(scratch, "invalid.yaml"); File.WriteAllText(invalid, "proxies:\n  - name: invalid\n    type: not-a-protocol\nrules: [MATCH,DIRECT]\n");
                bool rejected = false; try { await controller.ImportFileAsync(invalid); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected && controller.Settings.Profiles.Count == 1, "Invalid core config rejected without damaging existing profile");
                await controller.StartAsync();
                Assert(controller.IsRunning && controller.CoreVersion.Contains("1.19.31"), "Real Mihomo core starts and authenticates");
                using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }))
                {
                    var response = await client.GetAsync("http://127.0.0.1:" + controller.Settings.ControllerPort + "/version");
                    Assert(response.StatusCode == HttpStatusCode.Unauthorized, "Controller refuses unauthenticated API requests"); response.Dispose();
                }
                var groups = await controller.GetGroupsAsync();
                var group = groups.FirstOrDefault(g => g.Name == "测试策略");
                Assert(group != null && group.Nodes.Contains("DIRECT"), "Real proxy groups loaded");
                await controller.SelectProxyAsync(group.Name, "REJECT");
                Assert((await controller.GetGroupsAsync()).First(g => g.Name == group.Name).Current == "REJECT", "Node selection reaches real core");
                await controller.SelectProxyAsync(group.Name, "DIRECT");
                await controller.SetModeAsync("global"); await controller.SetModeAsync("rule");
                Assert(controller.Settings.Mode == "rule", "Rule/global mode API changes succeed");
                await CheckHttpProxy(controller.Settings.MixedPort);
                await CheckSocksProxy(controller.Settings.MixedPort);
                var snapshot = await controller.GetSnapshotAsync();
                Assert(snapshot.DownloadTotal > 0, "Traffic counters reflect real proxy requests");
                int mixed = controller.Settings.MixedPort, api = controller.Settings.ControllerPort;
                await controller.StopAsync();
                Assert(!controller.IsRunning && CanBind(mixed) && CanBind(api), "Stop releases both listening ports");
                Assert(!File.Exists(Path.Combine(scratch, "core", "runtime.yaml")), "Runtime controller secret removed on stop");
                controller.Settings.TunEnabled = true;
                await controller.ValidateProfileAsync(controller.Settings.SelectedProfileId);
                Assert(true, "Real core validates TUN gVisor config with no route/driver activation");
                controller.Settings.TunEnabled = false;
                // Check subscription import using a fixture on localhost, never an external account.
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task served = ServeOnce(listener, File.ReadAllText(Path.Combine(project, "examples", "direct-test.yaml")));
                await controller.ImportUrlAsync("本地测试订阅", "http://127.0.0.1:" + port + "/fixture.yaml");
                await served;
                Assert(controller.Settings.Profiles.Count == 2 && controller.Settings.Profiles[1].SourceUrl != null, "Clash subscription URL download/import works");
                string subscriptionId = controller.Settings.Profiles[1].Id;
                await controller.SelectProfileAsync(subscriptionId);
                await controller.StartAsync();
                string subscribedPath = new ProfileStore(scratch).GetProfilePath(subscriptionId);
                string before = File.ReadAllText(subscribedPath);
                var badUpdate = new TcpListener(IPAddress.Loopback, port); badUpdate.Start();
                Task badServed = ServeOnce(badUpdate, "proxies: [{name: bad, type: no-such-type}]\nrules: ['MATCH,DIRECT']\n");
                bool updateRejected = false; try { await controller.UpdateProfileAsync(subscriptionId); } catch (InvalidOperationException) { updateRejected = true; }
                await badServed;
                Assert(updateRejected && File.ReadAllText(subscribedPath) == before && controller.IsRunning, "Invalid subscription update preserves active connection and original profile");
                var goodUpdate = new TcpListener(IPAddress.Loopback, port); goodUpdate.Start();
                Task goodServed = ServeOnce(goodUpdate, File.ReadAllText(Path.Combine(project, "examples", "direct-test.yaml")).Replace("测试策略", "更新策略"));
                await controller.UpdateProfileAsync(subscriptionId); await goodServed;
                Assert(controller.IsRunning && (await controller.GetGroupsAsync()).Any(g => g.Name == "更新策略"), "Valid subscription update reloads real running core");
                var process = (System.Diagnostics.Process)typeof(AppController).GetField("core", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
                process.Kill(); process.WaitForExit(); await Task.Delay(300);
                Assert(!controller.IsRunning, "Unexpected core exit reflected in state");
                await controller.StartAsync();
                Assert(controller.IsRunning, "Core can restart after crash");
                await controller.StopAsync();
            }
        }
        private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port; }
        private static bool CanBind(int port) { var l = new TcpListener(IPAddress.Loopback, port); try { l.Start(); return true; } catch (SocketException) { return false; } finally { l.Stop(); } }
        private static async Task ServeOnce(TcpListener listener, string body)
        {
            try
            {
                using (var accepted = await listener.AcceptTcpClientAsync())
                using (var stream = accepted.GetStream())
                {
                    accepted.ReceiveTimeout = 8000;
                    var request = new StringBuilder(); byte[] one = new byte[1];
                    while (request.Length < 16384)
                    { int n = await stream.ReadAsync(one, 0, 1); if (n == 0) break; request.Append((char)one[0]); if (request.ToString().EndsWith("\r\n\r\n")) break; }
                    byte[] data = Encoding.UTF8.GetBytes(body);
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + data.Length + "\r\nConnection: close\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length); await stream.WriteAsync(data, 0, data.Length);
                }
            }
            finally { listener.Stop(); }
        }
        private static async Task CheckHttpProxy(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int endpointPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task served = ServeOnce(listener, "cute-clash-http-ok");
            using (var tcp = new TcpClient())
            {
                await tcp.ConnectAsync(IPAddress.Loopback, port);
                using (var stream = tcp.GetStream())
                {
                    byte[] req = Encoding.ASCII.GetBytes("GET http://127.0.0.1:" + endpointPort + "/ HTTP/1.1\r\nHost: 127.0.0.1:" + endpointPort + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(req, 0, req.Length);
                    string text = await new StreamReader(stream).ReadToEndAsync();
                    Assert(text.Contains("cute-clash-http-ok"), "Actual HTTP proxy forwards local request");
                }
            }
            await served;
        }
        private static async Task CheckSocksProxy(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int endpointPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task served = ServeOnce(listener, "cute-clash-socks-ok");
            using (var tcp = new TcpClient())
            {
                await tcp.ConnectAsync(IPAddress.Loopback, port);
                using (var stream = tcp.GetStream())
                {
                    await stream.WriteAsync(new byte[] { 5, 1, 0 }, 0, 3);
                    byte[] hello = await ReadExact(stream, 2); Assert(hello[0] == 5 && hello[1] == 0, "SOCKS5 method negotiation succeeds");
                    byte[] connect = { 5, 1, 0, 1, 127, 0, 0, 1, (byte)(endpointPort >> 8), (byte)(endpointPort & 255) };
                    await stream.WriteAsync(connect, 0, connect.Length);
                    byte[] response = await ReadExact(stream, 4); Assert(response[1] == 0, "SOCKS5 CONNECT succeeds");
                    if (response[3] == 1) await ReadExact(stream, 6); else if (response[3] == 4) await ReadExact(stream, 18); else { byte[] len = await ReadExact(stream, 1); await ReadExact(stream, len[0] + 2); }
                    byte[] req = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(req, 0, req.Length);
                    string text = await new StreamReader(stream).ReadToEndAsync();
                    Assert(text.Contains("cute-clash-socks-ok"), "Actual SOCKS5 proxy forwards local request");
                }
            }
            await served;
        }
        private static async Task<byte[]> ReadExact(NetworkStream stream, int size)
        {
            byte[] result = new byte[size]; int offset = 0;
            while (offset < size) { int n = await stream.ReadAsync(result, offset, size - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
            return result;
        }
    }
}
