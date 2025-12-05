using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BootLauncherLite
{
    public partial class DiscoveredNodeDetailsWindow : Window
    {
        private readonly DiscoveredNode _node;

        // --- DWM constants for caption colors ---
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        public DiscoveredNodeDetailsWindow(DiscoveredNode node)
        {
            InitializeComponent();
            _node = node ?? throw new ArgumentNullException(nameof(node));

            // XAML now owns all brushes/styles (locked Lite theme)
            DataContext = node;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyTitleBarColorsFromSettings();
        }

        private void ApplyTitleBarColorsFromSettings()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                // Load current theme from settings
                var settingsService = new SettingsService();
                var settings = settingsService.Load();

                string bgHex = string.IsNullOrWhiteSpace(settings.ThemeTitleBar)
                    ? "#111827"
                    : settings.ThemeTitleBar;

                string txtHex = string.IsNullOrWhiteSpace(settings.ThemeTitleBarText)
                    ? "#FFFFFF"
                    : settings.ThemeTitleBarText;

                var bgColor = (System.Windows.Media.Color)
                    System.Windows.Media.ColorConverter.ConvertFromString(bgHex);

                var txtColor = (System.Windows.Media.Color)
                    System.Windows.Media.ColorConverter.ConvertFromString(txtHex);

                // COLORREF = 0x00BBGGRR
                int bgRef = bgColor.R | (bgColor.G << 8) | (bgColor.B << 16);
                int txtRef = txtColor.R | (txtColor.G << 8) | (txtColor.B << 16);

                DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_CAPTION_COLOR,
                    ref bgRef,
                    Marshal.SizeOf<int>());

                DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_TEXT_COLOR,
                    ref txtRef,
                    Marshal.SizeOf<int>());
            }
            catch
            {
                // ignore on older Windows or if DWM not available
            }
        }

        private void UseSelectedIpButton_Click(object sender, RoutedEventArgs e)
        {
            if (IpsListBox.SelectedItem is string selectedIp &&
                !string.IsNullOrWhiteSpace(selectedIp))
            {
                _node.IpAddress = selectedIp;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(this,
                    "Please select an IP address first.",
                    "No selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
