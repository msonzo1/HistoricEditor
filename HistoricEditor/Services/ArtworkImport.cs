using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Fonti online da cui si possono importare le opere d'arte.
public enum ArtworkSource
{
    Wikipedia,
    Metropolitan,
    ArtInstitute,
    Cleveland,
    Europeana,
}

public sealed record ArtworkSourceInfo(ArtworkSource Fonte, string Nome, string Descrizione)
{
    public override string ToString() => Nome;

    public static IReadOnlyList<ArtworkSourceInfo> Tutte { get; } =
    [
        new(ArtworkSource.Wikipedia, "Wikipedia e Wikidata", "Voci di Wikipedia in italiano; dati e immagine da Wikidata e Wikimedia Commons."),
        new(ArtworkSource.Metropolitan, "Metropolitan Museum (New York)", "Collezione del Met; immagini solo per le opere di pubblico dominio."),
        new(ArtworkSource.ArtInstitute, "Art Institute of Chicago", "Collezione dell’Art Institute; immagini di pubblico dominio."),
        new(ArtworkSource.Cleveland, "Cleveland Museum of Art", "Collezione del museo di Cleveland; immagini con licenza CC0."),
        new(ArtworkSource.Europeana, "Europeana (musei europei)", "Musei, archivi e biblioteche europee, compresi quelli italiani."),
    ];
}

// Risultato di una ricerca: Id è il titolo della voce (Wikipedia) o l'identificativo dell'oggetto nel museo.
public sealed record ArtworkSearchResult(ArtworkSource Fonte, string Id, string Titolo, string Dettaglio)
{
    public string Etichetta => Dettaglio.Length > 0 ? $"{Titolo}, {Dettaglio}" : Titolo;
}

// Persona collegata all'opera: il Q-id può mancare (alcuni musei danno solo il nome e gli anni).
public sealed record ArtworkImportPerson(string WikidataId, string Nome, string Ruolo)
{
    public int? AnnoNascita { get; init; }
    public int? AnnoMorte { get; init; }
}

// Dati di un'opera letti da una fonte online, da verificare prima del salvataggio.
public sealed class ArtworkImport
{
    public string Titolo { get; init; } = string.Empty;
    public string Tipo { get; init; } = string.Empty;
    public string Data { get; init; } = string.Empty;
    public string Tecnica { get; init; } = string.Empty;
    public string Dimensioni { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public string WikidataId { get; init; } = string.Empty;

    // Indirizzi da aggiungere alle fonti (voce, pagina del museo, licenza dell'immagine).
    public List<string> Fonti { get; init; } = [];

    public bool Disambiguazione { get; init; }

    public List<ArtworkImportPerson> Persone { get; init; } = [];

    // Collocazione (museo o chiesa) con la gerarchia dei luoghi, come per gli eventi.
    public HistoricalPlaceRecord? Luogo { get; init; }

    // Nome della collocazione quando non è stato possibile leggerne la gerarchia.
    public string Collocazione { get; init; } = string.Empty;

    // Immagine scaricabile (vuoto se assente o non libera).
    public string ImmagineUrl { get; init; } = string.Empty;
}

// Conversioni dei testi dei musei (in inglese) nel formato delle schede.
public static partial class ArtworkText
{
    private static readonly (string Inglese, string Italiano)[] Words =
    [
        ("oil paint", "olio"), ("oil", "olio"), ("tempera", "tempera"), ("gold leaf", "foglia d’oro"), ("gold", "oro"), ("silver", "argento"),
        ("canvas", "tela"), ("wood panel", "tavola"), ("panel", "tavola"), ("wood", "legno"), ("poplar", "pioppo"), ("oak", "quercia"),
        ("walnut", "noce"), ("copper", "rame"), ("paper", "carta"), ("parchment", "pergamena"), ("vellum", "pergamena"), ("slate", "ardesia"),
        ("marble", "marmo"), ("bronze", "bronzo"), ("terracotta", "terracotta"), ("plaster", "gesso"), ("ivory", "avorio"), ("limestone", "calcare"),
        ("sandstone", "arenaria"), ("alabaster", "alabastro"), ("stone", "pietra"), ("glass", "vetro"), ("enamel", "smalto"), ("fresco", "affresco"),
        ("watercolor", "acquerello"), ("watercolour", "acquerello"), ("gouache", "guazzo"), ("pastel", "pastello"), ("charcoal", "carboncino"),
        ("black chalk", "matita nera"), ("red chalk", "sanguigna"), ("chalk", "gessetto"), ("graphite", "grafite"), ("pen", "penna"),
        ("brown ink", "inchiostro bruno"), ("ink", "inchiostro"), ("wash", "acquerellato"), ("etching", "acquaforte"), ("engraving", "incisione a bulino"),
        ("woodcut", "xilografia"), ("lithograph", "litografia"), ("gilding", "doratura"), ("gilt", "dorato"), ("gilded", "dorato"), ("linen", "lino"),
        ("silk", "seta"), ("wool", "lana"), ("cardboard", "cartone"), ("transferred to", "trasportato su"), ("on", "su"), ("and", "e"), ("with", "con"),
        ("over", "su"), ("traces of", "tracce di"), ("touches of", "tocchi di"), ("heightened with", "lumeggiato a"), ("white", "bianco"),
    ];

