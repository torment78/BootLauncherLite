using System;
using System.Diagnostics;
using System.IO;

namespace BootLauncherLite.Services
{
    /// <summary>
    /// Registers BootLauncher as an elevated ONLOGON scheduled task
    /// that runs: BootLauncher.exe --autorun
    /// </summary>
    public static class TaskSchedulerStartupService
    {
        private const string TaskName = "BootLauncher (Elevated Autorun)";

        /// <summary>
        /// Returns true if the scheduled task exists.
        /// </summary>
        public static bool IsRegistered()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{TaskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null)
                    return false;

                p.WaitForExit(3000);
                // ExitCode 0 = task exists, non-zero = not found / error
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create / update the elevated autorun task.
        /// Must be run elevated (admin), otherwise schtasks will fail.
        /// </summary>
        public static void Register()
        {
            // Resolve current EXE path
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                             ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

            exePath = Path.GetFullPath(exePath);

            // We want Task Scheduler to run:
            // "C:\path\BootLauncher.exe" --autorun
            string taskRun = $"\"{exePath}\" --autorun";

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Create /TN \"{TaskName}\" /TR \"{taskRun}\" /SC ONLOGON /RL HIGHEST /F",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null)
                throw new InvalidOperationException("Failed to start schtasks.exe");

            p.WaitForExit(5000);

            if (p.ExitCode != 0)
            {
                string err = p.StandardError.ReadToEnd();
                throw new InvalidOperationException(
                    $"schtasks /Create failed (code {p.ExitCode}).\n{err}");
            }
        }

        /// <summary>
        /// Remove the scheduled task (if present).
        /// </summary>
        public static void Unregister()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{TaskName}\" /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null)
                    return;

                p.WaitForExit(5000);
                // Ignore exit code – if it doesn't exist we don't care
            }
            catch
            {
                // swallow
            }
        }
    }
}

