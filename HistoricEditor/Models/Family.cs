namespace HistoricEditor;

using System.Text.Json.Serialization;

public static class Sessi
{
    public const string Maschio = "M";
    public const string Femmina = "F";

    public static string Opposto(string sesso)
        => sesso == Maschio ? Femmina : sesso == Femmina ? Maschio : string.Empty;
}

// Matrimonio o altra unione tra due persone; una persona può averne più di una.
public sealed class Unione
{
    public int Id { get; set; }
    public int Persona1Id { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Persona1 { get; set; }
    public int Persona2Id { get; set; }

    [JsonIgnore]
    public HistoricalPerson? Persona2 { get; set; }
    public string Tipo { get; set; } = "Matrimonio";
    public string Inizio { get; set; } = string.Empty;
    public string Fine { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public sealed record PersonSummary(int Id, string Nome, string Anni, string Sesso)
{
    public string Etichetta => Anni.Length > 0 ? $"{Nome} ({Anni})" : Nome;
}

public sealed class UnionView
{
    // Null quando i figli hanno un altro genitore comune ma l'unione non è stata registrata.
    public int? UnionId { get; init; }
    public PersonSummary? Coniuge { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public string Inizio { get; init; } = string.Empty;
    public string Fine { get; init; } = string.Empty;
    public List<PersonSummary> Figli { get; } = [];

    public bool IsRegistered => UnionId is not null;
    public bool IsImplicit => UnionId is null;

    public string Intestazione
    {
        get
        {
            var periodo = Inizio.Length == 0 && Fine.Length == 0 ? string.Empty : $", {Inizio}–{Fine}";
            return $"{Coniuge?.Etichetta ?? "Coniuge non indicato"} · {Tipo}{periodo}";
        }
    }
}

public sealed record FamilyView(
    PersonSummary? Padre,
    PersonSummary? Madre,
    IReadOnlyList<UnionView> Unioni,
    IReadOnlyList<PersonSummary> AltriFigli);
