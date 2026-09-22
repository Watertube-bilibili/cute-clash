using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace CuteClash
{
    /// <summary>Persists source profiles separately from the app-controlled runtime configuration.</summary>
    public sealed class ProfileStore
    {
        public const int MaximumProfileBytes = 8 * 1024 * 1024;
        private const int MaximumNodes = 100000;
        private readonly string dataDirectory;
        private readonly string profilesDirectory;
        private readonly string settingsPath;
        private readonly object sync = new object();

        public ProfileStore(string dataDirectory)
        {
            if (String.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException(Localization.T("数据目录不能为空。", "The data directory cannot be empty."), "dataDirectory");
            this.dataDirectory = Path.GetFullPath(dataDirectory);
            profilesDirectory = Path.Combine(this.dataDirectory, "profiles");
            settingsPath = Path.Combine(this.dataDirectory, "settings.json");
            Directory.CreateDirectory(profilesDirectory);
        }

        public AppSettings LoadSettings()
        {
            lock (sync)
            {
                if (!File.Exists(settingsPath)) return new AppSettings();
                if (new FileInfo(settingsPath).Length > 1024 * 1024) throw new InvalidDataException(Localization.T("设置文件过大。请检查 settings.json。 ", "The settings file is too large. Check settings.json."));
                AppSettings settings;
                try { settings = Serializer().Deserialize<AppSettings>(File.ReadAllText(settingsPath, Encoding.UTF8)); }
                catch (Exception ex) { throw new InvalidDataException(Localization.T("设置文件无法读取。上次成功保存的副本位于 settings.json.bak。", "The settings file could not be read. The last successfully saved copy is in settings.json.bak."), ex); }
                ValidateSettings(settings);
                return settings;
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            lock (sync)
            {
                ValidateSettings(settings);
                string text = Serializer().Serialize(settings);
                if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024) throw new InvalidDataException(Localization.T("设置文件过大。 ", "The settings file is too large."));
                AtomicWrite(settingsPath, text);
            }
        }

        public string GetProfilePath(string id)
        {
            ValidateId(id);
            return Path.Combine(profilesDirectory, id + ".yaml");
        }

        public ProfileInfo ImportText(string name, string sourceUrl, string text)
        {
            string normalized = ValidateText(text);
            if (String.IsNullOrWhiteSpace(name)) name = Localization.T("未命名配置", "Untitled profile");
            if (name.Length > 200) throw new ArgumentException(Localization.T("配置名称最多 200 个字符。", "Profile names can contain at most 200 characters."), "name");
            ValidateSourceUrl(sourceUrl);
            ProfileInfo info = new ProfileInfo { Id = Guid.NewGuid().ToString("N"), Name = name.Trim(), SourceUrl = sourceUrl, UpdatedAt = DateTime.UtcNow };
            lock (sync) AtomicWrite(GetProfilePath(info.Id), normalized);
            return info;
        }

        public void ReplaceText(string id, string text)
        {
            string path = GetProfilePath(id);
            string normalized = ValidateText(text);
            lock (sync)
            {
                if (!File.Exists(path)) throw new FileNotFoundException(Localization.T("配置文件不存在。", "The profile file does not exist."), path);
                AtomicWrite(path, normalized);
            }
        }

        public void Delete(string id)
        {
            string path = GetProfilePath(id);
            lock (sync)
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }

        public string ValidateText(string text)
        {
            YamlMappingNode root = Parse(text);
            CheckProviders(root, "proxy-providers", null);
            CheckProviders(root, "rule-providers", null);
            return Serialize(root);
        }

        public string BuildRuntimeConfig(AppSettings settings, string controllerSecret, string destinationPath)
        {
            ValidateSettings(settings);
            if (String.IsNullOrWhiteSpace(controllerSecret) || controllerSecret.Length < 16)
                throw new ArgumentException(Localization.T("控制器密钥必须至少包含 16 个字符。", "The controller secret must contain at least 16 characters."), "controllerSecret");
            string source = GetProfilePath(settings.SelectedProfileId);
            if (!File.Exists(source)) throw new FileNotFoundException(Localization.T("请先导入并选择一个配置。", "Import and select a profile first."), source);
            if (new FileInfo(source).Length > MaximumProfileBytes) throw new InvalidDataException(Localization.T("配置不能超过 8 MiB。 ", "The profile cannot exceed 8 MiB."));
            YamlMappingNode root = Parse(File.ReadAllText(source, Encoding.UTF8));
            string providerDirectory = Path.Combine(dataDirectory, "providers", settings.SelectedProfileId);
            CheckProviders(root, "proxy-providers", providerDirectory);
            CheckProviders(root, "rule-providers", providerDirectory);

            string[] removed = { "external-controller-tls", "external-controller-unix", "external-controller-pipe",
                "external-ui", "external-ui-name", "external-ui-url", "external-controller-cors", "script", "home-dir",
                "log-file", "geox-url", "tunnels", "ss-config", "vmess-config", "tuic-server", "ebpf", "interface-name", "routing-mark",
                "ntp", "iptables", "external-doh-server", "external-controller-routing-mark" };
            foreach (string key in removed) Remove(root, key);
            Put(root, "mixed-port", Number(settings.MixedPort));
            foreach (string key in new[] { "port", "socks-port", "redir-port", "tproxy-port" }) Put(root, key, Number(0));
            Put(root, "listeners", new YamlSequenceNode());
            Put(root, "allow-lan", Scalar("false"));
            Put(root, "bind-address", Scalar("127.0.0.1"));
            Put(root, "external-controller", Scalar("127.0.0.1:" + settings.ControllerPort));
            Put(root, "secret", Scalar(controllerSecret));
            Put(root, "mode", Scalar(settings.Mode));
            // Never inherit device, routes, listeners or firewall policy from a subscription.
            YamlMappingNode tun = new YamlMappingNode();
            Put(tun, "enable", Scalar(settings.TunEnabled ? "true" : "false"));
            Put(tun, "stack", Scalar("gvisor"));
            Put(tun, "device", Scalar("cute-clash"));
            Put(tun, "auto-route", Scalar("true"));
            Put(tun, "auto-detect-interface", Scalar("true"));
            Put(tun, "strict-route", Scalar("true"));
            Put(tun, "dns-hijack", new YamlSequenceNode(Scalar("any:53")));
            Put(root, "tun", tun);

            YamlNode dnsNode = Get(root, "dns");
            if (dnsNode != null && !(dnsNode is YamlMappingNode)) throw new InvalidDataException(Localization.T("dns 必须是 YAML 映射。 ", "dns must be a YAML mapping."));
            YamlMappingNode dns = dnsNode as YamlMappingNode ?? new YamlMappingNode();
            Put(dns, "listen", Scalar("127.0.0.1:1053"));
            if (settings.TunEnabled)
            {
                Put(dns, "enable", Scalar("true"));
                Default(dns, "enhanced-mode", Scalar("fake-ip"));
                Default(dns, "fake-ip-range", Scalar("198.18.0.1/16"));
                Default(dns, "default-nameserver", new YamlSequenceNode(Scalar("223.5.5.5"), Scalar("1.1.1.1")));
                Default(dns, "nameserver", new YamlSequenceNode(Scalar("https://dns.alidns.com/dns-query"), Scalar("https://cloudflare-dns.com/dns-query")));
            }
            Put(root, "dns", dns);
            string destination = Path.GetFullPath(destinationPath);
            if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(Localization.T("运行配置不能覆盖源配置。", "The runtime configuration cannot overwrite the source profile."), "destinationPath");
            string result = Serialize(root);
            lock (sync) AtomicWrite(destination, result);
            return destination;
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 };
        }

        private static void ValidateSettings(AppSettings settings)
        {
            if (settings == null) throw new InvalidDataException(Localization.T("设置文件为空。 ", "The settings file is empty."));
            if (String.IsNullOrEmpty(settings.Language)) settings.Language = "zh-CN";
            if (settings.Language != "zh-CN" && settings.Language != "en")
                throw new InvalidDataException(Localization.T("界面语言必须是 zh-CN 或 en。", "The interface language must be zh-CN or en."));
            if (settings.MixedPort < 1 || settings.MixedPort > 65535 || settings.ControllerPort < 1 || settings.ControllerPort > 65535 || settings.MixedPort == settings.ControllerPort)
                throw new InvalidDataException(Localization.T("代理端口和控制器端口必须在 1–65535 之间，且不能相同。 ", "The proxy and controller ports must be different and between 1 and 65535."));
            if (settings.Mode != "rule" && settings.Mode != "global" && settings.Mode != "direct") throw new InvalidDataException(Localization.T("代理模式必须是 rule、global 或 direct。 ", "The proxy mode must be rule, global, or direct."));
            if (settings.Profiles == null) settings.Profiles = new List<ProfileInfo>();
            if (settings.Profiles.Count > 1000) throw new InvalidDataException(Localization.T("最多可保存 1000 个配置。 ", "You can save at most 1000 profiles."));
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProfileInfo profile in settings.Profiles)
            {
                if (profile == null) throw new InvalidDataException(Localization.T("配置列表包含空项。 ", "The profile list contains an empty entry."));
                ValidateId(profile.Id);
                if (!ids.Add(profile.Id)) throw new InvalidDataException(Localization.T("配置 ID 重复。 ", "Duplicate profile ID."));
                if (String.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 200) throw new InvalidDataException(Localization.T("配置名称不能为空或超过 200 个字符。 ", "Profile names cannot be empty or exceed 200 characters."));
                ValidateSourceUrl(profile.SourceUrl);
            }
            if (!String.IsNullOrEmpty(settings.SelectedProfileId))
            {
                ValidateId(settings.SelectedProfileId);
                if (!ids.Contains(settings.SelectedProfileId)) throw new InvalidDataException(Localization.T("所选配置不在配置列表中。 ", "The selected profile is not in the profile list."));
            }
        }

        private static void ValidateSourceUrl(string url)
        {
            if (String.IsNullOrEmpty(url)) return;
            Uri uri;
            if (url.Length > 8192 || !Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
                throw new InvalidDataException(Localization.T("订阅地址必须是 HTTP 或 HTTPS URL。 ", "The subscription address must be an HTTP or HTTPS URL."));
        }

        private static void ValidateId(string id)
        {
            Guid parsed;
            if (id == null || id.Length != 32 || !Guid.TryParseExact(id, "N", out parsed)) throw new ArgumentException(Localization.T("配置 ID 无效。", "Invalid profile ID."), "id");
        }

        private static YamlMappingNode Parse(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidDataException(Localization.T("配置不能为空。 ", "The profile cannot be empty."));
            if (text.Length > MaximumProfileBytes || Encoding.UTF8.GetByteCount(text) > MaximumProfileBytes) throw new InvalidDataException(Localization.T("配置不能超过 8 MiB。 ", "The profile cannot exceed 8 MiB."));
            try
            {
                // Inspect syntax before RepresentationModel recursively constructs its graph.
                Parser parser = new Parser(new StringReader(text));
                int depth = 0, events = 0, aliases = 0;
                Stack<ParseFrame> frames = new Stack<ParseFrame>();
                while (parser.MoveNext())
                {
                    if (++events > MaximumNodes * 2) throw new InvalidDataException(Localization.T("配置包含过多 YAML 节点。 ", "The profile contains too many YAML nodes."));
                    bool beginsNode = parser.Current is MappingStart || parser.Current is SequenceStart || parser.Current is Scalar || parser.Current is AnchorAlias;
                    if (beginsNode && frames.Count > 0 && frames.Peek().IsMapping)
                    {
                        ParseFrame parent = frames.Peek();
                        if (parent.ExpectsKey)
                        {
                            // Reject complex/cyclic keys before the model can hash them.
                            Scalar keyEvent = parser.Current as Scalar;
                            if (keyEvent == null || String.IsNullOrEmpty(keyEvent.Value)) throw new InvalidDataException(Localization.T("YAML 键必须是非空标量，不能是复杂对象或别名。 ", "YAML keys must be nonempty scalars, not complex objects or aliases."));
                            if (!parent.Keys.Add(keyEvent.Value)) throw new InvalidDataException(Localization.T("YAML 包含重复键：", "Duplicate YAML key: ") + keyEvent.Value);
                        }
                        parent.ExpectsKey = !parent.ExpectsKey;
                    }
                    if (parser.Current is MappingStart || parser.Current is SequenceStart)
                    {
                        if (++depth > 64) throw new InvalidDataException(Localization.T("YAML 嵌套深度不能超过 64。 ", "YAML nesting cannot exceed 64 levels."));
                        frames.Push(new ParseFrame { IsMapping = parser.Current is MappingStart });
                    }
                    else if (parser.Current is MappingEnd || parser.Current is SequenceEnd) { depth--; frames.Pop(); }
                    else if (parser.Current is AnchorAlias && ++aliases > 128) throw new InvalidDataException(Localization.T("YAML 别名最多可引用 128 次。 ", "YAML aliases can be referenced at most 128 times."));
                }
                YamlStream yaml = new YamlStream();
                yaml.Load(new StringReader(text));
                if (yaml.Documents.Count != 1 || !(yaml.Documents[0].RootNode is YamlMappingNode)) throw new InvalidDataException(Localization.T("配置必须包含且只包含一个 YAML 根映射。 ", "The profile must contain exactly one YAML root mapping."));
                int count = 0;
                return (YamlMappingNode)Clone(yaml.Documents[0].RootNode, new HashSet<YamlNode>(new NodeReferenceComparer()), 0, ref count);
            }
            catch (YamlException ex) { throw new InvalidDataException(Localization.T("YAML 配置格式错误：", "Invalid YAML profile: ") + ex.Message, ex); }
        }

        private static YamlNode Clone(YamlNode source, HashSet<YamlNode> active, int depth, ref int count)
        {
            if (depth > 64 || ++count > MaximumNodes) throw new InvalidDataException(Localization.T("YAML 展开后过大或嵌套过深。 ", "Expanded YAML is too large or too deeply nested."));
            YamlScalarNode scalar = source as YamlScalarNode;
            if (scalar != null) return new YamlScalarNode(scalar.Value) { Style = scalar.Style, Tag = scalar.Tag };
            if (!active.Add(source)) throw new InvalidDataException(Localization.T("YAML 不能包含循环别名。 ", "YAML cannot contain cyclic aliases."));
            try
            {
                YamlSequenceNode sequence = source as YamlSequenceNode;
                if (sequence != null)
                {
                    YamlSequenceNode copy = new YamlSequenceNode();
                    foreach (YamlNode child in sequence.Children) copy.Add(Clone(child, active, depth + 1, ref count));
                    return copy;
                }
                YamlMappingNode mapping = source as YamlMappingNode;
                if (mapping == null) throw new InvalidDataException(Localization.T("不支持此 YAML 节点类型。 ", "This YAML node type is not supported."));
                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                YamlMappingNode result = new YamlMappingNode();
                // Expand merge keys first so all runtime overrides operate on explicit keys.
                foreach (KeyValuePair<YamlNode, YamlNode> pair in mapping.Children)
                {
                    YamlScalarNode key = pair.Key as YamlScalarNode;
                    if (key == null || key.Value == null) throw new InvalidDataException(Localization.T("YAML 键必须是非空标量，不能是复杂对象。 ", "YAML keys must be nonempty scalars, not complex objects."));
                    if (!keys.Add(key.Value)) throw new InvalidDataException(Localization.T("YAML 包含重复键：", "Duplicate YAML key: ") + key.Value);
                    if (key.Value == "<<")
                    {
                        YamlNode merge = Clone(pair.Value, active, depth + 1, ref count);
                        YamlMappingNode mergeMap = merge as YamlMappingNode;
                        if (mergeMap != null) MergeMissing(result, mergeMap);
                        else
                        {
                            YamlSequenceNode mergeSequence = merge as YamlSequenceNode;
                            if (mergeSequence == null) throw new InvalidDataException(Localization.T("YAML 合并键必须引用映射或映射列表。 ", "YAML merge keys must reference a mapping or a list of mappings."));
                            foreach (YamlNode item in mergeSequence.Children)
                            {
                                YamlMappingNode itemMap = item as YamlMappingNode;
                                if (itemMap == null) throw new InvalidDataException(Localization.T("YAML 合并列表只能包含映射。 ", "YAML merge lists can contain only mappings."));
                                MergeMissing(result, itemMap);
                            }
                        }
                    }
                }
                foreach (KeyValuePair<YamlNode, YamlNode> pair in mapping.Children)
                {
                    string key = ((YamlScalarNode)pair.Key).Value;
                    if (key != "<<") Put(result, key, Clone(pair.Value, active, depth + 1, ref count));
                }
                return result;
            }
            finally { active.Remove(source); }
        }

        private static void MergeMissing(YamlMappingNode target, YamlMappingNode source)
        {
            foreach (KeyValuePair<YamlNode, YamlNode> pair in source.Children)
            {
                string key = ((YamlScalarNode)pair.Key).Value;
                if (Get(target, key) == null) Put(target, key, pair.Value);
            }
        }

        private static void CheckProviders(YamlMappingNode root, string kind, string destination)
        {
            YamlNode node = Get(root, kind);
            if (node == null) return;
            YamlMappingNode providers = node as YamlMappingNode;
            if (providers == null) throw new InvalidDataException(kind + Localization.T(" 必须是映射。 ", " must be a mapping."));
            foreach (KeyValuePair<YamlNode, YamlNode> pair in providers.Children)
            {
                string name = ((YamlScalarNode)pair.Key).Value;
                YamlMappingNode provider = pair.Value as YamlMappingNode;
                if (provider == null) throw new InvalidDataException(Localization.T("Provider 必须是映射：", "Provider must be a mapping: ") + name);
                YamlNode path = Get(provider, "path");
                if (path != null)
                {
                    YamlScalarNode pathScalar = path as YamlScalarNode;
                    if (pathScalar == null) throw new InvalidDataException(Localization.T("Provider path 必须是文本。 ", "Provider path must be text."));
                    SafeProviderPath(pathScalar.Value);
                }
                YamlScalarNode typeNode = Get(provider, "type") as YamlScalarNode;
                string type = typeNode == null ? null : typeNode.Value;
                if (type == "http")
                {
                    YamlScalarNode url = Get(provider, "url") as YamlScalarNode;
                    if (url == null || String.IsNullOrWhiteSpace(url.Value)) throw new InvalidDataException(Localization.T("HTTP Provider 缺少 URL。 ", "HTTP provider is missing a URL."));
                    ValidateSourceUrl(url.Value);
                }
                if (destination != null)
                {
                    if (type == "inline") Remove(provider, "path");
                    else
                    {
                        Directory.CreateDirectory(destination);
                        string extension = ".yaml";
                        YamlScalarNode format = Get(provider, "format") as YamlScalarNode;
                        if (format != null && format.Value == "mrs") extension = ".mrs";
                        else if (format != null && format.Value == "text") extension = ".txt";
                        string managed = Path.Combine(destination, (kind == "proxy-providers" ? "proxy-" : "rule-") + Hash(name) + extension);
                        Put(provider, "path", Scalar(managed.Replace('\\', '/')));
                    }
                }
            }
        }

        private static void SafeProviderPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length > 512 || path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0 || path.StartsWith("/") || path.StartsWith("\\") || Path.IsPathRooted(path))
                throw new InvalidDataException(Localization.T("Provider path 只能使用配置目录内的相对路径。 ", "Provider path must be relative to the profile directory."));
            foreach (string part in path.Replace('\\', '/').Split('/'))
                if (part == ".." || part.TrimEnd(' ', '.') == "" && part != ".") throw new InvalidDataException(Localization.T("Provider path 不能包含路径跳转或空路径段。 ", "Provider path cannot contain traversal or empty path segments."));
        }

        private static string Hash(string text)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 32).ToLowerInvariant();
        }

        private static YamlScalarNode Scalar(string value) { return new YamlScalarNode(value); }
        private static YamlScalarNode Number(int value) { return Scalar(value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        private static YamlNode Get(YamlMappingNode map, string key) { YamlNode value; return map.Children.TryGetValue(Scalar(key), out value) ? value : null; }
        private static void Put(YamlMappingNode map, string key, YamlNode value) { map.Children[Scalar(key)] = value; }
        private static void Default(YamlMappingNode map, string key, YamlNode value) { if (Get(map, key) == null) Put(map, key, value); }
        private static void Remove(YamlMappingNode map, string key) { map.Children.Remove(Scalar(key)); }

        private static string Serialize(YamlMappingNode root)
        {
            StringWriter writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            new YamlStream(new YamlDocument(root)).Save(writer, false);
            string text = writer.ToString();
            if (Encoding.UTF8.GetByteCount(text) > MaximumProfileBytes) throw new InvalidDataException(Localization.T("规范化后的配置不能超过 8 MiB。 ", "The normalized profile cannot exceed 8 MiB."));
            return text;
        }

        private static void AtomicWrite(string path, string text)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(parent);
            string temporary = Path.Combine(parent, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private sealed class NodeReferenceComparer : IEqualityComparer<YamlNode>
        {
            public bool Equals(YamlNode left, YamlNode right) { return Object.ReferenceEquals(left, right); }
            public int GetHashCode(YamlNode node) { return RuntimeHelpers.GetHashCode(node); }
        }

        private sealed class ParseFrame
        {
            public bool IsMapping;
            public bool ExpectsKey = true;
            public readonly HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
