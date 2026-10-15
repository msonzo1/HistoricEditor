using System.IO;
using System.Text.Json;
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
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlberiStorici",
        "settings.json");

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static void Initialize() => Apply(Load(), persist: false);

    public static void Apply(AppTheme theme, bool persist = true)
    {
        var uri = new Uri(theme == AppTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
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

        if (persist)
        {
            Save(theme);
        }
    }

    private static AppTheme Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var stream = File.OpenRead(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(stream);
                if (settings is not null && Enum.TryParse<AppTheme>(settings.Theme, ignoreCase: true, out var theme))
                {
                    return theme;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Preferenza non leggibile: si usa il tema chiaro.
        }

        return AppTheme.Light;
    }

    private static void Save(AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new AppSettings { Theme = theme.ToString() }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferenza non salvabile: si ignora.
        }
    }

    private sealed class AppSettings
    {
        public string Theme { get; set; } = nameof(AppTheme.Light);
    }
}
