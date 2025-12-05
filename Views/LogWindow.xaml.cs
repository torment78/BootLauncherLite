using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BootLauncherLite.Views
{
    public partial class LogWindow : Window
    {
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

        public LogWindow(SettingsService settingsService, LogService logService)
        {
            InitializeComponent();

            _settingsService = settingsService;

            // Bind to shared log service
            DataContext = logService;

            // Load and apply current theme
            _currentSettings = _settingsService.Load();
            ApplyTheme(_currentSettings);
        }

        // Ensure HWND exists before we touch the caption
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                ApplyDarkTitleBarFromTheme(_currentSettings);
            }
            catch
            {
                // Ignore on older Windows / no DWM
            }
        }

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

            // 🔒 Ignore settings.ThemeTitleBar / ThemeTitleBarText in Lite.
            // Use the same fixed title colors as your main window default theme.
            var cap = (Color)ColorConverter.ConvertFromString("#111827"); // titleBg
            var txt = (Color)ColorConverter.ConvertFromString("#FFFFFF"); // titleText

            // COLORREF = 0x00BBGGRR
            int captionColor = (cap.B << 16) | (cap.G << 8) | cap.R;
            int textColor = (txt.B << 16) | (txt.G << 8) | txt.R;

            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }

        /// <summary>
        /// Apply the same color settings as the main window / audio window.
        /// Uses ThemeOuterBackground, ThemeCardBackground, ThemeTextColor,
        /// ThemeGridLines.
        /// </summary>
        public void ApplyTheme(AppSettings settings)
        {
            // 🔒 Ignore all Theme* fields in settings for Lite.
            // Use the fixed default theme values from DefaultThemeButton_Click.

            // Base colors
            var outer = (Color)ColorConverter.ConvertFromString("#0B0E13");
            var card = (Color)ColorConverter.ConvertFromString("#111827");
            var text = (Color)ColorConverter.ConvertFromString("#E5E7EB");
            var gridLines = (Color)ColorConverter.ConvertFromString("#374151");

            // Buttons
            var btnBg = (Color)ColorConverter.ConvertFromString("#1F2937");
            var btnFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");
            var btnBorder = (Color)ColorConverter.ConvertFromString("#374151");
            var btnHoverBg = (Color)ColorConverter.ConvertFromString("#2B5EBF");
            var btnHoverBrd = (Color)ColorConverter.ConvertFromString("#60A5FA");
            var btnPressedBg = (Color)ColorConverter.ConvertFromString("#1E40AF");

            // Window background + text (card style)
            Background = new SolidColorBrush(card);
            Foreground = new SolidColorBrush(text);

            // ListBox styling
            if (LogListBox != null)
            {
                LogListBox.Background = new SolidColorBrush(outer);
                LogListBox.BorderBrush = new SolidColorBrush(gridLines);
                LogListBox.Foreground = new SolidColorBrush(text);
            }

            // GroupBox foreground (used by ThemeLabel etc.)
            Resources["GroupBoxForegroundBrush"] = new SolidColorBrush(text);

            // 🔹 Button brushes so the Button Style in XAML picks up the right look
            Resources["ButtonBackgroundBrush"] = new SolidColorBrush(btnBg);
            Resources["ButtonBorderBrush"] = new SolidColorBrush(btnBorder);
            Resources["ButtonForegroundBrush"] = new SolidColorBrush(btnFg);
            Resources["ButtonHoverBackgroundBrush"] = new SolidColorBrush(btnHoverBg);
            Resources["ButtonHoverBorderBrush"] = new SolidColorBrush(btnHoverBrd);
            Resources["ButtonPressedBackgroundBrush"] = new SolidColorBrush(btnPressedBg);
        }



        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

