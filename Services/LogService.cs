using System;
using System.Collections.ObjectModel;
using System.IO;

namespace BootLauncher.Services
{
    public class LogService
    {
        public ObservableCollection<string> Entries { get; } = new();

        private readonly string _logFilePath;
        private readonly string _markerFilePath;

        public LogService(string? customPath = null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "BootLauncherLite", "logs");
            Directory.CreateDirectory(dir);

            _logFilePath = customPath ?? Path.Combine(dir, "BootLauncherLite.log");
            _markerFilePath = Path.Combine(dir, "boot.marker");

            ResetLogIfNewBoot();
        }

        public void Log(string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss}  {message}";

            // Make sure we add on UI thread (WPF Application)
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Entries.Add(line);
                if (Entries.Count > 1000)
                    Entries.RemoveAt(0);
            });

            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // logging to disk is best-effort
            }
        }

        private void ResetLogIfNewBoot()
        {
            try
            {
                // Approx boot time in UTC: "now - uptime"
                // TickCount64 is ms since system start (resets every boot)
                var bootUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

                // Store as a simple stamp (rounded to minute to avoid tiny clock drift issues)
                string bootStamp = bootUtc.ToString("yyyy-MM-dd HH:mm");

                string previous = "";
                if (File.Exists(_markerFilePath))
                {
                    try { previous = File.ReadAllText(_markerFilePath).Trim(); }
                    catch { previous = ""; }
                }

                if (!string.Equals(previous, bootStamp, StringComparison.Ordinal))
                {
                    // New boot detected -> reset log
                    try
                    {
                        if (File.Exists(_logFilePath))
                            File.Delete(_logFilePath);
                    }
                    catch
                    {
                        // If delete fails (locked), we just continue best-effort.
                    }

                    try
                    {
                        File.WriteAllText(_markerFilePath, bootStamp);
                    }
                    catch
                    {
                        // ignore marker write errors
                    }

                    // Optional header line for clarity
                    try
                    {
                        File.AppendAllText(
                            _logFilePath,
                            $"=== New boot detected ({bootStamp} UTC) ==={Environment.NewLine}");
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // Never let logging break startup
            }
        }
    }
}
