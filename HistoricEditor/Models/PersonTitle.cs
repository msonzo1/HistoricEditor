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

    public PersonTitle Clone() => (PersonTitle)MemberwiseClone();

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
