// 👇 Add these alias lines (you already have them)
using AudioDevice = BootLauncherLite.Audio.AudioDevice;
using AudioDeviceFlow = BootLauncherLite.Audio.AudioDeviceFlow;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;

namespace BootLauncherLite.Views
{
    public partial class AudioSettingsWindow : Window
    {
        private readonly AudioService _audioService;
        private readonly SettingsService _settingsService;

        private List<AudioDevice> _devices = new();

        // ---- DWM interop for dark title bar / caption colors ----
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

        // store current theme caption colors so we can apply them once hwnd exists
        private MediaColor _captionColor;
        private MediaColor _captionTextColor;

        public AudioSettingsWindow(SettingsService settingsService, BootLauncherLite.Audio.AudioService audioService)
        {
            InitializeComponent();

            _settingsService = settingsService;
            _audioService = audioService;

            var settings = _settingsService.Load();
            ApplyTheme(settings);

            // make sure we apply the DWM colors once the HWND is created
            this.SourceInitialized += AudioSettingsWindow_SourceInitialized;

            LoadDevicesAndSettings();
        }

        private void AudioSettingsWindow_SourceInitialized(object? sender, EventArgs e)
        {
            ApplyDwmTitleBar();
        }

        private static MediaColor ParseColor(string? hex, string fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                    hex = fallback;

                return (MediaColor)MediaColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (MediaColor)MediaColorConverter.ConvertFromString(fallback);
            }
        }


        // ----------------------------------------------------
        //  Apply theme colors from AppSettings to this window
        // ----------------------------------------------------
        private void ApplyTheme(AppSettings settings)
        {
            // ===== Base colors from settings (same pattern as LaunchItemAttributesWindow) =====
            var outer = ParseColor(settings.ThemeOuterBackground, "#0B0E13");
            var card = ParseColor(settings.ThemeCardBackground, "#111827");
            var text = ParseColor(settings.ThemeTextColor, "#E5E7EB");

            var controlBg = ParseColor(settings.ThemeControlBackground, "#1F2937");
            var controlFg = ParseColor(settings.ThemeControlForeground, "#FFFFFF");

            var gridRow = ParseColor(settings.ThemeGridRow, "#1F2937");
            var gridAlt = ParseColor(settings.ThemeGridAltRow, "#111827");
            var gridLines = ParseColor(settings.ThemeGridLines, "#374151");

            // DataGrid (kept consistent even if this window doesn’t show a grid)
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

            // Title bar colors (same idea as LaunchItemAttributesWindow, but using your stored fields)
            _captionColor = ParseColor(settings.ThemeTitleBar, "#111827");
            _captionTextColor = ParseColor(settings.ThemeTitleBarText, "#FFFFFF");

            // ===== Apply to window =====
            Background = new SolidColorBrush(card);
            Foreground = new SolidColorBrush(text);

            // ===== Push brushes into this window's resources =====

            // Controls base
            Resources["ControlBackgroundBrush"] = new SolidColorBrush(controlBg);
            Resources["ControlForegroundBrush"] = new SolidColorBrush(controlFg);

            // GroupBox
            Resources["GroupBoxBackgroundBrush"] = new SolidColorBrush(groupBg);
            Resources["GroupBoxBorderBrush"] = new SolidColorBrush(groupBr);
            
            Resources["GroupBoxForegroundBrush"] = new SolidColorBrush(groupFg);
            // Buttons (these are what your XAML button styles are using)
            Resources["ButtonBackgroundBrush"] = new SolidColorBrush(btnBg);
            Resources["ButtonBorderBrush"] = new SolidColorBrush(btnBorder);
            Resources["ButtonForegroundBrush"] = new SolidColorBrush(btnFg);
            Resources["ButtonTextBrush"] = new SolidColorBrush(btnFg);   // if any template still uses it
            Resources["ButtonHoverBackgroundBrush"] = new SolidColorBrush(btnHoverBg);
            Resources["ButtonHoverBorderBrush"] = new SolidColorBrush(btnHoverBrd);
            Resources["ButtonPressedBackgroundBrush"] = new SolidColorBrush(btnPressedBg);

            // DataGrid-like brushes (handy if this window ever shows a grid, and some styles already expect them)
            Resources["DataGridBackgroundBrush"] = new SolidColorBrush(dataGridBg);
            Resources["DataGridRowBrush"] = new SolidColorBrush(gridRow);
            Resources["DataGridAltRowBrush"] = new SolidColorBrush(gridAlt);
            Resources["DataGridGridLinesBrush"] = new SolidColorBrush(gridLines);
            Resources["DataGridForegroundBrush"] = new SolidColorBrush(dataGridFg);
            Resources["DataGridHoverBrush"] = new SolidColorBrush(dataGridHover);
            Resources["DataGridSelectedBrush"] = new SolidColorBrush(dataGridSelected);
            Resources["DataGridHeaderBackgroundBrush"] = new SolidColorBrush(headerBg);
            Resources["DataGridHeaderForegroundBrush"] = new SolidColorBrush(headerFg);

            // ComboBox brushes (the ThemedComboBox style uses these)
            Resources["ComboBgBrush"] = new SolidColorBrush(comboBg);
            Resources["ComboFgBrush"] = new SolidColorBrush(comboFg);
            Resources["ComboHoverBrush"] = new SolidColorBrush(comboHv);

            // If hwnd already exists (window reopened / theme reapplied), push DWM again
            if (IsLoaded)
                ApplyDwmTitleBar();
        }



