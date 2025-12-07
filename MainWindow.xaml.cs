
using MediaBrush = System.Windows.Media.Brush;
using System.Diagnostics;
using System.Security.Principal;
using System.ComponentModel;
using System.Threading.Tasks;

using MediaColor = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfControl = System.Windows.Controls.Control;
using WpfDataGrid = System.Windows.Controls.DataGrid;

namespace BootLauncherLite
{
    public partial class MainWindow : Window
    {
        // === Public collections for binding ===
        public ObservableCollection<LaunchItem> LaunchItems { get; } = new();
        public ObservableCollection<RemoteMachine> RemoteMachines { get; } = new();
        public ObservableCollection<DiscoveredNode> DiscoveredMachines { get; } = new();

        // === Services ===
        private readonly FileDialogService _fileDialogService;
        private readonly LaunchSequenceService _launchSequenceService;
        private readonly SettingsService _settingsService;
        private readonly LogService _logService;
        private readonly AudioService _audioService;
        private readonly DiscoveryService _discoveryService;
        private readonly WolService _wolService;
        private readonly TrayIconManager _trayIconManager;

        // Windows messages for right-click on title bar
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const int HTCAPTION = 2;

        // Color settings window guard
        private bool _colorWindowOpen = false;

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

        // === Windows ===
        private ToastWindow? _toastWindow;
        private LogWindow? _logWindow;

        // === State === 
        private bool _skipDelay;          // just skip the remaining delay
        private bool _skipNextApp;  // skip the *next* app but keep its delay
        private bool _stopSequence;
        private int _currentSequenceIndex = -1;
        private bool _isMasterMode = true;
        private bool _isAutorunStartup;
        private bool _uiInitialized = false;
        private bool _cancelSequence;
        private bool _forceShutdownRequested;
        private bool _startHiddenFromCmd;
        private bool _autoLaunchTriggered = false;
        // NEW: selected startup mode for this app (HKCU Run vs Scheduler)
        private StartupMode _startupMode = StartupMode.RegistryRun;
       

        public MainWindow()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            _fileDialogService = new FileDialogService();
            _launchSequenceService = new LaunchSequenceService();
            _logService = new LogService();
            _audioService = new AudioService();
            _wolService = new WolService();

            // Load settings once here so we can pull startup mode early
            var initialSettings = _settingsService.Load();
            _startupMode = initialSettings.StartupMode;

            //if (_startupMode == StartupMode.TaskSchedulerElevated && !IsRunningElevated())
            //{
            //    var result = MessageBox.Show(
            //        this,
            //        "BootLauncherLite is configured to use the elevated Task Scheduler startup mode,\n" +
            //        "but it is currently not running with administrator rights.\n\n" +
            //"Restart BootLauncherLite now as administrator?",
            //"Elevation recommended",
            // MessageBoxButton.YesNo,
            //MessageBoxImage.Warning);

            //if (result == MessageBoxResult.Yes)
            //{
            //RestartElevated();
            // return;
            //}
            //


            ApplySavedAudioDefaults();
            LoadSettings(); // fills LaunchItems, RemoteMachines, IsMasterMode, etc.

            // Create tray icon
            _trayIconManager = new TrayIconManager(ToggleMainWindowFromTray);


            // --- DISCOVERY SERVICE ---
            _discoveryService = new DiscoveryService(() => _isMasterMode);
            _discoveryService.NodeDiscovered += DiscoveryService_NodeDiscovered;
            _discoveryService.Start();

            // detect if launched with --autorun
            // detect if launched with --autorun
            var args = Environment.GetCommandLineArgs();

            _logService.Log("Startup args: " + string.Join(" ", args));

            // Only **--autorun** means: I was started by Windows (boot/autostart)
            // Anything that should trigger the *sequence* (REAL autorun only)
            bool hasAutorun =
                args.Any(a => string.Equals(a, "--autorun", StringComparison.OrdinalIgnoreCase)) ||
                args.Any(a => string.Equals(a, "--autorun-tray", StringComparison.OrdinalIgnoreCase));

