using System;
using System.Diagnostics;
using System.IO;

using Microsoft.Win32;

namespace BootLauncherLite.Services
{
    public static class StartupRegistrationService
    {
        private const string RegistryRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegistryRunValueName = "BootLauncher";

        private const string TaskName = "BootLauncherElevated";

        // Common args for any "autorun" startup:
        //  --autorun       = tells the app it's starting from Windows
        //  --autorun-tray  = our flag to start hidden to tray
        private const string AutorunArgs = "--autorun --autorun-tray";

        /// <summary>
        /// Old global check: "is this app registered in any way (registry OR task)?"
        /// </summary>
        public static bool IsRegistered()
        {
            return IsRegisteredInRegistry() || IsTaskRegistered();
        }

        /// <summary>
        /// NEW overload: check only the given mode (RegistryRun or TaskSchedulerElevated).
        /// </summary>
        public static bool IsRegistered(StartupMode mode)
        {
            return mode switch
            {
                StartupMode.RegistryRun => IsRegisteredInRegistry(),
                StartupMode.TaskSchedulerElevated => IsTaskRegistered(),
                _ => false
            };
        }

        /// <summary>
        /// Old simple "register" – keep for backward compatibility.
        /// Defaults to registry HKCU\Run.
        /// </summary>
        public static void Register()
        {
            Register(StartupMode.RegistryRun);
        }

        /// <summary>
        /// NEW: register according to selected mode.
        /// </summary>
        public static void Register(StartupMode mode)
        {
            // Clean up any previous registrations so we don't end up with both.
            UnregisterAll();

            switch (mode)
            {
                case StartupMode.RegistryRun:
                    RegisterInRegistry();
                    break;

                case StartupMode.TaskSchedulerElevated:
                    RegisterTask();
                    break;
            }
        }

        /// <summary>
        /// Old simple "unregister" – keep for backward compatibility.
        /// </summary>
        public static void Unregister()
        {
            UnregisterAll();
        }

        /// <summary>
        /// Remove both HKCU\Run and scheduled task entries (safety).
        /// </summary>
        public static void UnregisterAll()
        {
            UnregisterFromRegistry();
            UnregisterTask();
        }

        // ============================================================
        // Registry (HKCU\Run)
        // ============================================================

        private static bool IsRegisteredInRegistry()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKeyPath, false);
            return key?.GetValue(RegistryRunValueName) != null;
        }

        private static void RegisterInRegistry()
        {
            string exePath = Process.GetCurrentProcess().MainModule!.FileName!;
            exePath = Path.GetFullPath(exePath);

            // We always start with autorun + tray flags for HKCU run
            string args = AutorunArgs;

            using var key = Registry.CurrentUser.CreateSubKey(RegistryRunKeyPath, true);
            key.SetValue(RegistryRunValueName, $"\"{exePath}\" {args}");
        }

        private static void UnregisterFromRegistry()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKeyPath, writable: true);
            if (key == null)
                return;

            key.DeleteValue(RegistryRunValueName, throwOnMissingValue: false);
        }

        // ============================================================
        // Task Scheduler (elevated)
        // ============================================================

        private static bool IsTaskRegistered()
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{TaskName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (proc == null)
                    return false;

                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(2000);

                Debug.WriteLine($"[StartupRegistrationService] schtasks /Query output: {output}");
                if (!string.IsNullOrWhiteSpace(error))
                    Debug.WriteLine($"[StartupRegistrationService] schtasks /Query error: {error}");

                return output.IndexOf(TaskName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupRegistrationService] IsTaskRegistered exception: {ex}");
                return false;
            }
        }

        private static void RegisterTask()
        {
            string exePath = Process.GetCurrentProcess().MainModule!.FileName!;
            exePath = Path.GetFullPath(exePath);

            // Same args as registry: autorun + tray
            string args = AutorunArgs;

            // NOTE:
            //  /SC ONLOGON   -> run at user logon
            //  /RL HIGHEST   -> run with highest privileges (elevated)
            //  /F            -> force overwrite existing
            string arguments =
                $"/Create /TN \"{TaskName}\" " +
                $"/TR \"\\\"{exePath}\\\" {args}\" " +
                "/SC ONLOGON /RL HIGHEST /F";

            RunSchtasks(arguments);
        }

        private static void UnregisterTask()
        {
            string arguments = $"/Delete /TN \"{TaskName}\" /F";
            RunSchtasks(arguments);
        }

        private static void RunSchtasks(string arguments)
        {
            try
            {
                Debug.WriteLine($"[StartupRegistrationService] Running: schtasks.exe {arguments}");

                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (proc == null)
                    return;

                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(5000);

                if (!string.IsNullOrWhiteSpace(output))
                    Debug.WriteLine($"[StartupRegistrationService] schtasks output: {output}");
                if (!string.IsNullOrWhiteSpace(error))
                    Debug.WriteLine($"[StartupRegistrationService] schtasks error: {error}");

                if (proc.ExitCode != 0)
                {
                    Debug.WriteLine($"[StartupRegistrationService] schtasks exit code: {proc.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                // Not fatal for the app, but useful for debugging
                Debug.WriteLine($"[StartupRegistrationService] RunSchtasks exception: {ex}");
            }
        }
    }
}

