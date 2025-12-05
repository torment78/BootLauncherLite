using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;


namespace BootLauncherLite.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        public string SettingsDirectory { get; }

        // Separate, admin-only file for trusted RunAsAdmin launch items
        private static string GetPrivilegedLaunchListPath()
        {
            // %ProgramData%\BootLauncher\launch-items-admin.json
            var progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var dir = Path.Combine(progData, "BootLauncherLite");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "launch-items-admin.json");
        }

        public SettingsService(string? customPath = null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            SettingsDirectory = Path.Combine(appData, "BootLauncherLite");
            Directory.CreateDirectory(SettingsDirectory);

            _settingsPath = customPath ?? Path.Combine(SettingsDirectory, "settings.json");
        }

        public AppSettings Load()
        {
            AppSettings settings;

            try
            {
                if (!File.Exists(_settingsPath))
                    settings = new AppSettings();
                else
                {
                    var json = File.ReadAllText(_settingsPath);
                    settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // On any error, fall back to defaults
                settings = new AppSettings();
            }

            // ---- Harden RunAsAdmin against UAC bypass ----
            // We do NOT trust RunAsAdmin flags stored in user-writable AppData.
            // Instead, we recompute RunAsAdmin from a privileged list in ProgramData.

            try
            {
                var privilegedPath = GetPrivilegedLaunchListPath();
                if (File.Exists(privilegedPath))
                {
                    var jsonPriv = File.ReadAllText(privilegedPath);
                    var privilegedItems = JsonSerializer.Deserialize<List<LaunchItem>>(jsonPriv)
                                          ?? new List<LaunchItem>();

                    // Build a set of "trusted admin" keys by normalized path
                    var adminKeySet = new HashSet<string>(
                        privilegedItems
                            .Select(li => NormalizePath(li.FullPath))
                            .Where(k => !string.IsNullOrEmpty(k))!,
                        StringComparer.OrdinalIgnoreCase);

                    // 1) Ensure settings.LaunchItems exists
                    if (settings.LaunchItems == null)
                        settings.LaunchItems = new List<LaunchItem>();

                    // 2) Force RunAsAdmin based on privileged list only
                    foreach (var li in settings.LaunchItems)
                    {
                        var key = NormalizePath(li.FullPath);
                        bool isAdmin = key != null && adminKeySet.Contains(key);
                        li.RunAsAdmin = isAdmin;
                    }

                    // 3) Add any privileged items that are missing from the user list
                    foreach (var priv in privilegedItems)
                    {
                        var key = NormalizePath(priv.FullPath);
                        if (key == null)
                            continue;

                        bool exists = settings.LaunchItems.Any(li =>
                            string.Equals(NormalizePath(li.FullPath), key, StringComparison.OrdinalIgnoreCase));

                        if (!exists)
                        {
                            settings.LaunchItems.Add(priv);
                        }
                    }
                }
                else
                {
                    // No privileged file -> nobody is trusted admin. Clear RunAsAdmin flags.
                    if (settings.LaunchItems != null)
                    {
                        foreach (var li in settings.LaunchItems)
                            li.RunAsAdmin = false;
                    }
                }
            }
            catch
            {
                // If privileged file fails for any reason, we do NOT grant admin.
                if (settings.LaunchItems != null)
                {
                    foreach (var li in settings.LaunchItems)
                        li.RunAsAdmin = false;
                }
            }

            return settings;
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

                var json = JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                // 1) Save full settings (including LaunchItems) to user AppData.
                //    RunAsAdmin bits here are NOT trusted on load, so it's safe.
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // Ignore errors to not crash the app on save fail
            }

            // 2) Save privileged RunAsAdmin list to ProgramData.
            //    Only processes with sufficient rights (admin) can actually write this.
            //    Non-admin processes will get UnauthorizedAccess, which we swallow.
            try
            {
                var privilegedPath = GetPrivilegedLaunchListPath();

                var adminsOnly = (settings.LaunchItems ?? new List<LaunchItem>())
                    .Where(li => li.RunAsAdmin)
                    .ToList();

                var jsonPriv = JsonSerializer.Serialize(
                    adminsOnly,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(privilegedPath, jsonPriv);
            }
            catch
            {
                // Non-admin or locked path -> we just don't update the privileged list.
                // This prevents a non-admin from escalating by writing to ProgramData.
            }
        }

        private static string? NormalizePath(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return null;
            }
        }
    }

    public class ToastThemeSettings
    {
        public string BackgroundHex { get; set; } = "#202020";
        public double BackgroundOpacity { get; set; } = 0.9;

        public string BorderHex { get; set; } = "#404040";

        public string ButtonBackgroundHex { get; set; } = "#333333";
        public string ButtonBorderHex { get; set; } = "#555555";
        public string ButtonTextHex { get; set; } = "#FFFFFF";

        public string ButtonHoverBackgroundHex { get; set; } = "#444444";
        public string ButtonHoverBorderHex { get; set; } = "#777777";

        public string HeaderTextHex { get; set; } = "#FFFFFF";
        public string BodyTextHex { get; set; } = "#DDDDDD";
    }


    // === SINGLE source of truth for app settings ===
    public class AppSettings
    {
        // Startup sequence items
        public List<LaunchItem> LaunchItems { get; set; } = new();

        // Wake-on-LAN / master-slave
        public List<RemoteMachine> RemoteMachines { get; set; } = new();

        // Audio (we use separate playback + capture ids)
        public string? SelectedPlaybackDeviceId { get; set; }
        public string? SelectedCaptureDeviceId { get; set; }

        // Old field (safe to keep if you had it before; not used now)
        public string? SelectedAudioDeviceId { get; set; }

        // Behavior
        public bool AutoCloseWhenDone { get; set; } = false;
        public bool IsMasterMode { get; set; } = true;

        // === THEME COLORS ===
        public string ThemeOuterBackground { get; set; } = "#0B0E13";
        public string ThemeCardBackground { get; set; } = "#111827";
        public string ThemeTextColor { get; set; } = "#FFFFFF";

        public string ThemeControlBackground { get; set; } = "#1F2937";
        public string ThemeControlForeground { get; set; } = "#FFFFFF";

        public string ThemeGridRow { get; set; } = "#1F2937";
        public string ThemeGridAltRow { get; set; } = "#111827";
        public string ThemeGridLines { get; set; } = "#374151";

        public string ThemeTitleBar { get; set; } = "#111827";
        public string ThemeTitleBarText { get; set; } = "#FFFFFF";

        // Buttons
        public string ThemeButtonBackground { get; set; } = "#1F2937";
        public string ThemeButtonForeground { get; set; } = "#FFFFFF";
        public string ThemeButtonBorder { get; set; } = "#374151";
        public string ThemeButtonHoverBackground { get; set; } = "#2B5EBF";
        public string ThemeButtonHoverBorder { get; set; } = "#60A5FA";
        public string ThemeButtonPressedBackground { get; set; } = "#1E40AF";

        // GroupBox brushes (for RoundedGroupBox)
        public string ThemeGroupBoxBackground { get; set; } = "#1F2937";
        public string ThemeGroupBoxBorder { get; set; } = "#374151";
        public string ThemeGroupBoxForeground { get; set; } = "#FFFFFF";

        // ComboBox brushes (for ThemedComboBox)
        public string ThemeComboBackground { get; set; } = "#1F2937";
        public string ThemeComboForeground { get; set; } = "#FFFFFF";
        public string ThemeComboHoverBackground { get; set; } = "#374151";

        // Launch grid area
        public string ThemeLaunchGridAreaBackground { get; set; } = "#aabd19";

        // DataGrid
        public string ThemeDataGridBackground { get; set; } = "#111827";
        public string ThemeDataGridHover { get; set; } = "#2B5EBF";
        public string ThemeDataGridSelected { get; set; } = "#2B3440";
        public string ThemeDataGridForeground { get; set; } = "#FFFFFF";
        public string ThemeCardHeaderForeground { get; set; } = "#E5E7EB";

        public string RowColor { get; set; } = "#1F2937";   // default dark row
        public string RowHoverColor { get; set; } = "#2B5EBF";
        public string RowSelectedColor { get; set; } = "#2B3440";

        public string ThemeDataGridHeaderBackground { get; set; } = "#111827";
        public string ThemeDataGridHeaderForeground { get; set; } = "#FFFFFF";

        // Startup delay for --autorun mode
        public int AutorunDelaySeconds { get; set; } = 10;

        public bool ToastOnRight { get; set; } = true;  // default: right side
        public ToastThemeSettings ToastTheme { get; set; } = new ToastThemeSettings();
        // NEW: where/how BootLauncher registers itself for startup
        //  - RegistryRun           → HKCU\Software\Microsoft\Windows\CurrentVersion\Run
        //  - TaskSchedulerElevated → Task Scheduler task (run with highest privileges)
        public StartupMode StartupMode { get; set; } = StartupMode.RegistryRun;
    }
}

