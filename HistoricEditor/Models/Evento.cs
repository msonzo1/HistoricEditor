using System.Text.Json.Serialization;

namespace HistoricEditor;

// Evento storico (battaglia, trattato, incontro, scoperta…) a cui possono partecipare più persone.
public sealed class Evento
{
    public int Id { get; set; }
    public string Titolo { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;

    // Uno dei codici di DataEvento; Data2 è usata solo dai qualificatori a intervallo.
    public string DataQualificatore { get; set; } = DataEvento.Esatta;
    public string Data1 { get; set; } = string.Empty;
    public string Data2 { get; set; } = string.Empty;

    // Calendario e stile di inizio dell'anno delle due date (vedi HistoricalCalendar).
    public string Calendario { get; set; } = string.Empty;
    public string Stile { get; set; } = string.Empty;

    public string Descrizione { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;

    // Luogo dell'evento, come per le schede: se c'è un luogo specifico, quello amministrativo si ricava da esso.
    public int? LuogoId { get; set; }
    public int? LuogoSpecificoId { get; set; }

    [JsonIgnore]
    public Luogo? Luogo { get; set; }

    [JsonIgnore]
    public LuogoSpecifico? LuogoSpecifico { get; set; }

    public List<EventoPartecipante> Partecipanti { get; set; } = [];

    [JsonIgnore]
    public string DataTesto => DataEvento.Format(DataQualificatore, Data1, Data2) is { Length: > 0 } testo
        && HistoricalCalendar.Suffix(Calendario, Stile) is { Length: > 0 } suffisso
            ? $"{testo} ({suffisso})"
            : DataEvento.Format(DataQualificatore, Data1, Data2);

    // Anno moderno: con lo stile fiorentino o veneto le date di gennaio-marzo passano all'anno successivo.
    [JsonIgnore]
    public int? Anno => HistoricalCalendar.ModernYear(Data1, Stile) ?? HistoricalCalendar.ModernYear(Data2, Stile);
}

public sealed class EventoPartecipante
{
    public int EventoId { get; set; }

    [JsonIgnore]
    public Evento? Evento { get; set; }

    public int PersonaId { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Persona { get; set; }

    // Ruolo facoltativo nell'evento (es. «comandante», «firmatario», «vincitore»).
    public string Ruolo { get; set; } = string.Empty;
}

public sealed record DataQualificatoreInfo(string Codice, string Etichetta, bool Intervallo, string Separatore)
{
    // Testo mostrato e letto dagli screen reader per la voce della casella «Quando».
    public override string ToString() => Etichetta;
}

// Date di un evento: testo libero con un qualificatore (circa, prima, dopo, da… a…, tra… e…).
public static class DataEvento
{
    public const string Esatta = "";
    public const string Circa = "circa";
    public const string Prima = "prima";
    public const string Dopo = "dopo";
    public const string DaA = "da-a";
    public const string Tra = "tra";

    public static IReadOnlyList<DataQualificatoreInfo> Qualificatori { get; } =
    [
        new(Esatta, "Il (data precisa)", false, string.Empty),
        new(Circa, "Circa", false, string.Empty),
        new(Prima, "Prima del", false, string.Empty),
        new(Dopo, "Dopo il", false, string.Empty),
        new(DaA, "Da … a … (durata)", true, "a"),
        new(Tra, "Tra … e … (data incerta)", true, "e"),
    ];

    public static IReadOnlyList<string> TipiSuggeriti { get; } =
    [
        "Abdicazione", "Alleanza", "Assedio", "Battaglia", "Concilio", "Congiura", "Elezione",
        "Esplorazione", "Fondazione", "Guerra", "Incontro", "Incoronazione", "Invenzione",
        "Pace", "Rivolta", "Scoperta", "Trattato", "Viaggio", "Altro",
    ];

    public static DataQualificatoreInfo Info(string? codice)
        => Qualificatori.FirstOrDefault(q => q.Codice == (codice ?? string.Empty)) ?? Qualificatori[0];

    public static bool IsIntervallo(string? codice) => Info(codice).Intervallo;

    public static string Format(string? codice, string? data1, string? data2)
    {
        var d1 = (data1 ?? string.Empty).Trim();
        var d2 = (data2 ?? string.Empty).Trim();
        var info = Info(codice);
        if (d1.Length == 0 && (!info.Intervallo || d2.Length == 0))
        {
            return string.Empty;
        }

        return info.Codice switch
        {
            Circa => $"circa {d1}",
            Prima => $"prima {Articolata("di", d1)}",
            Dopo => $"dopo {Articolata(null, d1)}",
            DaA when d2.Length == 0 => $"{Articolata("da", d1)} in poi",
            DaA when d1.Length == 0 => $"fino {Articolata("a", d2)}",
            DaA => $"{Articolata("da", d1)} {Articolata("a", d2)}",
            Tra when d2.Length == 0 => $"dopo {Articolata(null, d1)}",
            Tra when d1.Length == 0 => $"prima {Articolata("di", d2)}",
            Tra => $"tra {Articolata(null, d1)} e {Articolata(null, d2)}",
            _ => d1,
        };
    }

    // Restituisce un messaggio d'errore se le date non sono coerenti con il qualificatore, altrimenti null.
    // Gli anni si confrontano in forma moderna, tenendo conto dello stile di inizio dell'anno.
    public static string? Validate(string? codice, string? data1, string? data2, string? stile = null)
    {
        var d1 = (data1 ?? string.Empty).Trim();
        var d2 = (data2 ?? string.Empty).Trim();
        if (!IsIntervallo(codice) || d1.Length == 0 || d2.Length == 0)
        {
            return null;
        }

        return HistoricalCalendar.ModernYear(d1, stile) is int y1 && HistoricalCalendar.ModernYear(d2, stile) is int y2 && y2 < y1
            ? "La seconda data precede la prima."
            : null;
    }

    // Preposizione articolata davanti a una data: «dal 1525», «dall’8 marzo», «prima dell’11 maggio».
    private static string Articolata(string? preposizione, string data)
    {
        var elisa = RichiedeElisione(data);
        var forma = preposizione switch
        {
            "da" => elisa ? "dall’" : "dal ",
            "a" => elisa ? "all’" : "al ",
            "di" => elisa ? "dell’" : "del ",
            _ => elisa ? "l’" : "il ",
        };
        return forma + data;
    }

    // Numeri che in italiano si leggono con vocale iniziale: 1 (uno), 11 (undici), 8, 80-89, 800-899… (otto…).
    private static bool RichiedeElisione(string data)
    {
        var digits = new string(data.TakeWhile(char.IsDigit).ToArray());
        return digits is "1" or "11" || (digits.Length > 0 && digits[0] == '8');
    }
}
