using System.Text.Json.Serialization;

namespace HistoricEditor;

// Opera d'arte (dipinto, scultura, affresco…) collegata ad artisti, committenti e personaggi raffigurati.
public sealed class Opera
{
    public int Id { get; set; }
    public string Titolo { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;

    // Testo libero: «1503-1519», «circa 1490», «XV secolo».
    public string Data { get; set; } = string.Empty;
    public string Tecnica { get; set; } = string.Empty;
    public string Dimensioni { get; set; } = string.Empty;

    // Collocazione attuale (museo, chiesa…), come i luoghi delle schede.
    public int? LuogoId { get; set; }
    public int? LuogoSpecificoId { get; set; }

    [JsonIgnore]
    public Luogo? Luogo { get; set; }

    [JsonIgnore]
    public LuogoSpecifico? LuogoSpecifico { get; set; }

    public string Note { get; set; } = string.Empty;

    // Una fonte per riga; gli indirizzi web diventano collegamenti nella scheda di lettura.
    public string Fonti { get; set; } = string.Empty;

    // Nome del file compresso nella cartella «Immagini» dell'archivio (vedi ArtworkImages); vuoto se assente.
    public string Immagine { get; set; } = string.Empty;

    // Elemento Wikidata dell'opera (es. Q12418), usato per riconoscerla quando viene reimportata.
    public string WikidataId { get; set; } = string.Empty;

    public List<OperaPersona> Persone { get; set; } = [];
}

public sealed class OperaPersona
{
    public int OperaId { get; set; }

    [JsonIgnore]
    public Opera? Opera { get; set; }

    public int PersonaId { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Persona { get; set; }

    // Uno dei codici di OperaRuoli; la stessa persona può avere più ruoli (es. autoritratto: autore e soggetto).
    public string Ruolo { get; set; } = OperaRuoli.Autore;
}

public sealed record OperaRuoloInfo(string Codice, string Etichetta, string Preposizione)
{
    public override string ToString() => Etichetta;
}

public static class OperaRuoli
{
    public const string Autore = "autore";
    public const string Bottega = "bottega";
    public const string Attribuita = "attribuita";
    public const string Committente = "committente";
    public const string Soggetto = "soggetto";

    public static IReadOnlyList<OperaRuoloInfo> Tutti { get; } =
    [
        new(Autore, "Autore", string.Empty),
        new(Bottega, "Bottega di", "bottega di"),
        new(Attribuita, "Attribuita a", "attribuita a"),
        new(Committente, "Committente", string.Empty),
        new(Soggetto, "Soggetto raffigurato", string.Empty),
    ];

    public static IReadOnlyList<string> TipiSuggeriti { get; } =
    [
        "Affresco", "Arazzo", "Architettura", "Disegno", "Dipinto", "Incisione", "Manoscritto miniato",
        "Medaglia", "Mosaico", "Oreficeria", "Pala d'altare", "Ritratto", "Scultura", "Vetrata", "Altro",
    ];

    public static OperaRuoloInfo Info(string? codice)
        => Tutti.FirstOrDefault(r => r.Codice == (codice ?? string.Empty).Trim()) ?? Tutti[0];

    public static bool IsValid(string? codice) => Tutti.Any(r => r.Codice == (codice ?? string.Empty).Trim());

    // Ruoli che indicano chi ha realizzato l'opera.
    public static bool IsArtista(string? codice) => codice is Autore or Bottega or Attribuita;

    // «La Gioconda — Leonardo da Vinci · 1503-1519 · Dipinto (soggetto raffigurato)»: l'opera vista da una persona collegata.
    public static string DescribeForPerson(Opera opera, int personId, Func<int, string> nameOf)
    {
        var artists = DescribeArtists(opera.Persone.Select(p => (p.Ruolo, nameOf(p.PersonaId))));
        var details = string.Join(" · ", new[] { artists, opera.Data.Trim(), opera.Tipo.Trim() }.Where(s => s.Length > 0));
        var roles = string.Join(", ", opera.Persone
            .Where(p => p.PersonaId == personId && IsValid(p.Ruolo))
            .Select(p => Info(p.Ruolo))
            .Distinct()
            .OrderBy(r => Tutti.ToList().IndexOf(r))
            .Select(r => r.Codice switch
            {
                Bottega => "bottega",
                Attribuita => "attribuita",
                _ => r.Etichetta.ToLower(System.Globalization.CultureInfo.CurrentCulture),
            }));
        var text = details.Length > 0 ? $"{opera.Titolo} — {details}" : opera.Titolo;
        return roles.Length > 0 ? $"{text} ({roles})" : text;
    }

    // «Leonardo da Vinci, bottega di Verrocchio, attribuita a Lorenzo di Credi»: solo chi ha realizzato l'opera.
    public static string DescribeArtists(IEnumerable<(string Ruolo, string Nome)> persone)
        => string.Join(", ", persone
            .Where(p => IsArtista(p.Ruolo) && !string.IsNullOrWhiteSpace(p.Nome))
            .OrderBy(p => Tutti.ToList().FindIndex(r => r.Codice == p.Ruolo))
            .Select(p => Info(p.Ruolo).Preposizione is { Length: > 0 } prep ? $"{prep} {p.Nome.Trim()}" : p.Nome.Trim()));
}