    private static readonly (string Inglese, string Italiano)[] Types =
    [
        ("painting", "Dipinto"), ("paintings", "Dipinto"), ("sculpture", "Scultura"), ("sculptures", "Scultura"), ("drawing", "Disegno"),
        ("drawings", "Disegno"), ("drawings and watercolors", "Disegno"), ("print", "Incisione"), ("prints", "Incisione"), ("fresco", "Affresco"),
        ("tapestry", "Arazzo"), ("textile", "Arazzo"), ("mosaic", "Mosaico"), ("medal", "Medaglia"), ("medals and plaquettes", "Medaglia"),
        ("manuscript", "Manoscritto miniato"), ("manuscripts and illuminations", "Manoscritto miniato"), ("miniature painting", "Dipinto"),
        ("stained glass", "Vetrata"), ("jewelry", "Oreficeria"), ("metalwork", "Oreficeria"), ("goldsmith work", "Oreficeria"),
        ("altarpiece", "Pala d'altare"), ("architecture", "Architettura"), ("architectural drawing", "Disegno"),
    ];

    // «Oil on canvas» → «olio su tela»; se una parola non è nota il testo resta in originale.
    public static string TranslateMedium(string? text)
    {
        var source = (text ?? string.Empty).Trim().TrimEnd('.');
        if (source.Length == 0)
        {
            return string.Empty;
        }

        var lower = source.ToLowerInvariant();
        var parts = new List<string>();
        var index = 0;
        while (index < lower.Length)
        {
            if (lower[index] is ' ' or ',' or ';')
            {
                if (lower[index] != ' ')
                {
                    parts.Add(lower[index].ToString());
                }

                index++;
                continue;
            }

            var match = Words.FirstOrDefault(w => string.CompareOrdinal(lower, index, w.Inglese, 0, w.Inglese.Length) == 0
                && (index + w.Inglese.Length == lower.Length || !char.IsLetter(lower[index + w.Inglese.Length])));
            if (match.Inglese is null)
            {
                return source;
            }

            parts.Add(match.Italiano);
            index += match.Inglese.Length;
        }

        return Regex.Replace(string.Join(' ', parts), @"\s+([,;])", "$1");
    }

    // «Painting», «Prints» → tipo dell'archivio; in mancanza il testo originale.
    public static string TranslateType(string? text)
    {
        var source = (text ?? string.Empty).Trim();
        var match = Types.FirstOrDefault(t => string.Equals(t.Inglese, source, StringComparison.OrdinalIgnoreCase));
        return match.Italiano ?? source;
    }

