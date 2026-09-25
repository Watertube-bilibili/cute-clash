using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CuteClash;
using YamlDotNet.RepresentationModel;

public static class ProfileTests
{
    public static int Run(string scratch)
    {
        Directory.CreateDirectory(scratch);
        string data = Path.Combine(scratch, "profile-store");
        ProfileStore store = new ProfileStore(data);
        int passed = 0;
        const string sample = "mixed-port: 54321\nexternal-controller: 0.0.0.0:8888\nsecret: hostile\n" +
            "port: 1234\nsocks-port: 1235\nredir-port: 1236\ntproxy-port: 1237\nallow-lan: true\nbind-address: '*'\n" +
            "authentication: ['subscription-user:subscription-password']\nskip-auth-prefixes: ['10.0.0.0/8']\n" +
            "external-controller-tls: 0.0.0.0:444\nexternal-controller-pipe: secret\nexternal-ui: C:/Windows\nexternal-ui-url: https://example.com/archive.zip\n" +
            "script: {code: unsafe}\ngeox-url: {geoip: 'https://example.com/custom.dat'}\n" +
            "tuic-server: {enable: true, listen: '0.0.0.0:9099'}\nntp: {enable: true, write-to-system: true}\nexternal-doh-server: /dns-query\n" +
            "listeners: [{name: evil, type: mixed, port: 8889}]\ntunnels: ['tcp,0.0.0.0:8890,example.com:80']\n" +
            "tun: {enable: true, device: evil, stack: system, route-address: ['0.0.0.0/0']}\n" +
            "dns: {enable: false, listen: '0.0.0.0:53', nameserver: ['https://example.com/dns-query']}\n" +
            "proxies:\n  - name: future\n    type: a-new-protocol\n    server: example.com\n    port: 443\n    obscure-option: {version: 42, token: 'abc:123'}\n" +
            "proxy-groups: [{name: PROXY, type: select, proxies: [future, DIRECT]}]\nrules: [MATCH,PROXY]\n" +
            "proxy-providers:\n  remote:\n    type: http\n    url: https://example.com/nodes.yaml\n    path: ./cache/nodes.yaml\n    interval: 3600\n" +
            "rule-providers:\n  domains: {type: http, behavior: domain, format: mrs, url: 'https://example.com/rules.mrs', path: ./rules/domains.mrs}\n";
        ProfileInfo imported = store.ImportText("测试配置", "https://example.com/profile", sample);
        Check(File.Exists(store.GetProfilePath(imported.Id)), "Imported profile missing");
        AppSettings settings = new AppSettings();
        settings.Profiles.Add(imported);
        settings.SelectedProfileId = imported.Id;
        store.SaveSettings(settings);
        Check(store.LoadSettings().Profiles[0].Name == "测试配置", "Settings Unicode round trip");
        passed++;

        string runtimePath = Path.Combine(scratch, "runtime.yaml");
        string secret = "test-secret-1234567890-abcdefgh";
        Check(store.BuildRuntimeConfig(settings, secret, runtimePath) == Path.GetFullPath(runtimePath), "Returned runtime path");
        YamlMappingNode runtime = Read(File.ReadAllText(runtimePath));
        Check(Value(runtime, "external-controller") == "127.0.0.1:19090" && Value(runtime, "secret") == secret, "Controller isolation");
        Check(Value(runtime, "mixed-port") == "7890" && Value(runtime, "allow-lan") == "false" && Value(runtime, "bind-address") == "127.0.0.1", "Inbound isolation");
        foreach (string key in new[] { "port", "socks-port", "redir-port", "tproxy-port" }) Check(Value(runtime, key) == "0", "Disabled inbound " + key);
        foreach (string key in new[] { "external-controller-tls", "external-controller-pipe", "external-ui", "external-ui-url", "script", "tunnels", "tuic-server", "ntp", "external-doh-server" }) Check(Get(runtime, key) == null, "Unsafe override survived: " + key);
        Check(((YamlSequenceNode)Get(runtime, "listeners")).Children.Count == 0, "Listeners cleared");
        Check(Value((YamlMappingNode)Get(runtime, "tun"), "enable") == "false", "Imported TUN cannot enable itself");
        Check(Value((YamlMappingNode)Get(runtime, "dns"), "listen") == "", "Unneeded DNS listener disabled");
        passed++;

        Check(((YamlSequenceNode)Get(runtime, "authentication")).Children.Count == 0, "Managed loopback proxy must not require subscription credentials");
        var skipAuth = (YamlSequenceNode)Get(runtime, "skip-auth-prefixes");
        Check(skipAuth.Children.Count == 2 && ((YamlScalarNode)skipAuth.Children[0]).Value == "127.0.0.1/32" && ((YamlScalarNode)skipAuth.Children[1]).Value == "::1/128", "Authentication bypass policy is limited to loopback");
        Check(File.ReadAllText(store.GetProfilePath(imported.Id)).Contains("subscription-user:subscription-password"), "Authentication changes do not alter the source profile");
        passed++;

        Check(Value((YamlMappingNode)Get(runtime, "geox-url"), "geoip") == "https://example.com/custom.dat", "Custom GEO download mirror retained");
        foreach (string invalidGeo in new[] { "[]", "not-a-map", "{geoip: []}", "{geoip: ''}", "{geoip: 'file:///C:/Windows/system.ini'}", "{geoip: 'ftp://example.test/data'}", "{geoip: 'https://name:private-password@example.test/data'}", "{geoip: 'https://example.test/data#fragment'}" })
            Fails(delegate { store.ValidateText("geox-url: " + invalidGeo + "\nproxies: []\n"); }, "Invalid GEO download address accepted");
        var geoUrls = (YamlMappingNode)Get(Read(store.ValidateText("geox-url: {geoip: 'https://cdn.example.test/geoip.dat?signature=a%2Bb', mmdb: 'http://mirror.example.test/Country.mmdb', asn: 'https://cdn.example.test/asn.mmdb', geosite: 'https://cdn.example.test/geosite.dat'}\nproxies: []\n")), "geox-url");
        Check(Value(geoUrls, "geoip").EndsWith("?signature=a%2Bb") && Value(geoUrls, "mmdb").StartsWith("http://"), "Signed HTTPS mirrors and HTTP GEO mirrors retain exact addresses");
        passed++;

        YamlMappingNode node = (YamlMappingNode)((YamlSequenceNode)Get(runtime, "proxies")).Children[0];
        Check(Value(node, "type") == "a-new-protocol", "Future protocol retained");
        Check(Value((YamlMappingNode)Get(node, "obscure-option"), "token") == "abc:123", "Nested unknown fields retained");
        Check(File.ReadAllText(store.GetProfilePath(imported.Id)).Contains("hostile"), "Runtime generation modified source profile");
        passed++;

        string providerPath = Value((YamlMappingNode)Get((YamlMappingNode)Get(runtime, "proxy-providers"), "remote"), "path");
        string expectedPrefix = Path.GetFullPath(Path.Combine(data, "providers", imported.Id)).Replace('\\', '/') + "/";
        Check(providerPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase), "Provider cache escaped managed directory");
        string mrsPath = Value((YamlMappingNode)Get((YamlMappingNode)Get(runtime, "rule-providers"), "domains"), "path");
        Check(mrsPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) && mrsPath.EndsWith(".mrs"), "MRS managed cache format");
        settings.TunEnabled = true;
        store.BuildRuntimeConfig(settings, secret, runtimePath);
        runtime = Read(File.ReadAllText(runtimePath));
        YamlMappingNode tun = (YamlMappingNode)Get(runtime, "tun");
        Check(Value(tun, "enable") == "true" && Value(tun, "stack") == "gvisor" && Value(tun, "device") == "cute-clash", "Managed TUN settings");
        Check(Get(tun, "route-address") == null && Value(tun, "auto-route") == "true", "Imported routes discarded");
        Check(Value(tun, "strict-route") == "false", "TUN must not force Windows strict-route firewall filters");
        var hijack = (YamlSequenceNode)Get(tun, "dns-hijack");
        Check(hijack.Children.Count == 2 && ((YamlScalarNode)hijack.Children[0]).Value == "any:53" && ((YamlScalarNode)hijack.Children[1]).Value == "tcp://any:53", "Both UDP and TCP DNS hijacking configured");
        YamlMappingNode dns = (YamlMappingNode)Get(runtime, "dns");
        Check(Value(dns, "enable") == "true" && Value(dns, "enhanced-mode") == "fake-ip", "TUN DNS enabled");
        Check(Value(dns, "listen") == "", "TUN uses internal DNS without occupying port 1053");
        Check(((YamlScalarNode)((YamlSequenceNode)Get(dns, "nameserver")).Children[0]).Value == "https://example.com/dns-query", "Custom DNS retained");
        Check(Value((YamlMappingNode)Get((YamlMappingNode)Get(runtime, "proxy-providers"), "remote"), "path") == providerPath, "Stable provider name");
        Check(File.Exists(runtimePath + ".bak"), "Runtime atomic backup missing");
        passed++;

        settings.Mode = "global";
        store.SaveSettings(settings);
        Check(store.LoadSettings().Mode == "global", "Settings update missing");
        Check(File.ReadAllText(Path.Combine(data, "settings.json.bak")).Contains("\"Mode\":\"rule\""), "Settings backup is not previous successful version");
        settings.MixedPort = settings.ControllerPort;
        Fails(delegate { store.SaveSettings(settings); }, "Invalid ports accepted");
        Check(store.LoadSettings().MixedPort == 7890, "Failed save damaged settings");
        settings.MixedPort = 7890;
        passed++;

        string previous = File.ReadAllText(store.GetProfilePath(imported.Id));
        Fails(delegate { store.ReplaceText(imported.Id, "x: [broken"); }, "Malformed YAML accepted");
        Check(File.ReadAllText(store.GetProfilePath(imported.Id)) == previous, "Failed import damaged original");
        store.ReplaceText(imported.Id, "proxies: []\nrules: ['MATCH,DIRECT']\n");
        Check(File.ReadAllText(store.GetProfilePath(imported.Id) + ".bak") == previous, "Profile backup is not previous version");
        passed++;

        foreach (string invalid in new[] { "../settings", "../../outside", "C:\\Windows\\system.ini", "", "abc", new string('g', 32) })
            Fails(delegate { store.GetProfilePath(invalid); }, "Invalid profile ID accepted: " + invalid);
        foreach (string invalid in new[] { "../outside.yaml", "cache/../../outside.yaml", "C:/Windows/system.ini", "/etc/passwd", "\\\\host\\file", "cache/.. /outside", "cache:stream", "cache//x" })
        {
            string yaml = "proxy-providers:\n  bad: {type: http, url: 'https://example.com/file', path: '" + invalid.Replace("'", "''") + "'}\n";
            Fails(delegate { store.ValidateText(yaml); }, "Unsafe provider path accepted: " + invalid);
        }
        Fails(delegate { store.ValidateText("proxy-providers: {bad: {type: http, url: 'file:///C:/Windows/system.ini'}}"); }, "File provider URL accepted");
        passed++;

        foreach (string invalid in new[] { "external-controller: safe\nexternal-controller: hostile\n", "a: {b: 1, b: 2}", "---\na: 1\n---\na: 2", "[a, b]", "? [a, b]\n: value", "a: &loop {b: *loop}", "&loop {*loop: value}", "? {a: b}\n: value", "a: &key text\n*key: value" })
            Fails(delegate { store.ValidateText(invalid); }, "Invalid YAML structure accepted");
        Fails(delegate { store.ValidateText("a: " + new string('[', 70) + "0" + new string(']', 70)); }, "Deep YAML accepted");
        Fails(delegate { store.ValidateText("a: " + new string('x', ProfileStore.MaximumProfileBytes)); }, "Oversized YAML accepted");
        StringBuilder bomb = new StringBuilder("a0: &a0 [x, x, x, x, x, x, x, x, x, x]\n");
        for (int i = 1; i < 6; i++) bomb.Append("a").Append(i).Append(": &a").Append(i).Append(" [").Append(String.Join(", ", new string[] { "*a" + (i - 1), "*a" + (i - 1), "*a" + (i - 1), "*a" + (i - 1), "*a" + (i - 1), "*a" + (i - 1), "*a" + (i - 1), "*a" + (i - 1), "*a" + (i - 1), "*a" + (i - 1) })).Append("]\n");
        Fails(delegate { store.ValidateText(bomb.ToString()); }, "Alias expansion bomb accepted");
        passed++;

        string merged = store.ValidateText("defaults: &d {type: vless, server: example.com, port: 443}\nproxies: [{<<: *d, name: test, port: 8443}]\n");
        YamlMappingNode mergedProxy = (YamlMappingNode)((YamlSequenceNode)Get(Read(merged), "proxies")).Children[0];
        Check(Value(mergedProxy, "type") == "vless" && Value(mergedProxy, "port") == "8443", "YAML alias/merge semantics failed");
        Check(Get(mergedProxy, "<<") == null, "Merge key not expanded");
        passed++;

        Check(Directory.GetFiles(data, "*.tmp", SearchOption.AllDirectories).Length == 0, "Atomic temporary files leaked");
        store.Delete(imported.Id);
        Check(!File.Exists(store.GetProfilePath(imported.Id)) && !File.Exists(store.GetProfilePath(imported.Id) + ".bak"), "Profile delete failed");
        return passed + 1;
    }

    // Exercises the real packaged core with fixtures only. No system proxy, TUN,
    // routing or public DNS changes are made by this test.
    public static async Task<int> RunCoreCompatibilityAsync(string corePath, string scratch)
    {
        Directory.CreateDirectory(scratch);
        var dnsServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int dnsPort = ((IPEndPoint)dnsServer.Client.LocalEndPoint).Port;
        Task dnsFixture = Task.Run(delegate
        {
            try
            {
                for (;;)
                {
                    IPEndPoint remote = null; byte[] request = dnsServer.Receive(ref remote);
                    if (request.Length < 17) continue;
                    int questionEnd = 12;
                    while (questionEnd < request.Length && request[questionEnd] != 0) questionEnd += 1 + request[questionEnd];
                    questionEnd += 5;
                    if (questionEnd > request.Length) continue;
                    byte[] response = new byte[questionEnd + 16]; Array.Copy(request, response, questionEnd);
                    response[2] = 0x81; response[3] = 0x80; response[6] = 0; response[7] = 1;
                    response[8] = response[9] = response[10] = response[11] = 0;
                    byte[] answer = { 0xc0, 0x0c, 0, 1, 0, 1, 0, 0, 0, 30, 0, 4, 127, 0, 0, 1 };
                    Array.Copy(answer, 0, response, questionEnd, answer.Length);
                    dnsServer.Send(response, response.Length, remote);
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        });
        Process process = null; Task<string> stdout = null, stderr = null;
        try
        {
            var store = new ProfileStore(Path.Combine(scratch, "data"));
            string source = "authentication: ['airport:password']\nskip-auth-prefixes: []\nproxies: []\nrules: ['MATCH,DIRECT']\n" +
                "geox-url: {geoip: 'http://127.0.0.1:" + dnsPort + "/mirror.dat'}\n" +
                "dns:\n  enable: true\n  listen: '127.0.0.1:1053'\n  ipv6: false\n  use-hosts: false\n  use-system-hosts: false\n" +
                "  enhanced-mode: redir-host\n  default-nameserver: ['127.0.0.1:" + dnsPort + "']\n  nameserver: ['udp://127.0.0.1:" + dnsPort + "']\n";
            ProfileInfo profile = store.ImportText("Local compatibility fixture", null, source);
            var settings = new AppSettings { MixedPort = FreePort(), ControllerPort = FreePort(), SelectedProfileId = profile.Id };
            while (settings.MixedPort == settings.ControllerPort) settings.ControllerPort = FreePort();
            settings.Profiles.Add(profile);
            string runtime = store.BuildRuntimeConfig(settings, "local-profile-test-secret", Path.Combine(scratch, "runtime.yaml"));
            var info = new ProcessStartInfo(corePath, "-d \"" + scratch + "\" -f \"" + runtime + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = scratch };
            foreach (string key in new System.Collections.Generic.List<string>(System.Linq.Enumerable.Cast<string>(info.EnvironmentVariables.Keys)))
                if (key.StartsWith("CLASH_OVERRIDE_", StringComparison.OrdinalIgnoreCase)) info.EnvironmentVariables.Remove(key);
            process = Process.Start(info); stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }))
            {
                client.Timeout = TimeSpan.FromSeconds(2);
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "local-profile-test-secret");
                string api = "http://127.0.0.1:" + settings.ControllerPort;
                bool ready = false;
                for (int i = 0; i < 60 && !ready && !process.HasExited; i++)
                {
                    try { using (var response = await client.GetAsync(api + "/version")) ready = response.IsSuccessStatusCode; }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                    if (!ready) await Task.Delay(100);
                }
                Check(ready, "Real Mihomo core did not start for the local compatibility fixture");
                string result = await client.GetStringAsync(api + "/dns/query?name=cute-clash-local-test.invalid&type=A");
                Check(result.Contains("127.0.0.1"), "Internal DNS resolver must work when dns.listen is empty");
                string configuration = await client.GetStringAsync(api + "/configs");
                Check(configuration.Contains("http://127.0.0.1:" + dnsPort + "/mirror.dat"), "Real core must retain a configured GEO mirror");
            }
            return 1;
        }
        finally
        {
            dnsServer.Close();
            if (process != null)
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                if (stdout != null && stderr != null) File.WriteAllText(Path.Combine(scratch, "core-compatibility.log"), stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
                process.Dispose();
            }
            dnsFixture.GetAwaiter().GetResult();
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private static YamlMappingNode Read(string text) { YamlStream yaml = new YamlStream(); yaml.Load(new StringReader(text)); return (YamlMappingNode)yaml.Documents[0].RootNode; }
    private static YamlNode Get(YamlMappingNode map, string key) { YamlNode value; return map.Children.TryGetValue(new YamlScalarNode(key), out value) ? value : null; }
    private static string Value(YamlMappingNode map, string key) { YamlScalarNode value = Get(map, key) as YamlScalarNode; return value == null ? null : value.Value; }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Profile test failed: " + message); }
    private static void Fails(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (ArgumentException) { return; }
        throw new Exception("Profile test failed: " + message);
    }
}
