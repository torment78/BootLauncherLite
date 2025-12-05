namespace BootLauncherLite.Models
{
    public class RemoteMachine
    {
        public string Name { get; set; } = string.Empty;      // Friendly name
        public string IpAddress { get; set; } = string.Empty; // For your info only right now
        public string MacAddress { get; set; } = string.Empty;
        public bool IsSelected { get; set; }                  // Included in WOL batch
    }
}

