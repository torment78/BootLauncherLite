using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace BootLauncherLite.Tray
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Action? _onDoubleClick;

        public TrayIconManager(Action? onDoubleClick = null)
        {
            _onDoubleClick = onDoubleClick;

            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "BootLauncher",
                Icon = LoadIconSafe()
            };

            // Double-click → show main window
            _notifyIcon.DoubleClick += (s, e) =>
            {
                _onDoubleClick?.Invoke();
            };
        }

        private static Icon LoadIconSafe()
        {
            try
            {
                // Use the app’s own EXE icon
                string exePath = Process.GetCurrentProcess().MainModule!.FileName!;
                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                    return icon;
            }
            catch
            {
                // ignored
            }

            // Fallback
            return SystemIcons.Application;
        }

        public void ShowInfo(string title, string text)
        {
            if (!_notifyIcon.Visible) _notifyIcon.Visible = true;

            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(5000);
        }

        public void ShowError(string title, string text)
        {
            if (!_notifyIcon.Visible) _notifyIcon.Visible = true;

            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Error;
            _notifyIcon.ShowBalloonTip(5000);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}

