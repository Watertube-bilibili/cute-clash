using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace CuteClash.Tests
{
    internal static class SystemProxyConnectionTests
    {
        internal static int Run(string scratch)
        {
            Directory.CreateDirectory(scratch);
            TestSeparateBackups(Path.Combine(scratch, "separate"));
            TestExternalChange(Path.Combine(scratch, "external"));
            TestPartialApply(Path.Combine(scratch, "partial-apply"));
            TestPartialRestore(Path.Combine(scratch, "partial-restore"));
            TestReadback(Path.Combine(scratch, "readback"));
            return 5;
        }

        private static void TestSeparateBackups(string directory)
        {
            var backend = new FakeConnections();
            ProxySnapshot lan = backend.Read(null), ras = backend.Read("宽带连接");
            var proxy = new SystemProxy(directory, backend);
            bool checkedBeforeWrite = false;
            backend.BeforeWrite = delegate
            {
                ProxyJournal journal = ReadJournal(directory);
                Check(journal.Connections.Count == 3, "Every LAN and RAS connection is journaled before a write");
                Check(journal.Connections[0].Connection == null && journal.Connections[0].Original.SameAs(lan), "LAN backup is separate");
                Check(journal.Connections[1].Connection == "宽带连接" && journal.Connections[1].Original.SameAs(ras), "Unicode dial-up connection has its own PAC and flags backup");
                checkedBeforeWrite = true;
            };
            proxy.Enable(18451);
            backend.BeforeWrite = null;
            Check(checkedBeforeWrite && backend.Read(null).Server == "127.0.0.1:18451" && backend.Read("宽带连接").Server == "127.0.0.1:18451", "LAN and Unicode RAS both apply the requested proxy");
            Check(proxy.Restore() && backend.Read(null).SameAs(lan) && backend.Read("宽带连接").SameAs(ras) && !proxy.HasJournal, "Each connection restores its own original settings");
        }

        private static void TestExternalChange(string directory)
        {
            var backend = new FakeConnections();
            ProxySnapshot ras = backend.Read("宽带连接");
            var proxy = new SystemProxy(directory, backend);
            proxy.Enable(18452);
            var external = new ProxySnapshot { Flags = 3, Server = "another-proxy:8888", Bypass = "custom", Pac = "" };
            backend.Values[""] = external;
            Check(proxy.Restore(), "Unchanged RAS entries still restore after another app changes LAN");
            Check(backend.Read(null).SameAs(external) && backend.Read("宽带连接").SameAs(ras) && !proxy.HasJournal, "Externally changed LAN settings are preserved while RAS restores");
        }

        private static void TestPartialApply(string directory)
        {
            var backend = new FakeConnections { FailConnection = "宽带连接" };
            ProxySnapshot lan = backend.Read(null), ras = backend.Read("宽带连接"), vpn = backend.Read("工作 VPN");
            var proxy = new SystemProxy(directory, backend);
            ExpectIOException(delegate { proxy.Enable(18453); });
            Check(proxy.HasJournal && backend.Read(null).Server == "127.0.0.1:18453", "A later write failure retains earlier changes and complete recovery data");
            Check(ReadJournal(directory).Connections.Count == 3, "Partial apply retains every original connection backup");
            backend.FailConnection = null;
            Check(proxy.Restore() && backend.Read(null).SameAs(lan) && backend.Read("宽带连接").SameAs(ras) && backend.Read("工作 VPN").SameAs(vpn), "Partial apply recovers changed LAN without rewriting untouched RAS");
            Check(!proxy.HasJournal, "Partial apply recovery completes cleanly");
        }

        private static void TestPartialRestore(string directory)
        {
            var backend = new FakeConnections();
            ProxySnapshot lan = backend.Read(null), ras = backend.Read("宽带连接"), vpn = backend.Read("工作 VPN");
            var proxy = new SystemProxy(directory, backend);
            proxy.Enable(18454);
            backend.FailConnection = "宽带连接";
            ExpectIOException(delegate { proxy.Restore(); });
            Check(backend.Read(null).SameAs(lan) && backend.Read("工作 VPN").SameAs(vpn), "One failed restore does not prevent other connections from restoring");
            ProxyJournal pending = ReadJournal(directory);
            Check(pending.Connections.Count == 1 && pending.Connections[0].Connection == "宽带连接" && pending.Connections[0].Original.SameAs(ras), "Only the failed connection remains in the recovery journal");
            backend.FailConnection = null;
            Check(new SystemProxy(directory, backend).Restore() && backend.Read("宽带连接").SameAs(ras) && !proxy.HasJournal, "A new application instance can retry the retained recovery");
        }

        private static void TestReadback(string directory)
        {
            var backend = new FakeConnections { IgnoreConnection = "宽带连接" };
            ProxySnapshot lan = backend.Read(null), ras = backend.Read("宽带连接");
            var proxy = new SystemProxy(directory, backend);
            ExpectIOException(delegate { proxy.Enable(18455); });
            Check(proxy.HasJournal && backend.Read("宽带连接").SameAs(ras), "Silent write failure is rejected by readback rather than reported as enabled");
            backend.IgnoreConnection = null;
            proxy.Restore();
            Check(backend.Read(null).SameAs(lan) && !proxy.HasJournal, "Readback failure still permits recovery of earlier applied settings");
            proxy.Enable(18455);
            backend.IgnoreConnection = "宽带连接";
            ExpectIOException(delegate { proxy.Restore(); });
            Check(ReadJournal(directory).Connections.Count == 1, "Silent restore failure also keeps the exact pending backup");
            backend.IgnoreConnection = null;
            proxy.Restore();
            Check(!proxy.HasJournal && backend.Read("宽带连接").SameAs(ras), "Silent restore failures are retryable");
        }

        private static ProxyJournal ReadJournal(string directory)
        {
            return new JavaScriptSerializer().Deserialize<ProxyJournal>(File.ReadAllText(Path.Combine(directory, "proxy-recovery.json")));
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void ExpectIOException(Action action)
        {
            bool rejected = false;
            try { action(); } catch (IOException) { rejected = true; }
            Check(rejected, "Expected a proxy write or readback failure");
        }

        private sealed class FakeConnections : IConnectionProxyBackend
        {
            internal readonly Dictionary<string, ProxySnapshot> Values = new Dictionary<string, ProxySnapshot> {
                { "", new ProxySnapshot { Flags = 9, Server = "lan-old:7000", Bypass = "<local>", Pac = "" } },
                { "宽带连接", new ProxySnapshot { Flags = 5, Server = "dialup-old:8000", Bypass = "*.internal", Pac = "https://pac.example.test/宽带.pac" } },
                { "工作 VPN", new ProxySnapshot { Flags = 3, Server = "vpn-old:9000", Bypass = "10.*", Pac = "" } }
            };
            internal string FailConnection;
            internal string IgnoreConnection;
            internal Action BeforeWrite;
            public IList<string> GetConnections() { return new List<string> { null, "宽带连接", "工作 VPN" }; }
            public ProxySnapshot Read() { return Read(null); }
            public void Write(ProxySnapshot snapshot) { Write(null, snapshot); }
            public ProxySnapshot Read(string connection)
            {
                ProxySnapshot value = Values[connection ?? ""];
                return new ProxySnapshot { Flags = value.Flags, Server = value.Server, Bypass = value.Bypass, Pac = value.Pac };
            }
            public void Write(string connection, ProxySnapshot snapshot)
            {
                if (BeforeWrite != null) BeforeWrite();
                if (FailConnection != null && FailConnection == (connection ?? "")) throw new IOException("Simulated RAS write failure");
                if (IgnoreConnection != null && IgnoreConnection == (connection ?? "")) return;
                Values[connection ?? ""] = new ProxySnapshot { Flags = snapshot.Flags, Server = snapshot.Server, Bypass = snapshot.Bypass, Pac = snapshot.Pac };
            }
        }
    }
}
