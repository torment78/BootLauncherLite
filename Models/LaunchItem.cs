using System.ComponentModel;

namespace BootLauncherLite.Models
{
    public class LaunchItem : INotifyPropertyChanged
    {
        private int _order;

        public int Order
        {
            get => _order;
            set
            {
                if (_order != value)
                {
                    _order = value;
                    OnPropertyChanged(nameof(Order));
                }
            }
        }

        public string FullPath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        // delay in ms
        public int DelayMs { get; set; }
        public int? MinimizeInitialDelayMs { get; set; }
        public bool StartMinimized { get; set; }
        public bool ForceMinimize { get; set; }
        public bool StartToTray { get; set; }
        public bool RunAsAdmin { get; set; } = false;
        public bool UseCmdRelay { get; set; }  // Launch via cmd.exe shim

        // NEW: optional arguments
        public string? Arguments { get; set; }

        // NEW: optional working directory
        public string? WorkingDirectory { get; set; }

        // NEW: if true, this item will *kill* a process instead of launching FullPath
        public bool KillInsteadOfLaunch { get; set; }

        // NEW: process name to kill, e.g. "vlc.exe", "obs64.exe"
        public string? KillProcessName { get; set; }
        public bool CloseToTray { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

