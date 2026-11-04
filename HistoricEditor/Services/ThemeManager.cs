using System.Windows;

namespace HistoricEditor;

public enum AppTheme
{
    Light,
    Dark,
}

// Gestisce il tema chiaro/scuro a runtime e lo conserva fra le sessioni.
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static void Initialize()
    {
        TitleBarTheme.Register();
        Apply(Load(), persist: false);
    }

    public static void Apply(AppTheme theme, bool persist = true)
    {
        var uri = new Uri(theme == AppTheme.Dark ? "/HistoricEditor;component/Themes/Dark.xaml" : "/HistoricEditor;component/Themes/Light.xaml", UriKind.Relative);
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            // La palette è l'unico dizionario che definisce i colori di base.
            if (dictionaries[i].Contains("AppBackground"))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = uri });
        Current = theme;
        TitleBarTheme.ApplyToOpenWindows(theme == AppTheme.Dark);

        if (persist)
        {
            Save(theme);
        }
    }

    private static AppTheme Load()
        => Enum.TryParse<AppTheme>(SettingsStore.Load().Theme, ignoreCase: true, out var theme) && Enum.IsDefined(theme) ? theme : AppTheme.Light;

    private static void Save(AppTheme theme) => SettingsStore.Update(s => s.Theme = theme.ToString());
}
