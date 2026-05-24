using System;
using System.Windows;

namespace EldenRingArmorStudio.Core
{
    public enum AppTheme { Dark, Light }

    public static class ThemeManager
    {
        private const string DarkUri = "Themes/DarkTheme.xaml";
        private const string LightUri = "Themes/LightTheme.xaml";

        public static AppTheme Current { get; private set; } = AppTheme.Dark;

        public static void Apply(AppTheme theme)
        {
            Current = theme;
            var uri = new Uri(theme == AppTheme.Dark ? DarkUri : LightUri,
                              UriKind.Relative);

            var dicts = Application.Current.Resources.MergedDictionaries;

            for (int i = 0; i < dicts.Count; i++)
            {
                var src = dicts[i].Source?.OriginalString ?? "";
                if (src.Contains("DarkTheme") || src.Contains("LightTheme"))
                {
                    dicts[i] = new ResourceDictionary { Source = uri };
                    SavePreference(theme);
                    // Propagar foreground a AvalonDock y controles que no
                    // respetan DynamicResource por sí solos
                    EldenRingArmorStudio.App.ApplyForegroundToAllWindows();
                    return;
                }
            }

            dicts.Insert(0, new ResourceDictionary { Source = uri });
            SavePreference(theme);
            EldenRingArmorStudio.App.ApplyForegroundToAllWindows();
        }

        public static void Toggle() =>
            Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

        public static void LoadSavedOrDefault()
        {
            var saved = AppConfig.Instance.Ui.DarkMode;
            Apply(saved ? AppTheme.Dark : AppTheme.Light);
        }

        private static void SavePreference(AppTheme theme)
        {
            AppConfig.Instance.Ui.DarkMode = (theme == AppTheme.Dark);
            AppConfig.Instance.Save();
        }
    }
}