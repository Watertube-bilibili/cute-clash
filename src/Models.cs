using System;
using System.Collections.Generic;

namespace CuteClash
{
    public sealed class AppSettings
    {
        public string Language { get; set; }
        public int MixedPort { get; set; }
        public int ControllerPort { get; set; }
        public string Mode { get; set; }
        public bool TunEnabled { get; set; }
        public bool SystemProxyEnabled { get; set; }
        public string SelectedProfileId { get; set; }
        public List<ProfileInfo> Profiles { get; set; }
        public AppSettings()
        {
            Language = "zh-CN";
            MixedPort = 7890; ControllerPort = 19090; Mode = "rule";
            Profiles = new List<ProfileInfo>();
        }
    }
    public sealed class ProfileInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SourceUrl { get; set; }
        public DateTime UpdatedAt { get; set; }
        public override string ToString() { return Name; }
    }
    public sealed class ProxyGroup
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Current { get; set; }
        public List<string> Nodes { get; set; }
        public override string ToString() { return Name; }
    }
    public sealed class RuntimeSnapshot
    {
        public long UploadTotal { get; set; }
        public long DownloadTotal { get; set; }
        public int Connections { get; set; }
        public string Version { get; set; }
    }
}