        // ----------------------------------------------------
        //  DWM title bar helpers
        // ----------------------------------------------------
        private void ApplyDwmTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                // enable dark mode on title bar
                int useDark = 1;
                // try newer attribute id first
                _ = DwmSetWindowAttribute(hwnd,
                    DWMWA_USE_IMMERSIVE_DARK_MODE_20,
                    ref useDark,
                    sizeof(int));

                // also try the older one just in case
                _ = DwmSetWindowAttribute(hwnd,
                    DWMWA_USE_IMMERSIVE_DARK_MODE_19,
                    ref useDark,
                    sizeof(int));

                // caption (title bar background) color
                int captionColor = ToColorRef(_captionColor);
                _ = DwmSetWindowAttribute(hwnd,
                    DWMWA_CAPTION_COLOR,
                    ref captionColor,
                    sizeof(int));

                // caption text color
                int captionTextColor = ToColorRef(_captionTextColor);
                _ = DwmSetWindowAttribute(hwnd,
                    DWMWA_TEXT_COLOR,
                    ref captionTextColor,
                    sizeof(int));
            }
            catch
            {
                // fail silently, worst case you just get default title bar
            }
        }

        // COLORREF = 0x00BBGGRR (alpha ignored)
        private static int ToColorRef(MediaColor c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        // ----------------------------------------------------
        //  Device enumeration + preselect based on settings
        // ----------------------------------------------------
        private void LoadDevicesAndSettings()
        {
            try
            {
                _devices = _audioService.GetDevices().ToList();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this,
                    $"Failed to enumerate audio devices:\n{ex.Message}",
                    "Audio error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                _devices = new List<AudioDevice>();
            }

            var playbackDevices = _devices.Where(d => d.Flow == AudioDeviceFlow.Render).ToList();
            var captureDevices = _devices.Where(d => d.Flow == AudioDeviceFlow.Capture).ToList();

            PlaybackComboBox.ItemsSource = playbackDevices;
            CaptureComboBox.ItemsSource = captureDevices;

            if (playbackDevices.Count == 0 && captureDevices.Count == 0)
            {
                InfoTextBlock.Text =
                    "No audio devices were found via the built-in CoreAudio API.\n" +
                    "This can happen if the COM interface layout doesn’t match this Windows version.\n\n" +
                    "You can still configure your default devices using Windows sound settings below.";
                InfoTextBlock.Visibility = Visibility.Visible;
                OpenSystemSoundButton.Visibility = Visibility.Visible;
                return;
            }

            InfoTextBlock.Visibility = Visibility.Collapsed;
            OpenSystemSoundButton.Visibility = Visibility.Collapsed;

            var settings = _settingsService.Load();

            // Preselect playback
            if (!string.IsNullOrWhiteSpace(settings.SelectedPlaybackDeviceId) &&
                playbackDevices.Any(d => d.Id == settings.SelectedPlaybackDeviceId))
            {
                PlaybackComboBox.SelectedValue = settings.SelectedPlaybackDeviceId;
            }
            else
            {
                var def = playbackDevices.FirstOrDefault(d => d.IsDefault);
                if (def != null) PlaybackComboBox.SelectedItem = def;
            }

            // Preselect capture
            if (!string.IsNullOrWhiteSpace(settings.SelectedCaptureDeviceId) &&
                captureDevices.Any(d => d.Id == settings.SelectedCaptureDeviceId))
            {
                CaptureComboBox.SelectedValue = settings.SelectedCaptureDeviceId;
            }
            else
            {
                var def = captureDevices.FirstOrDefault(d => d.IsDefault);
                if (def != null) CaptureComboBox.SelectedItem = def;
            }
        }

        // ----------------------------------------------------
        //  Buttons: set default playback / capture
        // ----------------------------------------------------
        private void SetPlaybackDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaybackComboBox.SelectedItem is not AudioDevice dev)
                return;

            try
            {
                _audioService.SetDefaultPlayback(dev.Id);

                var settings = _settingsService.Load();
                settings.SelectedPlaybackDeviceId = dev.Id;
                _settingsService.Save(settings);

                MessageBox.Show(this,
                    $"Set playback device to:\n{dev.Name}",
                    "Playback default changed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Failed to set playback device:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SetCaptureDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            if (CaptureComboBox.SelectedItem is not AudioDevice dev)
                return;

            try
            {
                _audioService.SetDefaultCapture(dev.Id);

                var settings = _settingsService.Load();
                settings.SelectedCaptureDeviceId = dev.Id;
                _settingsService.Save(settings);

                MessageBox.Show(this,
                    $"Set recording device to:\n{dev.Name}",
                    "Recording default changed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Failed to set recording device:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ----------------------------------------------------
        //  Buttons: open Windows sound settings / close
        // ----------------------------------------------------
        private void OpenSystemSoundButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:sound",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "control.exe",
                        Arguments = "mmsys.cpl",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(this,
                        $"Could not open Windows sound settings:\n{ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}


