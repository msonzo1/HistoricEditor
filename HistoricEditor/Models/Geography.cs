namespace HistoricEditor;

// Nome di un luogo in una determinata lingua (it/en/de/fr/es/nl), dalle etichette Wikidata.
public sealed class PlaceName
{
    public string Language { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class Luogo
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public Luogo? Parent { get; set; }
    public List<Luogo> Figli { get; set; } = [];
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }

    // Nomi del luogo nelle altre lingue (etichette Wikidata).
    public List<PlaceName> NomiAltreLingue { get; set; } = [];

    // Il livello successivo dello schema non esiste sotto questo luogo (es. Comunità di Madrid senza Provincia).
    public bool SenzaLivelloInferiore { get; set; }

    // Luoghi storici non più esistenti.
    public bool Storico { get; set; }
    public string Periodo { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

// Luogo puntuale (edificio, monastero, reggia…) sempre contenuto in un luogo amministrativo.
public sealed class LuogoSpecifico
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int LuogoId { get; set; }
    public Luogo? Luogo { get; set; }
    public string Indirizzo { get; set; } = string.Empty;
    public string Condizione { get; set; } = CondizioniLuogo.Esistente;
    public string DataDistruzione { get; set; } = string.Empty;
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }
    public string Note { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;
}

public static class CondizioniLuogo
{
    public const string Esistente = "Esistente";
    public const string InRovina = "In rovina";
    public const string Distrutto = "Distrutto";

    public static readonly string[] Tutte = [Esistente, InRovina, Distrutto];
}

// Schemi dei livelli amministrativi per Paese (dal livello radice al più fine).
public static class GeoSchemas
{
    private static readonly Dictionary<string, string[]> ByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Italia"] = ["Stato", "Regione", "Provincia", "Comune"],
        ["Francia"] = ["Stato", "Regione", "Dipartimento", "Arrondissement", "Comune"],
        ["Germania"] = ["Stato", "Land", "Circondario", "Comune"],
        ["Spagna"] = ["Stato", "Comunità autonoma", "Provincia", "Comune"],
        ["Portogallo"] = ["Stato", "Regione", "Distretto", "Comune"],
        ["Belgio"] = ["Stato", "Regione", "Provincia", "Arrondissement", "Comune"],
        ["Paesi Bassi"] = ["Stato", "Provincia", "Comune"],
        ["Regno Unito"] = ["Stato", "Nazione", "Contea", "Distretto"],
    };

    public static readonly string[] Fallback = ["Stato", "Divisione", "Comune"];

    // Suddivisioni note prive del livello successivo, usate per riconoscerle durante l'import.
    private static readonly (string Country, string Tipo, string[] Keywords)[] NoLowerLevelHints =
    [
        ("Spagna", "Comunità autonoma", ["Madrid", "Asturie", "Asturias", "Cantabria", "Rioja", "Navarra", "Baleari", "Balears", "Baleares", "Murcia"]),
        ("Belgio", "Regione", ["Bruxelles"]),
        // Le regioni autonome portoghesi non sono suddivise in distretti.
        ("Portogallo", "Regione", ["Azzorre", "Azores", "Açores", "Madera", "Madeira"]),
    ];

    // Paesi i cui livelli si leggono dall'infobox {{Divisione amministrativa}} della pagina it.wikipedia del comune.
    private static readonly HashSet<string> InfoboxCountries = new(StringComparer.OrdinalIgnoreCase) { "Portogallo" };

    public static bool UsesInfoboxDivisions(string country)
        => !string.IsNullOrWhiteSpace(country) && InfoboxCountries.Contains(country.Trim());

    public static IReadOnlyList<string> KnownCountries => [.. ByCountry.Keys];

    public static bool IsKnown(string country)
        => !string.IsNullOrWhiteSpace(country) && ByCountry.ContainsKey(country.Trim());

    public static string[] For(string country)
        => !string.IsNullOrWhiteSpace(country) && ByCountry.TryGetValue(country.Trim(), out var levels)
            ? levels
            : Fallback;

    public static bool HintsNoLowerLevel(string country, string tipo, string nome)
    {
        var value = nome?.Trim() ?? string.Empty;
        return value.Length > 0 && NoLowerLevelHints.Any(hint =>
            string.Equals(hint.Country, country?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(hint.Tipo, tipo, StringComparison.OrdinalIgnoreCase)
            && hint.Keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class GeoLevel
{
    public string Tipo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;

    // Nomi del livello nelle altre lingue (etichette Wikidata), salvati sul Luogo.
    public List<PlaceName> Nomi { get; set; } = [];

    // Coordinate del livello (P625 da Wikidata), salvate sul Luogo quando disponibili.
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }

    // Il livello non esiste per questo luogo (es. Provincia sotto la Comunità di Madrid).
    public bool Assente { get; set; }

    public GeoLevel()
    {
    }

    public GeoLevel(string tipo, string nome)
    {
        Tipo = tipo;
        Nome = nome;
    }
}

public static class CoordinateText
{
    public static bool TryParse(string text, out double? value)
    {
        value = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (double.TryParse(trimmed.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    public static string Format(double? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}
