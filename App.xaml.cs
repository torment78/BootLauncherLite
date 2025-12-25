using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows;

namespace BootLauncherLite
{
    public partial class App : Application
    {
        private SettingsService _settingsService;

        // Optional: global access to current settings if you want
        public static AppSettings CurrentSettings { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

#if !DEBUG
            // Release build: self-install only when truly needed (one-time, non-elevated)
            if (ShouldSelfInstall(e.Args))
            {
                if (HandleSelfInstall())
                    return;
            }
#endif

            // ✅ Init settings and apply toast theme after we know we're not exiting
            _settingsService = new SettingsService();
            CurrentSettings = _settingsService.Load();

            // Apply toast color scheme to global resources
            ToastThemeBrushHelper.ApplyToastThemeToResources(
                Resources,
                CurrentSettings.ToastTheme);

            // If you use StartupUri="MainWindow.xaml" in App.xaml,
            // WPF will create MainWindow automatically after base.OnStartup.
        }

        /// <summary>
        /// Public helper so the settings window can reapply + save
        /// when the user changes toast colors.
        /// </summary>
        public void ReapplyToastTheme()
        {
            if (CurrentSettings == null)
                return;

            ToastThemeBrushHelper.ApplyToastThemeToResources(
                Resources,
                CurrentSettings.ToastTheme);

            // Persist the change
            _settingsService.Save(CurrentSettings);
        }

        // ------------------------------------------------------------
        // Install path helpers (must exist in Debug too because
        // HandleSelfInstall() compiles in both Debug and Release).
        // ------------------------------------------------------------
        private static string GetInstallExePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BootLauncherLite",
                "BootLauncherLite.exe");
        }

        private static bool IsInstalled()
        {
            return File.Exists(GetInstallExePath());
        }

        // ------------------------------------------------------------
        // Elevation detection (do NOT depend on MainWindow here).
        // ------------------------------------------------------------
        private static bool IsProcessElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

#if !DEBUG
        private bool ShouldSelfInstall(string[] args)
        {
            // 1) Never self-install when elevated (avoids wrong AppData/profile + relaunch loops)
            if (IsProcessElevated())
                return false;

            // 2) If installed copy already exists -> do nothing
            if (IsInstalled())
                return false;

            // 3) If we're already running from the install path -> do nothing
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName!;
            if (currentExe.Equals(GetInstallExePath(), StringComparison.OrdinalIgnoreCase))
                return false;

            // 4) Avoid self-install during autorun/tray startup if Lite ever adds these flags
            if (args != null && args.Any(a =>
                    a.Equals("--autorun", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
                return false;

            // 5) First real manual run and not installed yet -> OK to self-install once
            return true;
        }
#endif

        /// <summary>
        /// If the app is not running from our desired "install" location,
        /// copy itself into AppData\Roaming\BootLauncherLite and create a desktop shortcut.
        /// Then launch the installed copy and exit this instance.
        /// Returns true if we *exited* (so caller should stop).
        /// </summary>
        private bool HandleSelfInstall()
        {
            string targetExe = GetInstallExePath();
            string targetFolder = Path.GetDirectoryName(targetExe)!;

            string currentExe = Process.GetCurrentProcess().MainModule!.FileName!;

            if (currentExe.Equals(targetExe, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                Directory.CreateDirectory(targetFolder);
                File.Copy(currentExe, targetExe, overwrite: true);

                CreateDesktopShortcut(targetExe, "BootLauncherLite");

                Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    Arguments = "",
                    UseShellExecute = true
                });

                Shutdown();
                return true;
            }
            catch (Exception ex)
            {
#if DEBUG
                MessageBox.Show(
                    $"Self-install failed:\n{ex.Message}",
                    "BootLauncherLite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
#endif
                return false;
            }
        }

        private void CreateDesktopShortcut(string targetPath, string shortcutName)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutLocation = Path.Combine(desktop, $"{shortcutName}.lnk");

                // COM-based shortcut via WScript.Shell
                Type shellType = Type.GetTypeFromProgID("WScript.Shell")
                                  ?? throw new InvalidOperationException("WScript.Shell not available.");

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutLocation);

                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Description = "BootLauncherLite startup sequencer";
                shortcut.IconLocation = targetPath;

                shortcut.Save();
            }
            catch
            {
                // No big deal if shortcut creation fails – app still works.
            }
        }
    }
}