            // Anything that should start hidden to tray:
            bool wantsTray =
                args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)) ||
                args.Any(a => string.Equals(a, "--hidden", StringComparison.OrdinalIgnoreCase)) ||
                args.Any(a => string.Equals(a, "--autorun-tray", StringComparison.OrdinalIgnoreCase));

            _isAutorunStartup = hasAutorun;
            _startHiddenFromCmd = wantsTray;

            _logService.Log($"Flags: hasAutorun={hasAutorun}, wantsTray={wantsTray}");



            // If we got a tray/hidden flag, hide after WPF has fully loaded
            if (_startHiddenFromCmd)
            {
                Loaded += (_, __) => HideToTrayOnStartup();
            }

            // Main startup check is now based on selected startup mode
            RunAtStartupCheckBox.IsChecked = StartupRegistrationService.IsRegistered(_startupMode);

            DataContext = this;
            LoadSettingsIntoUi();
            _uiInitialized = true;   // 👈 NEW – now we can auto-save safely
            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
        }
        private void StartupModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || StartupModeComboBox == null)
                return;

            var settings = _settingsService.Load();

            if (StartupModeComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<StartupMode>(tag, out var mode))
            {
                settings.StartupMode = mode;
                _startupMode = mode;
                _settingsService.Save(settings);
                _logService.Log($"Startup mode set to: {mode}");

                // If the user has "Run at startup" enabled, update the actual registration
                try
                {
                    if (RunAtStartupCheckBox.IsChecked == true)
                    {
                        // First remove any old registration so we don't end up with duplicates
                        StartupRegistrationService.UnregisterAll();
                        StartupRegistrationService.Register(mode);
                        _logService.Log($"Updated startup registration for mode: {mode}");
                    }
                }
                catch (Exception ex)
                {
                    _logService.Log($"Failed to update startup registration for new mode: {ex.Message}");
                    MessageBox.Show(this,
                        $"Failed to update startup registration for the selected mode:\n{ex.Message}",
                        "Startup mode",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                // If user picked the elevated Task Scheduler mode, ask to restart as admin
                if (mode == StartupMode.TaskSchedulerElevated && !IsRunningElevated())
                {
                    var result = MessageBox.Show(
                        this,
                        "The elevated Task Scheduler startup mode works best when " +
                        "BootLauncherLite is running as administrator.\n\n" +
                        "Restart BootLauncherLite now with admin rights?",
                        "Restart as administrator",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        RestartElevated();
                        // IMPORTANT: return so we don't keep executing in this instance
                        return;
                    }
                }
            }
        }

        private void ToggleMainWindowFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // CASE 1: Window is hidden / minimized → restore it
                    if (!IsVisible || WindowState == WindowState.Minimized || !ShowInTaskbar)
                    {
                        ShowInTaskbar = true;
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    }
                    // CASE 2: Window is visible → hide it to tray
                    else
                    {
                        WindowState = WindowState.Minimized;
                        ShowInTaskbar = false;
                        Hide();    // your StateChanged handler will also hide on minimize
                    }
                }
                catch
                {
                    // no hard crash if something weird happens
                }
            });
        }




        // ============================================================
        //  Autorun delay UI binding
        // ============================================================

        private void LoadSettingsIntoUi()
        {
            var settings = _settingsService.Load();

            _startupMode = settings.StartupMode;

            AutoCloseCheckBox.IsChecked = settings.AutoCloseWhenDone;

            // bind startup mode combo if present in XAML
            if (StartupModeComboBox != null)
            {
                string modeTag = settings.StartupMode.ToString();

                foreach (ComboBoxItem item in StartupModeComboBox.Items)
                {
                    if (item.Tag as string == modeTag)
                    {
                        StartupModeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            //  now bind the checkbox based on that mode
            RunAtStartupCheckBox.IsChecked =
                StartupRegistrationService.IsRegistered(settings.StartupMode);

            int delay = settings.AutorunDelaySeconds;
            if (delay < 0) delay = 0;
            if (delay > 120) delay = 120;

            AutorunDelaySlider.Value = delay;
            AutorunDelayValueText.Text = $"{delay} s";
        }
        private void HideToTrayOnStartup()
        {
            try
            {
                // Make sure tray icon exists (you already create it in ctor)
                // Then hide window and remove it from taskbar.
                WindowState = WindowState.Minimized;
                ShowInTaskbar = false;
                Hide();
                // If TrayIconManager has some "Show" or "Ensure" method, you can call it here.
                // For now we rely on its constructor having set it up.
                _logService.Log("Started hidden to tray from command-line flag.");
            }
            catch (Exception ex)
            {
                _logService.Log($"Failed to hide to tray on startup: {ex.Message}");
            }
        }

        private void ShowFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    Show();
                    WindowState = WindowState.Normal;
                    ShowInTaskbar = true;
                    Activate();
                }
                catch
                {
                    // ignore
                }
            });
        }

        private void AutorunDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AutorunDelayValueText == null)
                return; // can fire during InitializeComponent

            int seconds = (int)Math.Round(e.NewValue);
            AutorunDelayValueText.Text = $"{seconds} s";
            // If you want live-saving, uncomment:
            // SaveSettingsFromUi();
        }

        private void RunAtStartupCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = _settingsService.Load();

                if (RunAtStartupCheckBox.IsChecked == true)
                {
                    // If user selected Task Scheduler elevated mode,
                    // we must be running as admin to create the task.
                    if (_startupMode == StartupMode.TaskSchedulerElevated && !IsRunningElevated())
                    {
                        // Immediately revert the checkbox so UI stays honest
                        RunAtStartupCheckBox.IsChecked = false;

                        var result = MessageBox.Show(
                            this,
                            "To create an elevated Task Scheduler startup entry, " +
                            "BootLauncherLite itself must be running as administrator.\n\n" +
                            "Restart BootLauncherLite now as admin and then enable startup again?",
                            "Administrator required",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            RestartElevated();
                        }

                        return; // Do not call Register() in this non-admin instance
                    }

                    // Normal path: either RegistryRun, or we're already admin for TaskSchedulerElevated
                    StartupRegistrationService.Register(_startupMode);
                }
                else
                {
                    // Remove both registry + task for safety
                    StartupRegistrationService.UnregisterAll();
                }

                // After changing, reflect the real status again
                RunAtStartupCheckBox.IsChecked = StartupRegistrationService.IsRegistered(_startupMode);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Failed to update startup registration:\n{ex.Message}",
                    "Startup registration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // Revert checkbox to whatever Windows actually has
                RunAtStartupCheckBox.IsChecked = StartupRegistrationService.IsRegistered(_startupMode);
            }
        }



        private void SaveSettingsFromUi()
        {
            var settings = _settingsService.Load();

            settings.LaunchItems = LaunchItems
                .OrderBy(li => li.Order)
                .ToList();

            settings.RemoteMachines = RemoteMachines.ToList();
            settings.AutoCloseWhenDone = AutoCloseCheckBox.IsChecked == true;
            settings.IsMasterMode = _isMasterMode;
            settings.AutorunDelaySeconds = (int)Math.Round(AutorunDelaySlider.Value);

            // NEW: persist startup mode chosen in UI
            settings.StartupMode = _startupMode;

            _settingsService.Save(settings);
        }
        private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (!_uiInitialized)
                return;

            if (e.EditAction != DataGridEditAction.Commit)
                return;

            // Wait until the edit is committed so bindings are updated
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    SaveSettingsFromUi();
                    _logService.Log("Settings saved (CellEditEnding).");
                }
                catch
                {
                    // ignore – no crash if JSON save fails
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Catches immediate checkbox toggles that don't always trigger CellEditEnding
        private void Grid_CurrentCellChanged(object? sender, EventArgs e)
        {
            if (!_uiInitialized)
                return;

            try
            {
                SaveSettingsFromUi();
                _logService.Log("Settings saved (CurrentCellChanged).");
            }
            catch
            {
                // ignore
            }
        }
        // ============================================================
        //  Audio defaults on startup
        // ============================================================

        private void ApplySavedAudioDefaults()
        {
            var settings = _settingsService.Load();

            try
            {
                if (!string.IsNullOrWhiteSpace(settings.SelectedPlaybackDeviceId))
                {
                    _audioService.SetDefaultPlayback(settings.SelectedPlaybackDeviceId);
                }

                if (!string.IsNullOrWhiteSpace(settings.SelectedCaptureDeviceId))
                {
                    _audioService.SetDefaultCapture(settings.SelectedCaptureDeviceId);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                MessageBox.Show(
                    $"Failed to apply audio defaults:\n{ex.Message}",
                    "Audio defaults",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
#endif
            }
        }

        private void ApplyAudioDefaultsWithToast(ToastWindow toast)
        {
            var settings = _settingsService.Load();

            string playbackName = "unchanged";
            string captureName = "unchanged";

            try
            {
                var devices = _audioService.GetDevices().ToList();

                if (!string.IsNullOrWhiteSpace(settings.SelectedPlaybackDeviceId))
                {
                    _audioService.SetDefaultPlayback(settings.SelectedPlaybackDeviceId);

                    var dev = devices.FirstOrDefault(d => d.Id == settings.SelectedPlaybackDeviceId);
                    playbackName = dev?.Name ?? settings.SelectedPlaybackDeviceId;
                }

                if (!string.IsNullOrWhiteSpace(settings.SelectedCaptureDeviceId))
                {
                    _audioService.SetDefaultCapture(settings.SelectedCaptureDeviceId);

                    var dev = devices.FirstOrDefault(d => d.Id == settings.SelectedCaptureDeviceId);
                    captureName = dev?.Name ?? settings.SelectedCaptureDeviceId;
                }

                // final status after doing the work
                toast.SetAudioStatus($"Playback: {playbackName}, Mic: {captureName}");
                _logService.Log($"Applied audio defaults – Playback: {playbackName}, Mic: {captureName}");
            }
            catch (Exception ex)
            {
                _logService.Log($"Failed to apply audio defaults (toast): {ex.Message}");
                toast.SetAudioStatus("Error applying audio defaults.");
            }
        }

        // ============================================================
        //  Title bar right-click → color settings
        // ============================================================

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // In Lite: we no longer open the color settings on title-bar right click.
            // Keep the hook harmless and do nothing special.
            if (msg == WM_NCRBUTTONDOWN && wParam.ToInt32() == HTCAPTION)
            {
                // Do NOT open ColorSettingsWindow anymore.
                handled = false;  // let Windows handle the title bar normally
                return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }


        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var source = (HwndSource)PresentationSource.FromVisual(this);
            if (source != null)
            {
               
            }

            var settings = _settingsService.Load();
            ApplyTheme(settings);
        }

        // ============================================================
        //  Theme + dark title bar
        // ============================================================

        private void ApplyDarkTitleBarFromTheme(AppSettings settings)
        {
            // In Lite, ignore any theme fields in settings and just use the fixed theme colors.
            var cap = (Color)ColorConverter.ConvertFromString("#111827"); // titleBg
            var txt = (Color)ColorConverter.ConvertFromString("#FFFFFF"); // titleText

            ApplyDarkTitleBarFixed(cap, txt);
        }

        private void ApplyDarkTitleBarFixed(Color cap, Color txt)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref useDark, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref useDark, sizeof(int));

            // If elevated, override text color with red (keep your old behavior)
            if (IsElevatedInstance())
            {
                txt = MediaColor.FromRgb(255, 0, 0);
            }

            int captionColor = (cap.B << 16) | (cap.G << 8) | cap.R;
            int textColor = (txt.B << 16) | (txt.G << 8) | txt.R;

            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }


        private static Color ParseColor(string hex, string fallback)
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

        public void ApplyTheme(AppSettings settings)
        {
            // ===== Single hard-coded theme (from DefaultThemeButton_Click) =====

            // Base colors
            var outer = (Color)ColorConverter.ConvertFromString("#0B0E13");
            var card = (Color)ColorConverter.ConvertFromString("#111827");
            var text = (Color)ColorConverter.ConvertFromString("#E5E7EB");
            var cardHeaderText = (Color)ColorConverter.ConvertFromString("#E5E7EB");
            var controlBg = (Color)ColorConverter.ConvertFromString("#1F2937");
            var controlFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");

            // Grid + launch area
            var headerBg = (Color)ColorConverter.ConvertFromString("#111827");
            var headerFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");
            var gridRow = (Color)ColorConverter.ConvertFromString("#1F2937");
            var gridAlt = (Color)ColorConverter.ConvertFromString("#111827");
            var gridLines = (Color)ColorConverter.ConvertFromString("#374151");
            var launchGridArea = (Color)ColorConverter.ConvertFromString("#050816");

            // Title bar
            var titleBg = (Color)ColorConverter.ConvertFromString("#111827");
            var titleText = (Color)ColorConverter.ConvertFromString("#FFFFFF");

            // Buttons
            var btnBg = (Color)ColorConverter.ConvertFromString("#1F2937");
            var btnFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");
            var btnBorder = (Color)ColorConverter.ConvertFromString("#374151");
            var btnHoverBg = (Color)ColorConverter.ConvertFromString("#2B5EBF");
            var btnHoverBrd = (Color)ColorConverter.ConvertFromString("#60A5FA");
            var btnPressedBg = (Color)ColorConverter.ConvertFromString("#1E40AF");

            // GroupBox
            var groupBg = (Color)ColorConverter.ConvertFromString("#1F2937");
            var groupBrd = (Color)ColorConverter.ConvertFromString("#374151");
            var groupFg = text;

            // ComboBox – use the same scheme as your preset
            var comboBg = (Color)ColorConverter.ConvertFromString("#1F2937");
            var comboFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");
            var comboHvBg = (Color)ColorConverter.ConvertFromString("#374151");

            // DataGrid
            var dataGridBg = (Color)ColorConverter.ConvertFromString("#111827");
            var dataGridHover = (Color)ColorConverter.ConvertFromString("#2B5EBF");
            var dataGridSelected = (Color)ColorConverter.ConvertFromString("#2B3440");
            var dataGridFg = (Color)ColorConverter.ConvertFromString("#FFFFFF");
            var dataGridHeaderBg = headerBg;
            var dataGridHeaderFg = headerFg;

            // ===== Apply to window + root =====

            Resources["OuterBackgroundBrush"] = new SolidColorBrush(outer);
            Resources["CardBackgroundBrush"] = new SolidColorBrush(card);

            this.Background = (Brush)Resources["OuterBackgroundBrush"];
            if (RootBorder != null)
                RootBorder.Background = (Brush)Resources["CardBackgroundBrush"];

            this.Foreground = new SolidColorBrush(text);

            // ===== Push brushes into Window.Resources =====

            // GroupBox
            Resources["GroupBoxBackgroundBrush"] = new SolidColorBrush(groupBg);
            Resources["GroupBoxBorderBrush"] = new SolidColorBrush(groupBrd);
            Resources["GroupBoxForegroundBrush"] = new SolidColorBrush(groupFg);
            Resources["GroupBoxHeaderForegroundBrush"] = new SolidColorBrush(cardHeaderText);

            // Buttons
            Resources["ButtonBackgroundBrush"] = new SolidColorBrush(btnBg);
            Resources["ButtonBorderBrush"] = new SolidColorBrush(btnBorder);
            Resources["ButtonTextBrush"] = new SolidColorBrush(btnFg);
            Resources["ButtonHoverBackgroundBrush"] = new SolidColorBrush(btnHoverBg);
            Resources["ButtonHoverBorderBrush"] = new SolidColorBrush(btnHoverBrd);
            Resources["ButtonPressedBackgroundBrush"] = new SolidColorBrush(btnPressedBg);
            Resources["ButtonForegroundBrush"] = new SolidColorBrush(btnFg);

            Resources["ControlBackgroundBrush"] = new SolidColorBrush(controlBg);
            Resources["ControlForegroundBrush"] = new SolidColorBrush(controlFg);

            // DataGrid brushes
            Resources["DataGridBackgroundBrush"] = new SolidColorBrush(dataGridBg);
            Resources["DataGridRowBrush"] = new SolidColorBrush(gridRow);
            Resources["DataGridAltRowBrush"] = new SolidColorBrush(gridAlt);
            Resources["DataGridGridLinesBrush"] = new SolidColorBrush(gridLines);
            Resources["DataGridForegroundBrush"] = new SolidColorBrush(dataGridFg);
            Resources["DataGridHoverBrush"] = new SolidColorBrush(dataGridHover);
            Resources["DataGridSelectedBrush"] = new SolidColorBrush(dataGridSelected);
            Resources["DataGridHeaderBackgroundBrush"] = new SolidColorBrush(dataGridHeaderBg);
            Resources["DataGridHeaderForegroundBrush"] = new SolidColorBrush(dataGridHeaderFg);

            Resources["LaunchGridAreaBackgroundBrush"] = new SolidColorBrush(launchGridArea);

            // ===== ComboBox brushes =====

            var comboBgBrush = new SolidColorBrush(comboBg);
            var comboFgBrush = new SolidColorBrush(comboFg);
            var comboHoverBrush = new SolidColorBrush(comboHvBg);

            Resources["ComboBgBrush"] = comboBgBrush;
            Resources["ComboFgBrush"] = comboFgBrush;
            Resources["ComboHoverBrush"] = comboHoverBrush;

            // Still push directly to your StartupModeComboBox so it keeps the same style, just new colors
            if (StartupModeComboBox != null)
            {
                StartupModeComboBox.Background = comboBgBrush;
                StartupModeComboBox.Foreground = comboFgBrush;
            }

            // ===== DataGrid “fallback” styling (uses the existing styles/templates) =====

            void StyleGrid(DataGrid grid, bool isLaunchGrid)
            {
                if (grid == null) return;

                if (!isLaunchGrid)
                {
                    grid.Background = (Brush)Resources["DataGridBackgroundBrush"];
                }

                grid.RowBackground = (Brush)Resources["DataGridRowBrush"];
                grid.AlternatingRowBackground = (Brush)Resources["DataGridAltRowBrush"];
                grid.HorizontalGridLinesBrush = (Brush)Resources["DataGridGridLinesBrush"];
                grid.VerticalGridLinesBrush = (Brush)Resources["DataGridGridLinesBrush"];
                grid.Foreground = (Brush)Resources["DataGridForegroundBrush"];
            }

            StyleGrid(LaunchItemsGrid, isLaunchGrid: true);
            StyleGrid(RemoteMachinesGrid, isLaunchGrid: false);
            StyleGrid(DiscoveredMachinesGrid, isLaunchGrid: false);

            // ===== OS title bar colors (use the same fixed scheme) =====
            try
            {
                ApplyDarkTitleBarFixed(titleBg, titleText);
            }
            catch
            {
                // ignore older Windows that don't support it
            }
        }



       

        // ============================================================
        //  Autorun main entry (boot mode)
        // ============================================================

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Keep the title bar behavior
            if (IsElevatedInstance())
            {
                this.Title = "BootLauncherLite - ELEVATED";
            }
            else
            {
                this.Title = "BootLauncherLite";
            }

            // === Autorun logic on reboot / startup ===
            try
            {
                // Only do this if we were started with the --autorun flag
                if (_isAutorunStartup)
                {
                    // Load current settings to get the delay
                    var settings = _settingsService.Load();
                    int delay = settings.AutorunDelaySeconds;

                    if (delay < 0) delay = 0;
                    if (delay > 120) delay = 120;

                    _logService.Log($"Autorun startup detected (--autorun). Delay = {delay} s.");

                    if (delay > 0)
                    {
                        await Task.Delay(delay * 1000);
                    }

                    // Window might be closed or we may have already started once
                    if (_autoLaunchTriggered || !IsLoaded)
                        return;

                    _autoLaunchTriggered = true;

                    // If there are no launch items, don't bother popping a message box on boot
                    if (LaunchItems.Count == 0)
                    {
                        _logService.Log("Autorun requested but no launch items are defined. Skipping.");
                        return;
                    }

                    _logService.Log("Autorun: triggering Run sequence from startup.");

                    // Fake-click Run, but guard against null
                    if (RunButton != null)
                    {
                        RunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    else
                    {
                        _logService.Log("Autorun: RunButton was null, cannot trigger sequence.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.Log($"Autorun startup failed: {ex}");
            }
        }

        public static bool IsElevated()
        {
            return SecurityHelper.IsRunningAsAdmin();
        }

        private bool IsElevatedInstance() => IsElevated();

        private static bool IsRunningElevated() => IsElevated();



        // ============================================================
        //  Audio settings window
        // ============================================================

        private void AudioSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var audioWin = new AudioSettingsWindow(_settingsService, _audioService)
            {
                Owner = this
            };
            audioWin.ShowDialog();
        }

        // ============================================================
        //  Launch item creation
        // ============================================================

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var path = _fileDialogService.BrowseForExecutableOrScript();
            if (!string.IsNullOrWhiteSpace(path))
            {
                PathTextBox.Text = path;
            }
        }

        private void FromProcessesButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerWindow
            {
                Owner = this
            };

            if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedPath))
            {
                PathTextBox.Text = picker.SelectedPath;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var path = PathTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                System.Windows.MessageBox.Show(this,
                    "Please select a program or script first.",
                    "Missing path",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(path))
            {
                var result = System.Windows.MessageBox.Show(this,
                    "The selected file does not exist on disk. Add it anyway?",
                    "File not found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            if (!int.TryParse(DelayTextBox.Text?.Trim(), out int delaySeconds))
            {
                System.Windows.MessageBox.Show(this,
                    "Delay must be a number (seconds).",
                    "Invalid delay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (LaunchItems == null)
                return;

            string displayName = System.IO.Path.GetFileNameWithoutExtension(path);

            bool startMinimized = StartMinimizedCheckBox.IsChecked == true;
            bool forceMinimize = ForceMinimizeCheckBox.IsChecked == true;
            bool startToTray = StartToTrayCheckBox.IsChecked == true;
            bool useCmdRelay = UseCmdRelayCheckBox.IsChecked == true;
            bool closeToTray = CloseToTrayCheckBox.IsChecked == true;

            var newItem = new LaunchItem
            {
                FullPath = path,
                DisplayName = displayName,
                DelayMs = delaySeconds * 1000,
                StartMinimized = startMinimized,
                ForceMinimize = forceMinimize,
                StartToTray = startToTray,
                UseCmdRelay = useCmdRelay,
                CloseToTray = closeToTray
            };

            int maxOrder = LaunchItems.Any() ? LaunchItems.Max(li => li.Order) : 0;
            newItem.Order = maxOrder + 1;

            LaunchItems.Add(newItem);

            SaveSettings();

            PathTextBox.Clear();
            DelayTextBox.Text = "0";
            StartMinimizedCheckBox.IsChecked = false;
            ForceMinimizeCheckBox.IsChecked = false;
            StartToTrayCheckBox.IsChecked = false;
            UseCmdRelayCheckBox.IsChecked = false;
            CloseToTrayCheckBox.IsChecked = false;
        }


        // ===============================================
        //  Elevation helpers
        // ===============================================
        


        private void RestartElevated()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule!.FileName!;

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"  // triggers UAC elevation
                };

                Process.Start(psi);

                // Close current (non-elevated) instance
                Application.Current.Shutdown();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // 1223 = user cancelled UAC
                _logService.Log("User cancelled elevation request.");
                MessageBox.Show(this,
                    "Elevation was cancelled. The app will continue without admin rights.",
                    "Elevation cancelled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logService.Log($"Failed to restart elevated: {ex}");
                MessageBox.Show(this,
                    $"Could not restart as administrator:\n{ex.Message}",
                    "Elevation error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ============================================================
        //  Discovery callbacks
        // ============================================================

        private void DiscoveredMachinesGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;

            DependencyObject? dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is not DataGridRow row)
                return;

            if (row.Item is not DiscoveredNode node)
                return;

            var win = new DiscoveredNodeDetailsWindow(node)
            {
                Owner = this
            };

            if (win.ShowDialog() == true)
            {
                DiscoveredMachinesGrid.Items.Refresh();
            }
        }

        private void DiscoveryService_NodeDiscovered(string name, string ip, string mode, string mac, bool isSelf)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = DiscoveredMachines
                    .FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    var node = new DiscoveredNode
                    {
                        Name = name,
                        IpAddress = ip,
                        Mode = mode,
                        IsSelf = isSelf,
                        LastSeen = DateTime.Now,
                        MacAddress = mac ?? string.Empty,
                        AllIps = new System.Collections.Generic.List<string> { ip }
                    };

                    DiscoveredMachines.Add(node);
                }
                else
                {
                    existing.Mode = mode;
                    existing.IsSelf = isSelf;
                    existing.LastSeen = DateTime.Now;

                    if (!string.IsNullOrWhiteSpace(mac))
                        existing.MacAddress = mac;

                    if (existing.AllIps == null)
                        existing.AllIps = new System.Collections.Generic.List<string>();

                    if (!existing.AllIps.Contains(ip))
                        existing.AllIps.Add(ip);

                    if (string.IsNullOrWhiteSpace(existing.IpAddress))
                    {
                        existing.IpAddress = ip;
                    }
                }
            });
        }

        private void AddDiscoveredToWolButton_Click(object sender, RoutedEventArgs e)
        {
            if (DiscoveredMachinesGrid.SelectedItem is not DiscoveredNode node)
                return;

            if (node.IsSelf)
            {
                _logService.Log("Ignored adding self from discovered list to WOL.");
                return;
            }

            var existing = RemoteMachines
                .FirstOrDefault(m => m.Name == node.Name || m.IpAddress == node.IpAddress);

            if (existing != null)
            {
                existing.IsSelected = true;
                existing.IpAddress = node.IpAddress;

                if (!string.IsNullOrWhiteSpace(node.MacAddress))
                    existing.MacAddress = node.MacAddress;

                _logService.Log(
                    $"Updated existing WOL machine from discovered peer: {existing.Name} ({existing.IpAddress}), MAC={existing.MacAddress}");
            }
            else
            {
                var rm = new RemoteMachine
                {
                    Name = node.Name,
                    IpAddress = node.IpAddress,
                    MacAddress = string.IsNullOrWhiteSpace(node.MacAddress)
                                 ? ""
                                 : node.MacAddress,
                    IsSelected = true
                };

                RemoteMachines.Add(rm);
                _logService.Log(
                    $"Added discovered peer to WOL list: {rm.Name} ({rm.IpAddress}), MAC={rm.MacAddress}");
            }
        }

        private void RefreshDiscoveryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DiscoveredMachines.Clear();

                _discoveryService?.RequestImmediateUpdate();
                _discoveryService?.ForceBroadcast();

                _logService.Log("Manual discovery refresh triggered.");
            }
            catch (Exception ex)
            {
                _logService.Log($"Failed to refresh discovery: {ex.Message}");
                MessageBox.Show(this,
                    "Could not refresh network discovery.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ============================================================
        //  Run sequence + WOL (with retry + logs + tray balloons)
        // ============================================================

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (LaunchItems.Count == 0)
            {
                MessageBox.Show(this,
                    "No launch items defined.",
                    "Nothing to run",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            LaunchItemsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LaunchItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            SaveSettings();


            // RESET sequence control flags for this run
            _cancelSequence = false;
            _forceShutdownRequested = false;
            _skipDelay = false;
            _skipNextApp = false;
            _currentSequenceIndex = -1;


            var itemsInOrder = LaunchItems.OrderBy(li => li.Order).ToList();
            _logService.Log($"Starting sequence with {itemsInOrder.Count} item(s).");

            var toast = EnsureToast();
            toast.SetHeader("BootLauncherLite sequence");
            toast.SetSequenceStatus("Starting up…");

            toast.SetAudioStatus("Setting default playback / mic…");
            ShowToastAtConfiguredSide(toast);
            ApplyAudioDefaultsWithToast(toast);

            RunButton.IsEnabled = false;
            try
            {
                if (_isMasterMode)
                {
                    _logService.Log("Mode: Master. Sending Wake-on-LAN to selected machines...");
                    await WakeSelectedMachinesAsync(toast);
                }
                else
                {
                    _logService.Log("Mode: Slave. Skipping Wake-on-LAN.");
                    toast.SetSequenceStatus("Slave mode – skipping Wake-on-LAN.");
                }

                toast.SetSequenceStatus("Running launch sequence…");
                

                await RunSequenceWithToastsAsync(itemsInOrder, toast);

                _logService.Log("Sequence completed.");

                var settings = _settingsService.Load();
                if (settings.AutoCloseWhenDone)
                {
                    _logService.Log("Auto-close enabled. Closing application.");
                    Close();
                }

            }
            catch (Exception ex)
            {
                _logService.Log($"Error while running sequence: {ex}");
                MessageBox.Show(this,
                    $"Error while running sequence:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                toast.SetSequenceStatus("Sequence aborted due to error.");
                toast.AddActivity(ex.Message);
            }
            finally
            {
                RunButton.IsEnabled = true;
            }
        }

        private async Task WakeSelectedMachinesAsync(ToastWindow? toast = null)
        {
            // If user already hit "Stop sequence", don't even start WOL
            if (_cancelSequence)
            {
                _logService.Log("Wake-on-LAN skipped because sequence was cancelled.");
                toast?.SetSequenceStatus("Wake-on-LAN cancelled.");
                return;
            }

            var machines = RemoteMachines.ToList();
            var selected = machines
                .Where(m => m.IsSelected && !string.IsNullOrWhiteSpace(m.MacAddress))
                .ToList();

            if (selected.Count == 0)
            {
                _logService.Log("No remote machines selected for Wake-on-LAN.");
                toast?.SetSequenceStatus("No machines selected for Wake-on-LAN.");
                return;
            }

            toast?.SetSequenceStatus($"Sending Wake-on-LAN to {selected.Count} machine(s)…");

            int success = 0;

            foreach (var m in selected)
            {
                // Allow Stop sequence to break WOL mid-flight
                if (_cancelSequence)
                {
                    _logService.Log("Wake-on-LAN cancelled by user during send loop.");
                    toast?.SetSequenceStatus("Wake-on-LAN cancelled.");
                    break;
                }

                toast?.AddWolInfo(m.IpAddress, m.MacAddress);

                try
                {
                    bool ok = await _wolService.WakeAsync(
                        m.MacAddress,
                        m.IpAddress,
                        retries: 3,
                        retryDelaySeconds: 5);

                    if (ok)
                    {
                        success++;
                        _logService.Log($"WOL success: {m.Name} ({m.IpAddress}) MAC={m.MacAddress}");
                    }
                    else
                    {
                        _logService.Log($"WOL likely failed: {m.Name} ({m.IpAddress}) MAC={m.MacAddress}");
                    }
                }
                catch (Exception ex)
                {
                    _logService.Log($"WOL exception for {m.Name} ({m.MacAddress}): {ex.Message}");
                }
            }

            // If cancelled, we already updated status/log above
            if (_cancelSequence)
                return;

            // Normal summary if not cancelled
            if (success == selected.Count)
            {
                toast?.SetSequenceStatus($"Wake-on-LAN OK ({success}/{selected.Count}).");
                _trayIconManager.ShowInfo("Wake-on-LAN",
                    $"Successfully woke {success} / {selected.Count} machines.");
            }
            else
            {
                toast?.SetSequenceStatus(
                    $"Wake-on-LAN partial: {success}/{selected.Count} machine(s) responded.");

                _trayIconManager.ShowError("Wake-on-LAN",
                    $"Woke {success} / {selected.Count} machines.\nSome machines did not respond.");
            }
        }


        // ============================================================
        //  Launch list controls
        // ============================================================

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (LaunchItemsGrid.SelectedItem is not LaunchItem selected)
                return;

            var index = LaunchItems.IndexOf(selected);
            if (index <= 0)
                return;

            LaunchItems.Move(index, index - 1);
            ReindexOrders();
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (LaunchItemsGrid.SelectedItem is not LaunchItem selected)
                return;

            var index = LaunchItems.IndexOf(selected);
            if (index < 0 || index >= LaunchItems.Count - 1)
                return;

            LaunchItems.Move(index, index + 1);
            ReindexOrders();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (LaunchItemsGrid.SelectedItem is not LaunchItem selected)
                return;

            _logService.Log($"Removed item: #{selected.Order} {selected.DisplayName}");
            LaunchItems.Remove(selected);
            ReindexOrders();
        }

        private void SaveLaunchListButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 🔴 Make sure any cell edits (checkbox / delay / path, etc.) are committed
                LaunchItemsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LaunchItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);

                SaveSettingsFromUi();

                _logService.Log("Settings saved manually via Save list button.");
                MessageBox.Show(this,
                    "Launch items and settings have been saved.",
                    "Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logService.Log($"Manual save failed: {ex.Message}");
                MessageBox.Show(this,
                    $"Failed to save settings:\n{ex.Message}",
                    "Save error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public static class SecurityHelper
        {
            public static bool IsRunningAsAdmin()
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // ============================================================
        //  Log window
        // ============================================================

        private void ShowLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_logWindow == null || !_logWindow.IsLoaded)
            {
                _logWindow = new LogWindow(_settingsService, _logService)
                {
                    Owner = this
                };
            }

            _logWindow.Show();
            _logWindow.Activate();
        }

        // ============================================================
        //  Startup folder helpers
        // ============================================================

        private void OpenUserStartupFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:startup",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                _logService.Log("Opened user Startup folder (shell:startup).");
            }
            catch (Exception ex)
            {
                _logService.Log($"Failed to open user Startup folder: {ex.Message}");
                MessageBox.Show(this,
                    "Could not open the user Startup folder.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenCommonStartupFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:common startup",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                _logService.Log("Opened common Startup folder (shell:common startup).");
            }
            catch (Exception ex)
            {
                _logService.Log($"Failed to open common Startup folder: {ex.Message}");
                MessageBox.Show(this,
                    "Could not open the common Startup folder.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ============================================================
        //  Window lifecycle
        // ============================================================

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                try { Hide(); } catch { }
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            SaveSettings();
            _trayIconManager.Dispose();
            _toastWindow?.Close();
            _logWindow?.Close();
            _discoveryService?.Dispose();
        }

        private void LaunchItemsGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;

            // 🔴 Commit any pending edit in the grid BEFORE we inspect SelectedItem or open the window
            LaunchItemsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LaunchItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is not DataGridRow row)
                return;

            if (row.Item is not LaunchItem item)
                return;

            var win = new LaunchItemAttributesWindow(item, _settingsService)
            {
                Owner = this
            };

            if (win.ShowDialog() == true)
            {
                SaveSettingsFromUi();
                LaunchItemsGrid.Items.Refresh();
            }
        }


        // ============================================================
        //  WOL / Master-Slave UI Handlers
        // ============================================================

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            _isMasterMode = MasterRadio.IsChecked == true;

            if (_logService == null)
                return;

            _logService.Log($"Mode set to: {(_isMasterMode ? "Master" : "Slave")}");
        }

        private void DisableHeartbeatCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = DisableHeartbeatCheckBox.IsChecked != true;

            _discoveryService?.SetHeartbeatEnabled(enabled);

            _logService.Log(enabled
                ? "Discovery heartbeat enabled."
                : "Discovery heartbeat disabled by user.");
        }

        private void LaunchItemsGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;

            // 🔴 Commit edits before opening the attribute window
            LaunchItemsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LaunchItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            DependencyObject? dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is not DataGridRow row)
                return;

            if (row.Item is not LaunchItem item)
                return;

            var win = new LaunchItemAttributesWindow(item, _settingsService)
            {
                Owner = this
            };

            if (win.ShowDialog() == true)
            {
                SaveSettingsFromUi();
                LaunchItemsGrid.Items.Refresh();
            }
        }


        private void AddRemoteMachineButton_Click(object sender, RoutedEventArgs e)
        {
            var machine = new RemoteMachine
            {
                Name = "New machine",
                IpAddress = "",
                MacAddress = "",
                IsSelected = true
            };

            RemoteMachines.Add(machine);
            _logService.Log($"Added remote machine placeholder: {machine.Name}. Edit IP/MAC in the list.");
        }

        private void RemoveRemoteMachineButton_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteMachinesGrid.SelectedItem is RemoteMachine rm)
            {
                RemoteMachines.Remove(rm);
                _logService.Log($"Removed remote machine: {rm.Name}");
            }
        }

        private async void TestWolButton_Click(object sender, RoutedEventArgs e)
        {
            _logService.Log("Test WOL requested by user.");
            await WakeSelectedMachinesAsync();
        }

        // ============================================================
        //  Toast-based sequence
        // ============================================================

        private ToastWindow EnsureToast()
        {
            if (_toastWindow == null || !_toastWindow.IsLoaded)
            {
                _toastWindow = new ToastWindow();

                // Skip only the delay
                _toastWindow.SkipDelayRequested += () =>
                {
                    _skipDelay = true;
                    _logService.Log("User clicked 'Skip delay' on toast.");
                    _toastWindow.AddActivity("Delay skipped by user.");
                };

                // Skip the next app
                _toastWindow.SkipAppRequested += () =>
                {
                    _skipNextApp = true;
                    _logService.Log("User clicked 'Skip next app' on toast.");
                    _toastWindow.AddActivity("Next app marked as skipped.");
                };

                // STOP SEQUENCE (but keep app open)
                _toastWindow.StopSequenceRequested += () =>
                {
                    _cancelSequence = true;
                    _logService.Log("User clicked 'Stop sequence' on toast.");
                    _toastWindow.AddActivity("Sequence cancelled by user.");
                };

                // FORCE SHUTDOWN = close BootLauncherLite only (NOT Windows)
                _toastWindow.ForceShutdownRequested += () =>
                {
                    _cancelSequence = true;
                    _forceShutdownRequested = true;

                    _logService.Log("User clicked 'Force shutdown' on toast (closing app).");
                    _toastWindow.AddActivity("Sequence cancelled – closing launcher.");

                    // Close the WPF app
                    Dispatcher.Invoke(() =>
                    {
                        try { Close(); } catch { /* ignore */ }
                    });
                };
            }

            return _toastWindow;
        }



        // ============================================================
        //  Toast helpers
        // ============================================================

        private void SetNextAppLabelWithAction(ToastWindow toast, LaunchItem? nextItem, int delaySeconds)
        {
            if (nextItem == null)
            {
                toast.SetNextApp("None (this is the last app)", delaySeconds);
                return;
            }

            bool isKill = nextItem.KillInsteadOfLaunch;
            string prefix = isKill ? "Kill " : "Launch ";
            toast.SetNextApp(prefix + nextItem.DisplayName, delaySeconds);
        }

        private async Task RunSequenceWithToastsAsync(
            System.Collections.Generic.List<LaunchItem> items, ToastWindow toast)
        {
            if (items == null || items.Count == 0)
                return;

            toast.SetSequenceStatus("Running launch sequence…");
           

            _currentSequenceIndex = -1;
            _skipDelay = false;
            _skipNextApp = false;

            int index = 0;

            while (index < items.Count)
            {
                // === GLOBAL CANCEL CHECK (Stop sequence / Force shutdown) ===
                if (_cancelSequence)
                {
                    _logService.Log("Sequence cancelled before launching next app.");
                    toast.SetSequenceStatus("Sequence cancelled.");
                    toast.SetNextApp(string.Empty, 0);
                    toast.SetCountdown(0);
                    toast.AddActivity("Sequence stopped by user.");
                    toast.HideWithFade();
                    _currentSequenceIndex = -1;
                    return;
                }

                _currentSequenceIndex = index;
                var current = items[index];

                bool isKill = current.KillInsteadOfLaunch;
                string verb = isKill ? "Killing" : "Launching";

                _logService.Log($"{verb} app: {current.DisplayName} ({current.FullPath})");
                toast.AddActivity($"{verb}: {current.DisplayName}");

                _launchSequenceService.StartProcess(current);

                int delayMs = Math.Max(0, current.DelayMs);
                int delaySeconds = delayMs / 1000;

                int nextIndex = index + 1;

                if (nextIndex >= items.Count)
                {
                    // last app, nothing more to do
                    break;
                }

                _skipDelay = false;

                // === COUNTDOWN LOOP BETWEEN APPS ===
                while (delaySeconds > 0)
                {
                    // CANCEL during countdown
                    if (_cancelSequence)
                    {
                        _logService.Log("Sequence cancelled during delay.");
                        toast.SetSequenceStatus("Sequence cancelled.");
                        toast.SetNextApp(string.Empty, 0);
                        toast.SetCountdown(0);
                        toast.AddActivity("Sequence stopped by user.");
                        toast.HideWithFade();
                        _currentSequenceIndex = -1;
                        return;
                    }

                    // Skip to another app
                    if (_skipNextApp)
                    {
                        _skipNextApp = false;

                        if (nextIndex + 1 < items.Count)
                        {
                            var skipped = items[nextIndex];
                            _logService.Log($"Skip to next – will NOT launch: {skipped.DisplayName}");
                            toast.AddActivity($"Skip app: {skipped.DisplayName}");
                            nextIndex++;
                        }
                        else
                        {
                            var skipped = items[nextIndex];
                            _logService.Log($"Skip to next – last app skipped: {skipped.DisplayName}");
                            toast.AddActivity($"Skip last app: {skipped.DisplayName}");
                            toast.SetNextApp("None (sequence end)", 0);
                            toast.SetCountdown(0);

                            _currentSequenceIndex = -1;
                            toast.SetSequenceStatus("Sequence ended (no more apps).");
                            toast.HideWithFade();
                            return;
                        }
                    }

                    var nextItem = items[nextIndex];
                    bool nextIsKill = nextItem.KillInsteadOfLaunch;
                    string prefix = nextIsKill ? "Kill " : "Launch ";

                    toast.SetNextApp(prefix + nextItem.DisplayName, delaySeconds);
                    toast.SetCountdown(delaySeconds);

                    int appsLeftAfterTarget = items.Count - (nextIndex + 1);
                    toast.SetSequenceStatus(
                        appsLeftAfterTarget > 0
                            ? $"Waiting before next app… ({appsLeftAfterTarget} app(s) left after this)"
                            : "Waiting before final app…");

                    if (_skipDelay)
                    {
                        _skipDelay = false;
                        _logService.Log("Delay skipped by user.");
                        toast.AddActivity("Delay skipped – launching next app now.");
                        break;
                    }

                    await Task.Delay(1000);
                    delaySeconds--;
                }

                index = nextIndex;
            }

            _currentSequenceIndex = -1;

            if (_cancelSequence)
            {
                // If we fall out here with cancel true (edge case)
                _logService.Log("Sequence cancelled at end.");
                toast.SetSequenceStatus("Sequence cancelled.");
                toast.AddActivity("Sequence stopped by user.");
            }
            else
            {
                toast.SetSequenceStatus("All done.");
                toast.AddActivity("Sequence complete.");
            }

            toast.SetNextApp(string.Empty, 0);
            toast.SetCountdown(0);
            toast.HideWithFade();
        }

        // Helper: show toast at configured side (left/right)
        private void ShowToastAtConfiguredSide(ToastWindow toast)
        {
            var settings = _settingsService.Load();
            var workArea = SystemParameters.WorkArea;

            double top = workArea.Top + 20;
            double left;

            if (settings.ToastOnRight)
                left = workArea.Right - toast.Width - 20;
            else
                left = workArea.Left + 20;

            // Clear any leftover fade-out animation
            toast.BeginAnimation(Window.OpacityProperty, null);

            toast.Top = top;
            toast.Left = left;

            toast.Opacity = 0.96;
            toast.Show();
            toast.Activate();
        }




        private void SetThisAppLabelWithAction(ToastWindow toast, LaunchItem item, int delaySeconds)
        {
            string prefix = item.KillInsteadOfLaunch ? "Kill " : "Launch ";
            toast.SetNextApp(prefix + item.DisplayName, delaySeconds);
        }

        // ============================================================
        //  Settings Load/Save
        // ============================================================

        private void LoadSettings()
        {
            try
            {
                var settings = _settingsService.Load();

                LaunchItems.Clear();
                if (settings.LaunchItems != null)
                {
                    foreach (var item in settings.LaunchItems.OrderBy(li => li.Order))
                        LaunchItems.Add(item);
                }
                ReindexOrders();

                AutoCloseCheckBox.IsChecked = settings.AutoCloseWhenDone;

                RemoteMachines.Clear();
                if (settings.RemoteMachines != null)
                {
                    foreach (var m in settings.RemoteMachines)
                        RemoteMachines.Add(m);
                }

                _isMasterMode = settings.IsMasterMode;
                if (_isMasterMode)
                {
                    MasterRadio.IsChecked = true;
                    SlaveRadio.IsChecked = false;
                }
                else
                {
                    MasterRadio.IsChecked = false;
                    SlaveRadio.IsChecked = true;
                }

                // startup mode is handled in LoadSettingsIntoUi as well
                _startupMode = settings.StartupMode;
            }
            catch
            {
                // ignore for now – bad JSON etc.
            }
        }

        private void SaveSettings()
        {
            try
            {
                SaveSettingsFromUi();
            }
            catch
            {
                // ignore – not critical for running
            }
        }

        // ============================================================
        //  Helpers
        // ============================================================

        private void ReindexOrders()
        {
            for (int i = 0; i < LaunchItems.Count; i++)
            {
                LaunchItems[i].Order = i + 1;
            }

            LaunchItemsGrid.Items.Refresh();
        }

        private void LaunchItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void LaunchItemsGrid_SelectionChanged_1(object sender, SelectionChangedEventArgs e)
        {
        }

        private void HeartBeatOff_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void AutoCloseCheckBox_Checked(object sender, RoutedEventArgs e)
        {
        }

        // ============================================================
        //  Startup mode combo handler
        // ============================================================

        

    }
}


