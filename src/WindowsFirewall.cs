using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace CuteClash
{
    // Windows 7 has netsh advfirewall but not the PowerShell NetSecurity module.
    // See Clash Party PR #1788, merge 9ea9da7d586d2fd5115fc7cfeb42b2825210cb8e.
    // Rules are restricted to this executable, inbound TCP/UDP, without edge traversal.
    internal sealed class WindowsFirewall
    {
        private readonly string corePath;
        private readonly IFirewallCommandRunner runner;
        private readonly bool requireElevation;
        private readonly HashSet<string> activeProtocols = new HashSet<string>(StringComparer.Ordinal);
        internal string RulePrefix { get; private set; }

        public WindowsFirewall(string path) : this(path, new NetshFirewallCommandRunner(), true) { }

        internal WindowsFirewall(string path, IFirewallCommandRunner commandRunner, bool checkElevation)
        {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
                path.IndexOf('"') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0 || path.IndexOf('\0') >= 0)
                throw new ArgumentException("The firewall target must be an absolute local executable path.", "path");
            corePath = Path.GetFullPath(path);
            if (!String.Equals(Path.GetExtension(corePath), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The firewall target must be an executable.", "path");
            if (commandRunner == null) throw new ArgumentNullException("commandRunner");
            runner = commandRunner;
            requireElevation = checkElevation;
            using (SHA256 hash = SHA256.Create())
                RulePrefix = "Cute Clash TUN " + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(corePath.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
        }

        public void EnsureRules()
        {
            CheckElevation();
            if (!File.Exists(corePath)) throw new FileNotFoundException("The core executable was not found.", corePath);
            try
            {
                foreach (string protocol in new[] { "TCP", "UDP" })
                {
                    // netsh returns 1 when there is no matching rule. The exact name,
                    // executable, direction and protocol keep this cleanup app-owned.
                    FirewallCommandResult cleanup = runner.Run(DeleteArguments(protocol));
                    if (cleanup.ExitCode == 0) activeProtocols.Remove(protocol);
                    FirewallCommandResult result = runner.Run("advfirewall firewall add rule name=" + Quoted(RulePrefix + " " + protocol) +
                        " dir=in action=allow program=" + Quoted(corePath) + " enable=yes profile=any protocol=" + protocol + " edge=no");
                    if (result.ExitCode != 0)
                        throw new InvalidOperationException(Localization.Format("无法为 TUN 核心设置 Windows 防火墙规则（退出码 {0}）：{1}", "Could not configure Windows Firewall for the TUN core (exit code {0}): {1}", result.ExitCode, result.Output));
                    activeProtocols.Add(protocol);
                }
            }
            catch
            {
                // Roll back successful additions. Failed cleanup remains tracked for
                // the caller's next cleanup attempt; preserve the original setup error.
                try { RemoveRules(); } catch { }
                throw;
            }
        }

        public void RemoveRules()
        {
            if (activeProtocols.Count == 0) return;
            CheckElevation();
            Exception failure = null;
            foreach (string protocol in new List<string>(activeProtocols))
            {
                try
                {
                    FirewallCommandResult result = runner.Run(DeleteArguments(protocol));
                    if (result.ExitCode != 0)
                        throw new InvalidOperationException(Localization.Format(
                            "未能确认 Cute Clash 的 {0} 防火墙规则已清理（退出码 {1}）：{2}",
                            "Removal of Cute Clash's {0} firewall rule could not be confirmed (exit code {1}): {2}", protocol, result.ExitCode, result.Output));
                    activeProtocols.Remove(protocol);
                }
                catch (Exception ex) { failure = ex; }
            }
            if (failure != null) throw failure;
        }

        private string DeleteArguments(string protocol)
        {
            return "advfirewall firewall delete rule name=" + Quoted(RulePrefix + " " + protocol) +
                " program=" + Quoted(corePath) + " dir=in protocol=" + protocol;
        }

        private static string Quoted(string value) { return "\"" + value + "\""; }

        private void CheckElevation()
        {
            if (requireElevation && !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException(Localization.T("设置 TUN 防火墙规则需要管理员权限。", "Administrator privileges are required to configure the TUN firewall rules."));
        }
    }

    internal interface IFirewallCommandRunner
    {
        FirewallCommandResult Run(string arguments);
    }

    internal sealed class FirewallCommandResult
    {
        internal int ExitCode;
        internal string Output;
    }

    internal sealed class NetshFirewallCommandRunner : IFirewallCommandRunner
    {
        public FirewallCommandResult Run(string arguments)
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe");
            var info = new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = new Process { StartInfo = info })
            {
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    try { process.Kill(); process.WaitForExit(2000); } catch (InvalidOperationException) { }
                    throw new TimeoutException(Localization.T("Windows 防火墙规则操作超时。", "The Windows Firewall operation timed out."));
                }
                string text = (output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult()).Trim();
                return new FirewallCommandResult { ExitCode = process.ExitCode, Output = text.Length > 2000 ? text.Substring(0, 2000) : text };
            }
        }
    }
}
