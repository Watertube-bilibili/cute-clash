using System;
using System.IO;
using System.Text;
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
        foreach (string key in new[] { "external-controller-tls", "external-controller-pipe", "external-ui", "external-ui-url", "script", "geox-url", "tunnels", "tuic-server", "ntp", "external-doh-server" }) Check(Get(runtime, key) == null, "Unsafe override survived: " + key);
        Check(((YamlSequenceNode)Get(runtime, "listeners")).Children.Count == 0, "Listeners cleared");
        Check(Value((YamlMappingNode)Get(runtime, "tun"), "enable") == "false", "Imported TUN cannot enable itself");
        Check(Value((YamlMappingNode)Get(runtime, "dns"), "listen") == "127.0.0.1:1053", "DNS localhost binding");
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
        YamlMappingNode dns = (YamlMappingNode)Get(runtime, "dns");
        Check(Value(dns, "enable") == "true" && Value(dns, "enhanced-mode") == "fake-ip", "TUN DNS enabled");
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
