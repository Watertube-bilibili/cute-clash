using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace CuteClash.Tests
{
    internal static class RuntimeCompatTests
    {
        public static int Run(string scratch)
        {
            Directory.CreateDirectory(scratch);
            var serializer = new JavaScriptSerializer();
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string profileId = "0123456789abcdef0123456789abcdef";
            string legacy = "{\"Language\":\"en\",\"MixedPort\":7890,\"ControllerPort\":19090,\"Mode\":\"rule\",\"Profiles\":[{\"Id\":\"" + profileId + "\",\"Name\":\"旧配置 / Existing profile\",\"UpdatedAt\":\"\\/Date(-1)\\/\"}],\"UnknownFutureOption\":{\"enabled\":true}}";
            File.WriteAllText(Path.Combine(scratch, "settings.json"), legacy);
            var store = new ProfileStore(scratch);
            AppSettings settings = store.LoadSettings();
            Check(settings.Language == "en" && settings.Profiles[0].UpdatedAt == epoch.AddMilliseconds(-1) && settings.Profiles[0].UpdatedAt.Kind == DateTimeKind.Utc, "Legacy settings date retains the UTC instant before 1970");
            store.SaveSettings(settings);
            string saved = File.ReadAllText(Path.Combine(scratch, "settings.json"));
            Check(saved.Contains("\\/Date(-1)\\/") && store.LoadSettings().Profiles[0].UpdatedAt == epoch.AddMilliseconds(-1), "Saved dates remain readable by the Framework edition");

            ProfileInfo offset = serializer.Deserialize<ProfileInfo>("{\"UpdatedAt\":\"\\/Date(0+0800)\\/\"}");
            ProfileInfo negativeOffset = serializer.Deserialize<ProfileInfo>("{\"UpdatedAt\":\"\\/Date(-1-0530)\\/\"}");
            Check(offset.UpdatedAt == epoch && offset.UpdatedAt.Kind == DateTimeKind.Utc && negativeOffset.UpdatedAt == epoch.AddMilliseconds(-1), "Microsoft date offset does not shift UTC milliseconds");

            var api = serializer.Deserialize<Dictionary<string, object>>("{\"proxies\":{\"策略组\":{\"all\":[\"DIRECT\",\"REJECT\"],\"now\":\"DIRECT\"}},\"connections\":[{\"id\":\"test\",\"download\":2147483648}],\"uploadTotal\":2147483648,\"small\":7,\"ratio\":0.25,\"largeFloat\":1e50,\"enabled\":true,\"missing\":null}");
            var proxies = (Dictionary<string, object>)api["proxies"];
            var group = (Dictionary<string, object>)proxies["策略组"];
            var nodes = (IList)group["all"];
            Check(nodes.Count == 2 && (string)nodes[0] == "DIRECT" && ((ICollection)api["connections"]).Count == 1, "API dictionaries and arrays preserve existing collection contracts");
            Check(api["small"] is int && api["uploadTotal"] is long && api["ratio"] is decimal && api["largeFloat"] is double && (long)api["uploadTotal"] == 2147483648L && (decimal)api["ratio"] == 0.25m && (bool)api["enabled"] && api["missing"] == null, "API numeric, boolean and null values retain useful CLR types");

            var bounded = new JavaScriptSerializer { MaxJsonLength = 8, RecursionLimit = 2 };
            Reject(delegate { bounded.Deserialize<object>("\"123456789\""); }, "Oversized input is rejected");
            Reject(delegate { bounded.Serialize(new { value = "123456789" }); }, "Oversized output is rejected");
            bounded.MaxJsonLength = 1024;
            Reject(delegate { bounded.Deserialize<object>("[[[1]]]"); }, "Excessive input nesting is rejected");
            Reject(delegate { bounded.Serialize(new object[] { new object[] { new object[] { 1 } } }); }, "Excessive output nesting is rejected");
            Reject(delegate { serializer.Deserialize<AppSettings>("{\"Profiles\":["); }, "Truncated JSON is rejected");
            Reject(delegate { serializer.Deserialize<ProfileInfo>("{\"UpdatedAt\":\"\\/Date(9223372036854775807)\\/\"}"); }, "Out-of-range dates are rejected");
#if NET6_0
            Reject(delegate { serializer.Deserialize<ProfileInfo>("{\"UpdatedAt\":\"/Date(0+1460)/\"}"); }, "Invalid date offsets are rejected");
            Reject(delegate { serializer.Deserialize<Dictionary<string, object>>("{\"n\":1e9999}"); }, "Non-finite API numbers are rejected");
#endif

            TestJournal(Path.Combine(scratch, "journal"));
            string entry = Assembly.GetEntryAssembly().Location;
            string executable = AppController.WatchdogExecutablePath(entry);
            Check(executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(executable), "Watchdog resolves the actual apphost executable");
#if NET6_0
            Check(AppController.WatchdogExecutablePath(@"C:\Apps\cute clash\cute-clash.dll") == @"C:\Apps\cute clash\cute-clash.exe", "Managed DLL path maps to adjacent apphost, including spaces");
#else
            Check(executable == entry, "Framework watchdog retains its original executable path");
#endif
            return 6;
        }

        private static void TestJournal(string directory)
        {
            Directory.CreateDirectory(directory);
            var original = new ProxySnapshot { Flags = 13, Server = "old-proxy:8080", Bypass = "*.example", Pac = "https://example.test/proxy.pac" };
            var applied = new ProxySnapshot { Flags = 3, Server = "127.0.0.1:34567", Bypass = "<local>;localhost;127.*;[::1]", Pac = "" };
            var backend = new FakeProxy { Current = applied };
            // Exact structure written by the previous .NET Framework edition.
            File.WriteAllText(Path.Combine(directory, "proxy-recovery.json"), "{\"Original\":{\"Flags\":13,\"Server\":\"old-proxy:8080\",\"Bypass\":\"*.example\",\"Pac\":\"https://example.test/proxy.pac\"},\"Applied\":{\"Flags\":3,\"Server\":\"127.0.0.1:34567\",\"Bypass\":\"<local>;localhost;127.*;[::1]\",\"Pac\":\"\"}}");
            var proxy = new SystemProxy(directory, backend);
            Check(proxy.Restore() && backend.Current.SameAs(original) && !proxy.HasJournal, "Legacy recovery journal restores PAC and flags without native proxy writes");
            proxy.Enable(34567);
            var journal = new JavaScriptSerializer().Deserialize<ProxyJournal>(File.ReadAllText(Path.Combine(directory, "proxy-recovery.json")));
            Check(journal.Connections.Count == 1 && journal.Connections[0].Original.SameAs(original) && journal.Connections[0].Applied.SameAs(applied), "New recovery journal preserves both snapshots");
            Check(proxy.Restore() && backend.Current.SameAs(original), "New recovery journal can restore the prior state");
        }
        private sealed class FakeProxy : IProxyBackend
        {
            public ProxySnapshot Current;
            public ProxySnapshot Read() { return Current; }
            public void Write(ProxySnapshot snapshot) { Current = snapshot; }
        }
        private static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception) { rejected = true; }
            Check(rejected, name);
        }
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL " + name);
        }
    }
}
