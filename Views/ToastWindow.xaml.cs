using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace BootLauncherLite.Views
{
    public partial class ToastWindow : Window
    {
        // Fired back to MainWindow
        public event Action? SkipDelayRequested;
        public event Action? SkipAppRequested;
        public event Action? StopSequenceRequested;
        public event Action? ForceShutdownRequested;

        // Simple state so we can compose the body text
        private string _sequenceStatus = string.Empty;
        private string _nextAppLabel = string.Empty;
        private int _countdownSeconds;
        private string _audioStatus = string.Empty;
        private readonly List<string> _activityLines = new();

        public ToastWindow()
        {
            InitializeComponent();

            
            
        }

       

        // ---------- Public API used from MainWindow ----------

        public void SetHeader(string text)
        {
            TitleTextBlock.Text = text ?? string.Empty;
        }

        public void SetSequenceStatus(string text)
        {
            _sequenceStatus = text ?? string.Empty;
            RefreshBody();
        }

        public void SetNextApp(string label, int delaySeconds)
        {
            _nextAppLabel = label ?? string.Empty;
            _countdownSeconds = delaySeconds;
            RefreshBody();
        }

        public void SetCountdown(int seconds)
        {
            _countdownSeconds = seconds;
            RefreshBody();
        }

        public void SetAudioStatus(string text)
        {
            _audioStatus = text ?? string.Empty;
            RefreshBody();
        }

        public void AddActivity(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            _activityLines.Add(text);
            // keep last few so it doesn’t explode
            if (_activityLines.Count > 20)
                _activityLines.RemoveRange(0, _activityLines.Count - 20);

            RefreshBody();
        }

        public void AddWolInfo(string ip, string mac)
        {
            AddActivity($"WOL → {ip}  ({mac})");
        }

        public void ShowNearTopLeft()
        {
            // Actually top-right of work area
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 16;
            Top = wa.Top + 16;
            Show();
        }

        public void HideWithFade()
        {
            Dispatcher.Invoke(async () =>
            {
                try
                {
                    // Stop any previous opacity animations first
                    BeginAnimation(Window.OpacityProperty, null);

                    var anim = new DoubleAnimation
                    {
                        From = Opacity,
                        To = 0.0,
                        Duration = TimeSpan.FromMilliseconds(200),
                        FillBehavior = FillBehavior.Stop
                    };

                    anim.Completed += (_, __) =>
                    {
                        // Actually hide the window and reset for next run
                        Hide();
                        // IMPORTANT: clear animation and reset opacity
                        BeginAnimation(Window.OpacityProperty, null);
                        Opacity = 0.96;
                    };

                    BeginAnimation(Window.OpacityProperty, anim);
                }
                catch
                {
                    // Fallback: just hide + reset opacity
                    BeginAnimation(Window.OpacityProperty, null);
                    Hide();
                    Opacity = 0.96;
                }
            });
        }

        // ---------- Private helpers ----------

        private void RefreshBody()
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(_sequenceStatus))
                sb.AppendLine(_sequenceStatus);

            if (!string.IsNullOrWhiteSpace(_nextAppLabel))
                sb.AppendLine("Next: " + _nextAppLabel);

            if (_countdownSeconds > 0)
                sb.AppendLine($"In: {_countdownSeconds} s");

            if (!string.IsNullOrWhiteSpace(_audioStatus))
                sb.AppendLine("Audio: " + _audioStatus);

            if (_activityLines.Count > 0)
            {
                sb.AppendLine();
                foreach (var line in _activityLines.TakeLast(5))
                    sb.AppendLine("• " + line);
            }

            BodyTextBlock.Text = sb.ToString().TrimEnd();
        }

        // ---------- Button click handlers (wired from XAML) ----------

        private void SkipDelayButton_Click(object sender, RoutedEventArgs e)
        {
            SkipDelayRequested?.Invoke();
        }

        private void SkipNextAppButton_Click(object sender, RoutedEventArgs e)
        {
            SkipAppRequested?.Invoke();
        }

        private void StopSequenceButton_Click(object sender, RoutedEventArgs e)
        {
            StopSequenceRequested?.Invoke();
        }

        private void ForceShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            ForceShutdownRequested?.Invoke();
        }
    }
}

