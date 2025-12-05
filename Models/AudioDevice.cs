namespace BootLauncherLite.Models
{
    public enum AudioDeviceFlow
    {
        Render,   // speakers, headphones
        Capture   // microphones
    }

    public class AudioDevice
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AudioDeviceFlow Flow { get; set; }
        public bool IsDefault { get; set; }
    }
}