    // «ca. 1665–67» → «circa 1665–67»; «16th century» → «XVI secolo»; «1650 BCE» → «1650 a.C.».
    public static string TranslateDate(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        value = CircaRegex().Replace(value, "circa ");
        value = CenturyRegex().Replace(value, m =>
        {
            var century = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            var prefix = m.Groups["part"].Value.ToLowerInvariant() switch
            {
                "early" => "inizio ",
                "mid" or "mid-" or "middle" => "metà ",
                "late" => "fine ",
                _ => string.Empty,
            };
            return $"{prefix}{WikipediaImportService.RomanNumeral(century)} secolo";
        });
        value = Regex.Replace(value, @"\b(B\.?C\.?E?\.?)(?=\s|$)", "a.C.");
        value = Regex.Replace(value, @"\b(A\.?D\.?|C\.?E\.?)(?=\s|$)", "d.C.");
        return value.Trim();
    }

    // Prima misura metrica, escludendo cornice e supporto: «17 x 24 in. (43.2 x 61 cm)» → «43,2 × 61 cm».
    public static string ExtractMetricDimensions(string? text)
    {
        var segments = (text ?? string.Empty).Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ordered = segments
            .OrderBy(s => Regex.IsMatch(s, @"\b(framed|frame|mount|sheet|support|base|pedestal)\b", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(s, @"\bunframed\b", RegexOptions.IgnoreCase) ? 1 : 0);
        foreach (var segment in ordered)
        {
            var match = MetricRegex().Match(segment);
            if (match.Success)
            {
                var numbers = Regex.Split(match.Groups["n"].Value, @"\s*[x×]\s*").Select(n => n.Replace('.', ','));
                return $"{string.Join(" × ", numbers)} {match.Groups["u"].Value}";
            }
        }

        return string.Empty;
    }

    // Anni tra parentesi in «Claude Monet (French, 1840–1926)».
    public static (string Nome, int? Nascita, int? Morte) ParseArtistDisplay(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        var nameEnd = value.IndexOfAny(['(', '\n']);
        var name = (nameEnd > 0 ? value[..nameEnd] : value).Trim().TrimEnd(',');
        var years = Regex.Match(value, @"(?<b>\d{3,4})\s*[–-]\s*(?<d>\d{3,4})");
        return years.Success
            ? (name, int.Parse(years.Groups["b"].Value, CultureInfo.InvariantCulture), int.Parse(years.Groups["d"].Value, CultureInfo.InvariantCulture))
            : (name, null, null);
    }

    // Ruolo dei musei in inglese («Workshop of», «Attributed to») → ruolo dell'opera; null se non è un autore (es. «Follower of»).
    public static string? MapArtistRole(string? role)
    {
        var value = (role ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0 || value is "artist" or "author" or "painter" or "sculptor" or "maker" or "designer" or "creator" or "illuminator" or "draftsman" or "engraver" or "printmaker")
        {
            return OperaRuoli.Autore;
        }

        if (value.Contains("attributed"))
        {
            return OperaRuoli.Attribuita;
        }

        if (value.Contains("workshop") || value.Contains("studio"))
        {
            return OperaRuoli.Bottega;
        }

        if (value.Contains("commission") || value.Contains("patron"))
        {
            return OperaRuoli.Committente;
        }

        return null;
    }

    [GeneratedRegex(@"^\s*(ca\.|c\.|circa|about|approx\.?)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CircaRegex();

    [GeneratedRegex(@"(?<part>early|mid-?|middle|late)?\s*(?<n>\d{1,2})(st|nd|rd|th)\s+century", RegexOptions.IgnoreCase)]
    private static partial Regex CenturyRegex();

    [GeneratedRegex(@"(?<n>\d+(?:[.,]\d+)?(?:\s*[x×]\s*\d+(?:[.,]\d+)?){0,2})\s*(?<u>cm|mm|m)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetricRegex();
}
