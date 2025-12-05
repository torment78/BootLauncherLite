using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BootLauncherLite.Models;
using BootLauncherLite.Services;

namespace BootLauncherLite.Views
{
    public partial class ProcessPickerWindow : Window
    {
        private readonly ProcessPickerService _service = new ProcessPickerService();
        private readonly SettingsService _settingsService;
        private readonly AppSettings _currentSettings;

        public ObservableCollection<ProcessEntry> Processes { get; } = new();

        public string? SelectedPath { get; private set; }

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

        public ProcessPickerWindow()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            _currentSettings = _settingsService.Load();

            // Apply fixed Lite theme before loading data
            ApplyTheme(_currentSettings);

            DataContext = this;
            Loaded += ProcessPickerWindow_Loaded;
        }

        // Ensure the HWND exists before we touch the caption colors
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                ApplyDarkTitleBarFromTheme(_currentSettings);
            }
            catch
            {
                // ignore on older Windows / no DWM
            }
        }

        // =========================================================
        //  THEME (LITE: fixed colors, ignore Theme* from settings)
        // =========================================================

        private void ApplyDarkTitleBarFromTheme(AppSettings settings)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref useDark, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref useDark, sizeof(int));

            // 🔒 For Lite: ignore settings.ThemeTitleBar / ThemeTitleBarText, use fixed defaults
            var cap = (Color)ColorConverter.ConvertFromString("#111827"); // titleBg
            var txt = (Color)ColorConverter.ConvertFromString("#FFFFFF"); // titleText

            // COLORREF = 0x00BBGGRR
            int captionColor = (cap.B << 16) | (cap.G << 8) | cap.R;
            int textColor = (txt.B << 16) | (txt.G << 8) | txt.R;

            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }

        public void ApplyTheme(AppSettings settings)
        {
            // 🔒 LITE: ignore Theme* in settings; hard-code BootLauncherLite default theme.

            // Base
            var outer = (Color)ColorConverter.ConvertFromString("#0B0E13");
            var card = (Color)ColorConverter.ConvertFromString("#111827");
            var text = (Color)ColorConverter.ConvertFromString("#E5E7EB");

            var gridRow = (Color)ColorConverter.ConvertFromString("#1F2937");
            var gridAlt = (Color)ColorConverter.ConvertFromString("#111827");
            var gridLines = (Color)ColorConverter.ConvertFromString("#374151");

            var dataGridBg = (Color)ColorConverter.ConvertFromString("#111827");
            var dataGridHover = (Color)ColorConverter.ConvertFromString("#2B5EBF");
            var dataGridSelected = (Color)ColorConverter.ConvertFromString("#2B3440");
            var dataGridFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");
            var headerBg = (Color)ColorConverter.ConvertFromString("#111827");
            var headerFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");

            // Buttons
            var btnBg = (Color)ColorConverter.ConvertFromString("#1F2937");
            var btnFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");
            var btnBorder = (Color)ColorConverter.ConvertFromString("#374151");
            var btnHoverBg = (Color)ColorConverter.ConvertFromString("#2B5EBF");
            var btnHoverBrd = (Color)ColorConverter.ConvertFromString("#60A5FA");
            var btnPressedBg = (Color)ColorConverter.ConvertFromString("#1E40AF");

            // GroupBox
            var groupBg = (Color)ColorConverter.ConvertFromString("#1F2937");
            var groupBr = (Color)ColorConverter.ConvertFromString("#374151");
            var groupFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");

            // ComboBox (even if we don't currently use one, keep for future consistency)
            var comboBg = (Color)ColorConverter.ConvertFromString("#1F2937");
            var comboFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");
            var comboHv = (Color)ColorConverter.ConvertFromString("#374151");

            // Window background + text
            Background = new SolidColorBrush(card);
            Foreground = new SolidColorBrush(text);

            // Push brushes into this window's resources (so XAML DynamicResource picks them up)

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

            // DataGrid / row colors
            Resources["DataGridBackgroundBrush"] = new SolidColorBrush(dataGridBg);
            Resources["DataGridRowBrush"] = new SolidColorBrush(gridRow);
            Resources["DataGridAltRowBrush"] = new SolidColorBrush(gridAlt);
            Resources["DataGridGridLinesBrush"] = new SolidColorBrush(gridLines);
            Resources["DataGridForegroundBrush"] = new SolidColorBrush(dataGridFg);
            Resources["DataGridHoverBrush"] = new SolidColorBrush(dataGridHover);
            Resources["DataGridSelectedBrush"] = new SolidColorBrush(dataGridSelected);
            Resources["DataGridHeaderBackgroundBrush"] = new SolidColorBrush(headerBg);
            Resources["DataGridHeaderForegroundBrush"] = new SolidColorBrush(headerFg);

            // ComboBox global style for this window (if ever added)
            var comboBgBrush = new SolidColorBrush(comboBg);
            var comboFgBrush = new SolidColorBrush(comboFg);

            var comboStyle = new Style(typeof(System.Windows.Controls.ComboBox));
            comboStyle.Setters.Add(
                new Setter(System.Windows.Controls.Control.BackgroundProperty, comboBgBrush));
            comboStyle.Setters.Add(
                new Setter(System.Windows.Controls.Control.ForegroundProperty, comboFgBrush));
            Resources[typeof(System.Windows.Controls.ComboBox)] = comboStyle;

            // Explicit DataGrid styling for ProcessesGrid
            if (ProcessesGrid != null)
            {
                ProcessesGrid.Background = new SolidColorBrush(dataGridBg);
                ProcessesGrid.RowBackground = new SolidColorBrush(gridRow);
                ProcessesGrid.AlternatingRowBackground = new SolidColorBrush(gridAlt);
                ProcessesGrid.HorizontalGridLinesBrush = new SolidColorBrush(gridLines);
                ProcessesGrid.VerticalGridLinesBrush = new SolidColorBrush(gridLines);
                ProcessesGrid.Foreground = new SolidColorBrush(dataGridFg);
            }
        }

        // =========================================================
        //  EVENTS
        // =========================================================

        private void ProcessPickerWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            Processes.Clear();

            foreach (var (name, path) in _service.GetProcesses())
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                Processes.Add(new ProcessEntry
                {
                    ProcessName = name,
                    FilePath = path
                });
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessesGrid.SelectedItem is ProcessEntry entry)
            {
                SelectedPath = entry.FilePath;
                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public class ProcessEntry
    {
        public string ProcessName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }
}
