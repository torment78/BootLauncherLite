

namespace BootLauncherLite.Services
{
    public class ProcessPickerService
    {
        public IEnumerable<(string ProcessName, string? FilePath)> GetProcesses()
        {
            foreach (var p in Process.GetProcesses())
            {
                string? path = null;
                try
                {
                    path = p.MainModule?.FileName;
                }
                catch
                {
                    // some processes will throw (access denied / 32 vs 64 bit)
                }

                yield return (p.ProcessName, path);
            }
        }
    }
}

