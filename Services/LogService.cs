

namespace BootLauncherLite.Services
{
    public class LogService
    {
        public ObservableCollection<string> Entries { get; } = new();

        private readonly string _logFilePath;

        public LogService(string? customPath = null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "BootLauncher", "logs");
            Directory.CreateDirectory(dir);

            _logFilePath = customPath ?? Path.Combine(dir, "BootLauncher.log");
        }

        public void Log(string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss}  {message}";

            // Make sure we add on UI thread (WPF Application)
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Entries.Add(line);
                if (Entries.Count > 1000)
                {
                    Entries.RemoveAt(0);
                }
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
    }
}

