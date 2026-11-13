using System.Text.Json.Serialization;

namespace HistoricEditor;

// Dinastia o casata; ParentId indica la dinastia di cui è un ramo (es. Asburgo di Spagna → Asburgo).
public sealed class Dinastia
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int? ParentId { get; set; }

    [JsonIgnore]
    public Dinastia? Parent { get; set; }

    public string WikidataId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    // Vuoto = dinastia o casata; «gens» = gens romana (vedi TipiDinastia).
    public string Tipo { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsGens => Tipo == TipiDinastia.Gens;
}

public static class TipiDinastia
{
    public const string Dinastia = "";
    public const string Gens = "gens";
}
