using System.IO;
using System.Text.Json;

namespace HistoricEditor;

// Cosa succede con doppio clic o Invio su un personaggio.
public enum PersonOpenMode
{
    // Apre la scheda di modifica (comportamento storico).
    Edit,

    // Apre la scheda di lettura, senza campi modificabili.
    View,
}

public sealed class AppSettings
{
    public string Theme { get; set; } = nameof(AppTheme.Light);

    public string OpenMode { get; set; } = nameof(PersonOpenMode.Edit);

    // Chiave personale delle API di Europeana; vuota = chiave dimostrativa pubblica.
    public string EuropeanaKey { get; set; } = string.Empty;

    // Dopo l'import di un personaggio, importa anche i suoi eventi da Wikidata (operazione lunga: disattivata in modo predefinito).
    public bool ImportPersonEvents { get; set; }

    // Nell'elenco delle persone mostra il nome latino dei personaggi romani sotto quello italiano.
    public bool ShowLatinNames { get; set; }

    // Formato delle date scritte dall'app e riconosciute nei campi (DateFormats): GG/MM/AAAA, GG.MM.AAAA o GG-MM-AAAA.
    public string DateFormat { get; set; } = DateFormats.Slash;

    public PersonOpenMode PersonOpenMode
        => Enum.TryParse<PersonOpenMode>(OpenMode, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : PersonOpenMode.Edit;
}

// Preferenze dell'interfaccia in settings.json; ogni modifica rilegge il file per non perdere le altre impostazioni.
public static class SettingsStore
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlberiStorici",
        "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize<AppSettings>(stream) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Preferenze non leggibili: si usano i valori predefiniti.
        }

        return new AppSettings();
    }

    public static void Update(Action<AppSettings> change, string? path = null)
    {
        path ??= DefaultPath;
        var settings = Load(path);
        change(settings);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferenze non salvabili: si ignora.
        }
    }
}
