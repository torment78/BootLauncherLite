using System.Windows;
using System.Windows.Media;

namespace BootLauncherLite.Services
{
    public static class ToastThemeBrushHelper
    {
        public static void ApplyToastThemeToResources(
            ResourceDictionary resources,
            ToastThemeSettings toast)
        {
            if (toast == null) return;

            var bgColor = ColorFromHex(toast.BackgroundHex);
            var bgBrush = new SolidColorBrush(bgColor)
            {
                Opacity = Clamp01(toast.BackgroundOpacity)
            };
            bgBrush.Freeze();

            resources["ToastBackgroundBrush"] = bgBrush;
            resources["ToastBorderBrush"] = MakeFrozenBrush(toast.BorderHex);

            resources["ToastButtonBackgroundBrush"] = MakeFrozenBrush(toast.ButtonBackgroundHex);
            resources["ToastButtonBorderBrush"] = MakeFrozenBrush(toast.ButtonBorderHex);
            resources["ToastButtonTextBrush"] = MakeFrozenBrush(toast.ButtonTextHex);
            resources["ToastButtonHoverBackgroundBrush"] = MakeFrozenBrush(toast.ButtonHoverBackgroundHex);
            resources["ToastButtonHoverBorderBrush"] = MakeFrozenBrush(toast.ButtonHoverBorderHex);

            resources["ToastHeaderTextBrush"] = MakeFrozenBrush(toast.HeaderTextHex);
            resources["ToastBodyTextBrush"] = MakeFrozenBrush(toast.BodyTextHex);
        }

        private static SolidColorBrush MakeFrozenBrush(string hex)
        {
            var c = ColorFromHex(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Color ColorFromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Colors.Transparent;

            hex = hex.Trim();

            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return Colors.Magenta;
            }
        }


        public static class AutoThemeToastHelper
        {
            /// <summary>
            /// Copies the current base theme colors from AppSettings into ToastTheme,
            /// so the toast automatically follows the active theme.
            /// Call this after you have set ThemeOuterBackground / ThemeCardBackground / Button colors etc.
            /// </summary>
            public static void SyncToastFromBaseTheme(AppSettings s)
            {
                if (s == null) return;

                if (s.ToastTheme == null)
                    s.ToastTheme = new ToastThemeSettings();

                // Panel background & border
                s.ToastTheme.BackgroundHex = s.ThemeCardBackground ?? "#111827";
                s.ToastTheme.BorderHex = s.ThemeButtonBorder ?? "#374151";

                // Text
                s.ToastTheme.HeaderTextHex = s.ThemeCardHeaderForeground ?? s.ThemeTextColor ?? "#FFFFFF";
                s.ToastTheme.BodyTextHex = s.ThemeTextColor ?? "#E5E7EB";

                // Buttons
                s.ToastTheme.ButtonBackgroundHex = s.ThemeButtonBackground ?? "#1F2937";
                s.ToastTheme.ButtonBorderHex = s.ThemeButtonBorder ?? "#374151";
                
                s.ToastTheme.ButtonHoverBackgroundHex = s.ThemeButtonHoverBackground ?? "#2B5EBF";
                s.ToastTheme.ButtonHoverBorderHex = s.ThemeButtonHoverBorder ?? "#60A5FA";

                // Opacity – whatever you like, 0–1 range.
                // Your ToastThemeSettings already has BackgroundOpacity since you used it in the helper.
                s.ToastTheme.BackgroundOpacity = Math.Clamp(s.ToastTheme.BackgroundOpacity <= 0 ? 0.96 : s.ToastTheme.BackgroundOpacity, 0.0, 1.0);
            }
        }

        private static double Clamp01(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }
    }
}

