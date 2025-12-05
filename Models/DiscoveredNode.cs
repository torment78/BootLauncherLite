namespace BootLauncherLite.Models
{
    public class DiscoveredNode
    {
        public string Name { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string Mode { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public DateTime LastSeen { get; set; }
        public bool IsSelf { get; set; }
        public List<string> AllIps { get; set; } = new();
        // NEW: everything this slave reported in its heartbeat
        public List<RemoteDeviceInfo> Devices { get; set; } = new();
    }

    public class RemoteDeviceInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";   // e.g. "Playback", "Capture", "App", etc.
        public string Path { get; set; } = "";   // optional
        public string Extra { get; set; } = "";  // description, friendly text, etc.
    }


}

