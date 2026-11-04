namespace HistoricEditor;

// Titolo o carica ricoperta dal personaggio; date e cause sono testo libero (date storiche incerte).
public sealed class PersonTitle
{
    public string Titolo { get; set; } = string.Empty;
    public string Numerazione { get; set; } = string.Empty;
    public string Inizio { get; set; } = string.Empty;
    public string CausaInizio { get; set; } = string.Empty;
    public string Fine { get; set; } = string.Empty;
    public string CausaFine { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;

    // Predecessore e successore nella carica (Wikidata P1365/P1366); il codice Wikidata, se noto, serve ad abbinarli alle schede.
    public string Predecessore { get; set; } = string.Empty;
    public string PredecessoreWikidataId { get; set; } = string.Empty;
    public string Successore { get; set; } = string.Empty;
    public string SuccessoreWikidataId { get; set; } = string.Empty;

    // Date di inizio/fine riportate da ciascuna fonte (es. le singole affermazioni Wikidata unite in questo titolo).
    public List<TitleDateSource> FontiDate { get; set; } = [];

    public PersonTitle Clone()
    {
        var clone = (PersonTitle)MemberwiseClone();
        clone.FontiDate = FontiDate.Select(s => s.Clone()).ToList();
        return clone;
    }

    public bool IsEmpty() => new[] { Titolo, Numerazione, Inizio, CausaInizio, Fine, CausaFine }.All(string.IsNullOrWhiteSpace);

    public static readonly IReadOnlyList<string> StartCauses =
    [
        "Nascita", "Morte del padre", "Morte della madre", "Morte del predecessore", "Successione ereditaria",
        "Abdicazione del predecessore", "Elezione", "Nomina", "Matrimonio", "Incoronazione",
        "Vittoria in battaglia", "Conquista", "Colpo di stato", "Trattato",
    ];

    public static readonly IReadOnlyList<string> EndCauses =
    [
        "Morte", "Abdicazione", "Deposizione", "Sconfitta in battaglia", "Colpo di stato",
        "Rinuncia", "Fine del mandato", "Annessione", "Trattato", "Esilio", "Promozione",
    ];
}

// Date di un titolo secondo una singola fonte.
public sealed class TitleDateSource
{
    public string Fonte { get; set; } = string.Empty;
    public string Inizio { get; set; } = string.Empty;
    public string Fine { get; set; } = string.Empty;

    public TitleDateSource Clone() => (TitleDateSource)MemberwiseClone();
}
