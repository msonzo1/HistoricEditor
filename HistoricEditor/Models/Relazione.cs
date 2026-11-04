using System.Text.Json.Serialization;

namespace HistoricEditor;

// Relazione non familiare tra due personaggi (alleanza, rivalità, mecenatismo, tutela, maestro/allievo…).
// Nelle relazioni con un verso Persona1 è la parte «attiva»: il maestro, il mecenate, il tutore, il reggente.
public sealed class Relazione
{
    public int Id { get; set; }
    public int Persona1Id { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Persona1 { get; set; }
    public int Persona2Id { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Persona2 { get; set; }

    // Nome di uno dei tipi di Relazioni.Tipi oppure testo libero.
    public string Tipo { get; set; } = string.Empty;
    public string Inizio { get; set; } = string.Empty;
    public string Fine { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

// Forme maschili e femminili di come la relazione si legge dal punto di vista di ciascuna parte.
public sealed record RelazioneTipo(string Nome, string Attivo, string AttivoF, string Passivo, string PassivoF)
{
    public bool Simmetrica => Attivo == Passivo && AttivoF == PassivoF;
}

// Voce della casella «Relazione»: tipo e verso dal punto di vista della persona della scheda.
public sealed record RelazioneScelta(string Etichetta, string Tipo, bool Attivo)
{
    public override string ToString() => Etichetta;
}

public sealed record RelationView(int Id, PersonSummary Altro, string Etichetta, string Inizio, string Fine, string Note)
{
    public bool HasNote => Note.Length > 0;

    public string Descrizione
    {
        get
        {
            var periodo = Inizio.Length == 0 && Fine.Length == 0 ? string.Empty : $" · {Inizio}–{Fine}";
            return $"{Etichetta} {Altro.Etichetta}{periodo}";
        }
    }
}

public static class Relazioni
{
    public static IReadOnlyList<RelazioneTipo> Tipi { get; } =
    [
        new("Alleanza", "Alleato di", "Alleata di", "Alleato di", "Alleata di"),
        new("Rivalità", "Rivale di", "Rivale di", "Rivale di", "Rivale di"),
        new("Amicizia", "Amico di", "Amica di", "Amico di", "Amica di"),
        new("Mecenatismo", "Mecenate di", "Mecenate di", "Protetto di", "Protetta di"),
        new("Tutela", "Tutore di", "Tutrice di", "Sotto la tutela di", "Sotto la tutela di"),
        new("Reggenza", "Reggente per", "Reggente per", "Sotto la reggenza di", "Sotto la reggenza di"),
        new("Maestro e allievo", "Maestro di", "Maestra di", "Allievo di", "Allieva di"),
        new("Influenza", "Ha influenzato", "Ha influenzato", "Influenzato da", "Influenzata da"),
    ];

    public static RelazioneTipo? Find(string? tipo)
        => Tipi.FirstOrDefault(t => string.Equals(t.Nome, tipo?.Trim(), StringComparison.CurrentCultureIgnoreCase));

    // Come si legge la relazione per la persona indicata: «Maestro di», «Allieva di»… I tipi liberi restano come scritti.
    public static string Etichetta(string tipo, bool attivo, string sesso)
    {
        if (Find(tipo) is not { } info)
        {
            return $"{tipo.Trim()}:";
        }

        var femminile = sesso == Sessi.Femmina;
        return attivo ? (femminile ? info.AttivoF : info.Attivo) : (femminile ? info.PassivoF : info.Passivo);
    }

    // Voci proposte nella casella: una per i tipi simmetrici, due (una per verso) per gli altri.
    public static IReadOnlyList<RelazioneScelta> Scelte(string sesso)
    {
        var scelte = new List<RelazioneScelta>();
        foreach (var tipo in Tipi)
        {
            scelte.Add(new RelazioneScelta(Etichetta(tipo.Nome, true, sesso), tipo.Nome, true));
            if (!tipo.Simmetrica)
            {
                scelte.Add(new RelazioneScelta(Etichetta(tipo.Nome, false, sesso), tipo.Nome, false));
            }
        }

        return scelte;
    }

    // Interpreta il testo della casella: una delle voci proposte (anche nella forma dell'altro genere) o un tipo libero.
    public static RelazioneScelta? Interpreta(string? testo, string sesso)
    {
        var value = (testo ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return null;
        }

        var scelta = Scelte(sesso).Concat(Scelte(Sessi.Opposto(sesso) is { Length: > 0 } altro ? altro : Sessi.Femmina))
            .FirstOrDefault(s => string.Equals(s.Etichetta, value, StringComparison.CurrentCultureIgnoreCase));
        if (scelta is not null)
        {
            return scelta;
        }

        return Find(value) is { } tipo ? new RelazioneScelta(Etichetta(tipo.Nome, true, sesso), tipo.Nome, true) : new RelazioneScelta(value, value, true);
    }
}
