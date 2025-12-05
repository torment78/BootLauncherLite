

    namespace BootLauncherLite
    {
        public partial class App : System.Windows.Application
        {
            private SettingsService _settingsService;

            // Optional: global access to current settings if you want
            public static AppSettings CurrentSettings { get; private set; }

            protected override void OnStartup(StartupEventArgs e)
            {
                base.OnStartup(e);

#if !DEBUG
            // In Release / published build: do self-install
            if (HandleSelfInstall())
                return;
#endif

                // ✅ Init settings and apply toast theme after we know we're not exiting
                _settingsService = new SettingsService();
                CurrentSettings = _settingsService.Load();

                // Apply toast color scheme to global resources
                ToastThemeBrushHelper.ApplyToastThemeToResources(
                    this.Resources,
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
                    this.Resources,
                    CurrentSettings.ToastTheme);

                // Persist the change
                _settingsService.Save(CurrentSettings);
            }

            /// <summary>
            /// If the app is not running from our desired "install" location,
            /// copy itself into Documents\BootLauncherLite and create a desktop shortcut.
            /// Then launch the installed copy and exit this instance.
            /// Returns true if we *exited* (so caller should stop).
            /// </summary>
            private bool HandleSelfInstall()
            {
                // Where we want the app to live – SAME as SettingsService folder
                string targetFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BootLauncherLite");

                string targetExe = Path.Combine(targetFolder, "BootLauncherLite.exe");

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
                    System.Windows.MessageBox.Show(
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




