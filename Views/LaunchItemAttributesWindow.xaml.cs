using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace BootLauncherLite.Views
{
    public partial class LaunchItemAttributesWindow : Window
    {
        private readonly LaunchItem _item;
        private readonly SettingsService _settingsService;
        private readonly AppSettings _currentSettings;

        // ---- DWM interop for dark title bar ----
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_19 = 19; // older builds
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_20 = 20; // newer builds
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        public LaunchItemAttributesWindow(LaunchItem item, SettingsService settingsService)
        {
            InitializeComponent();

            _item = item ?? throw new ArgumentNullException(nameof(item));
            _settingsService = settingsService ?? new SettingsService();

            _currentSettings = _settingsService.Load();
            ApplyTheme(_currentSettings);

            // Existing bindings
            DisplayNameTextBlock.Text = _item.DisplayName;
            PathTextBlock.Text = _item.FullPath;

            ArgumentsTextBox.Text = _item.Arguments ?? string.Empty;
            WorkingDirTextBox.Text = _item.WorkingDirectory ?? string.Empty;
            KillInsteadCheckBox.IsChecked = _item.KillInsteadOfLaunch;
            KillProcessNameTextBox.Text = _item.KillProcessName ?? string.Empty;

            // 👉 per-item admin flag
            RunAsAdminCheckBox.IsChecked = _item.RunAsAdmin;

            // 👉 NEW: per-item minimize delay (ms → seconds, default 5)
            int delayMs = _item.MinimizeInitialDelayMs ?? 5000; // default 5000 ms if null

            if (delayMs <= 0)
                delayMs = 5000;

            if (MinimizeDelayTextBox != null)
                MinimizeDelayTextBox.Text = (delayMs / 1000).ToString();
        }

        // 🔹 Correct place for caption color / text color
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                if (_currentSettings != null)
                    ApplyDarkTitleBarFromTheme(_currentSettings);
            }
            catch
            {
                // Ignore on older Windows / non-DWM setups
            }
        }

        // =========================================================
        //  THEME
        // =========================================================

        private static Color ParseColor(string? hex, string fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                    hex = fallback;

                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString(fallback);
            }
        }

        private void ApplyDarkTitleBarFromTheme(AppSettings settings)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref useDark, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref useDark, sizeof(int));

            var cap = ParseColor(settings.ThemeTitleBar, "#111827");
            var txt = ParseColor(settings.ThemeTitleBarText, "#FFFFFF");

            // COLORREF = 0x00BBGGRR
            int captionColor = (cap.B << 16) | (cap.G << 8) | cap.R;
            int textColor = (txt.B << 16) | (txt.G << 8) | txt.R;

            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }

        public void ApplyTheme(AppSettings settings)
        {
            // ===== Base colors from settings =====
            var outer = ParseColor(settings.ThemeOuterBackground, "#0B0E13");
            var card = ParseColor(settings.ThemeCardBackground, "#111827");
            var text = ParseColor(settings.ThemeTextColor, "#E5E7EB");

            var controlBg = ParseColor(settings.ThemeControlBackground, "#1F2937");
            var controlFg = ParseColor(settings.ThemeControlForeground, "#FFFFFF");

            var gridRow = ParseColor(settings.ThemeGridRow, "#1F2937");
            var gridAlt = ParseColor(settings.ThemeGridAltRow, "#111827");
            var gridLines = ParseColor(settings.ThemeGridLines, "#374151");

            // DataGrid body + header (for brushes this window uses)
            var dataGridBg = ParseColor(settings.ThemeDataGridBackground, "#111827");
            var dataGridHover = ParseColor(settings.ThemeDataGridHover, "#2B5EBF");
            var dataGridSelected = ParseColor(settings.ThemeDataGridSelected, "#2B3440");
            var dataGridFg = ParseColor(settings.ThemeDataGridForeground, "#FFFFFF");
            var headerBg = ParseColor(settings.ThemeDataGridHeaderBackground, "#111827");
            var headerFg = ParseColor(settings.ThemeDataGridHeaderForeground, "#FFFFFF");

            // Buttons
            var btnBg = ParseColor(settings.ThemeButtonBackground, "#1F2937");
            var btnFg = ParseColor(settings.ThemeButtonForeground, "#FFFFFF");
            var btnBorder = ParseColor(settings.ThemeButtonBorder, "#374151");
            var btnHoverBg = ParseColor(settings.ThemeButtonHoverBackground, "#2B5EBF");
            var btnHoverBrd = ParseColor(settings.ThemeButtonHoverBorder, "#60A5FA");
            var btnPressedBg = ParseColor(settings.ThemeButtonPressedBackground, "#1E40AF");

            // GroupBox
            var groupBg = ParseColor(settings.ThemeGroupBoxBackground, "#1F2937");
            var groupBr = ParseColor(settings.ThemeGroupBoxBorder, "#374151");
            var groupFg = text;

            // ComboBox
            var comboBg = ParseColor(settings.ThemeComboBackground, "#1F2937");
            var comboFg = ParseColor(settings.ThemeComboForeground, "#FFFFFF");
            var comboHv = ParseColor(settings.ThemeComboHoverBackground, "#374151");

            // ===== Apply to window =====
            Background = new SolidColorBrush(card);
            Foreground = new SolidColorBrush(text);

            // ===== Push brushes into this window's resources =====

            // GroupBox
            Resources["GroupBoxBackgroundBrush"] = new SolidColorBrush(groupBg);
            Resources["GroupBoxBorderBrush"] = new SolidColorBrush(groupBr);
            Resources["GroupBoxForegroundBrush"] = new SolidColorBrush(groupFg);

            // Buttons
            Resources["ButtonBackgroundBrush"] = new SolidColorBrush(btnBg);
            Resources["ButtonBorderBrush"] = new SolidColorBrush(btnBorder);
            Resources["ButtonForegroundBrush"] = new SolidColorBrush(btnFg);
            Resources["ButtonHoverBackgroundBrush"] = new SolidColorBrush(btnHoverBg);
            Resources["ButtonHoverBorderBrush"] = new SolidColorBrush(btnHoverBrd);
            Resources["ButtonPressedBackgroundBrush"] = new SolidColorBrush(btnPressedBg);

            // DataGrid / row-styled controls (your TextBoxes use DataGridRowBrush)
            Resources["DataGridBackgroundBrush"] = new SolidColorBrush(dataGridBg);
            Resources["DataGridRowBrush"] = new SolidColorBrush(gridRow);
            Resources["DataGridAltRowBrush"] = new SolidColorBrush(gridAlt);
            Resources["DataGridGridLinesBrush"] = new SolidColorBrush(gridLines);
            Resources["DataGridForegroundBrush"] = new SolidColorBrush(dataGridFg);
            Resources["DataGridHoverBrush"] = new SolidColorBrush(dataGridHover);
            Resources["DataGridSelectedBrush"] = new SolidColorBrush(dataGridSelected);
            Resources["DataGridHeaderBackgroundBrush"] = new SolidColorBrush(headerBg);
            Resources["DataGridHeaderForegroundBrush"] = new SolidColorBrush(headerFg);

            // ComboBox global style for this window
            var comboBgBrush = new SolidColorBrush(comboBg);
            var comboFgBrush = new SolidColorBrush(comboFg);

            var comboStyle = new Style(typeof(System.Windows.Controls.ComboBox));
            comboStyle.Setters.Add(
                new Setter(System.Windows.Controls.Control.BackgroundProperty, comboBgBrush));
            comboStyle.Setters.Add(
                new Setter(System.Windows.Controls.Control.ForegroundProperty, comboFgBrush));

            Resources[typeof(System.Windows.Controls.ComboBox)] = comboStyle;

            // Explicitly theme the textboxes
            ArgumentsTextBox.Background = new SolidColorBrush(gridRow);
            WorkingDirTextBox.Background = new SolidColorBrush(gridRow);
            KillProcessNameTextBox.Background = new SolidColorBrush(gridRow);

            // 👉 NEW: theme the minimize delay textbox the same way
            if (MinimizeDelayTextBox != null)
                MinimizeDelayTextBox.Background = new SolidColorBrush(gridRow);

            // ❌ Note: NO DWM call here anymore – we do that in OnSourceInitialized
        }

        // =========================================================
        //  EVENTS
        // =========================================================

        private void BrowseWorkingDir_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog();
            dlg.Description = "Select working directory for this launch item";

            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                WorkingDirTextBox.Text = dlg.SelectedPath;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _item.Arguments = string.IsNullOrWhiteSpace(ArgumentsTextBox.Text)
                ? null
                : ArgumentsTextBox.Text.Trim();

            _item.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirTextBox.Text)
                ? null
                : WorkingDirTextBox.Text.Trim();

            _item.KillInsteadOfLaunch = KillInsteadCheckBox.IsChecked == true;

            _item.KillProcessName = string.IsNullOrWhiteSpace(KillProcessNameTextBox.Text)
                ? null
                : KillProcessNameTextBox.Text.Trim();

            // per-item admin flag
            _item.RunAsAdmin = RunAsAdminCheckBox.IsChecked == true;

            // 👉 NEW: per-item minimize delay (seconds → ms)
            int delaySeconds;
            if (!int.TryParse(MinimizeDelayTextBox.Text, out delaySeconds) || delaySeconds < 0)
                delaySeconds = 5; // fallback

            _item.MinimizeInitialDelayMs = delaySeconds * 1000;

            DialogResult = true;
            Close();
        }
    }
}

