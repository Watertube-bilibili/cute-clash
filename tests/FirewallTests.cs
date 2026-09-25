using System;
using System.Collections.Generic;
using System.IO;

namespace CuteClash.Tests
{
    internal static class FirewallTests
    {
        internal static int Run(string scratch)
        {
            Directory.CreateDirectory(scratch);
            string core = Path.Combine(scratch, "core & 测试.exe");
            File.WriteAllText(core, "fake executable: command runner is mocked");
            var commands = new FakeRunner();
            var firewall = new WindowsFirewall(core, commands, false);
            firewall.EnsureRules();
            Check(commands.Arguments.Count == 4, "Each protocol has exactly one remove and one add command");
            foreach (string command in commands.Arguments)
            {
                Check(command.Contains("program=\"" + core + "\"") && command.Contains("dir=in") && command.Contains(firewall.RulePrefix), "Every command targets only the same named executable and direction");
                Check(!command.Contains("name=all") && !command.Contains("set allprofiles") && !command.Contains("powershell"), "Never disable or broadly remove firewall rules");
            }
            Check(commands.Arguments[1].Contains("protocol=TCP edge=no") && commands.Arguments[3].Contains("protocol=UDP edge=no"), "TUN rules cover TCP and UDP without edge traversal");
            firewall.EnsureRules();
            Check(commands.Arguments[0] == commands.Arguments[4], "Repeated setup uses the same scoped rule names");
            firewall.RemoveRules();
            Check(commands.Arguments[8].Contains("delete rule") && commands.Arguments[9].Contains("delete rule"), "Stop removes both app-owned rules");

            var other = new WindowsFirewall(Path.Combine(scratch, "other.exe"), new FakeRunner(), false);
            Check(firewall.RulePrefix != other.RulePrefix, "Separate installation paths never share rule names");
            var missing = new FakeRunner();
            Reject(delegate { new WindowsFirewall(Path.Combine(scratch, "missing.exe"), missing, false).EnsureRules(); });
            Check(missing.Arguments.Count == 0, "Missing executables cannot modify the firewall");
            foreach (string invalid in new[] { "relative.exe", @"\\server\share\core.exe", core + "\n", core + "\"", core + ".dll" })
                Reject(delegate { new WindowsFirewall(invalid, new FakeRunner(), false); });

            var failed = new FakeRunner { FailUdp = true };
            Reject(delegate { new WindowsFirewall(core, failed, false).EnsureRules(); });
            Check(failed.Arguments.Count == 5 && failed.Arguments[4].Contains("delete rule") && failed.Arguments[4].Contains("protocol=TCP"), "Partial setup failure rolls back the successfully added rule");

            var incomplete = new FakeRunner();
            var retryable = new WindowsFirewall(core, incomplete, false);
            retryable.EnsureRules();
            incomplete.FailDeleteTcp = true;
            Reject(delegate { retryable.RemoveRules(); });
            Check(incomplete.Arguments.Count == 6, "A failed cleanup still attempts every tracked protocol");
            incomplete.FailDeleteTcp = false;
            retryable.RemoveRules();
            Check(incomplete.Arguments.Count == 7 && incomplete.Arguments[6].Contains("protocol=TCP"), "Only the failed cleanup is retried");
            retryable.RemoveRules();
            Check(incomplete.Arguments.Count == 7, "Successful cleanup is idempotent without redundant deletes");
            new WindowsFirewall(core, incomplete, false).RemoveRules();
            Check(incomplete.Arguments.Count == 7, "A fresh helper does not remove rules it never added");

            var rollbackFailure = new FakeRunner { FailUdp = true, FailDeleteTcp = true };
            var rollback = new WindowsFirewall(core, rollbackFailure, false);
            Reject(delegate { rollback.EnsureRules(); });
            rollbackFailure.FailDeleteTcp = false;
            rollback.RemoveRules();
            Check(rollbackFailure.Arguments.Count == 6 && rollbackFailure.Arguments[5].Contains("protocol=TCP"), "Failed rollback retains ownership for the next cleanup attempt");
            return 5;
        }

        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; } catch (FileNotFoundException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Expected the operation to fail");
        }
        private sealed class FakeRunner : IFirewallCommandRunner
        {
            internal readonly List<string> Arguments = new List<string>();
            internal bool FailUdp;
            internal bool FailDeleteTcp;
            public FirewallCommandResult Run(string arguments)
            {
                Arguments.Add(arguments);
                return new FirewallCommandResult {
                    ExitCode = (FailUdp && arguments.Contains("add rule") && arguments.Contains("protocol=UDP")) ||
                        (FailDeleteTcp && arguments.Contains("delete rule") && arguments.Contains("protocol=TCP")) ? 1 : 0,
                    Output = "simulated firewall result"
                };
            }
        }
    }
}
