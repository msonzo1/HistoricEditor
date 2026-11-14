using System.Text.Json.Serialization;

namespace HistoricEditor;

// Dinastia o casata; ParentId indica la dinastia di cui è un ramo (es. Asburgo di Spagna → Asburgo).
public sealed class Dinastia
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    // Nome latino (2026-11-14): «gens Iulia» per «gens Giulia», «Iulii Caesares». Atteso per le gens e i loro rami,
    // facoltativo per le altre dinastie.
    public string NomeLatino { get; set; } = string.Empty;

    public int? ParentId { get; set; }

    [JsonIgnore]
    public Dinastia? Parent { get; set; }

    public string WikidataId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    // Vuoto = dinastia o casata; «gens» = gens romana (vedi TipiDinastia).
    public string Tipo { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsGens => Tipo == TipiDinastia.Gens;

    // «gens Giulia (gens Iulia)»; solo il nome se il latino manca o è uguale.
    [JsonIgnore]
    public string NomeCompleto => DisplayName(Nome, NomeLatino);

    // Nomi di un campo (separati da «;») con il nome latino delle dinastie registrate: «gens Giulia (gens Iulia); Iulii Caesares».
    public static string WithLatinNames(string? text, IReadOnlyList<Dinastia> dynasties)
        => string.Join("; ", PeopleGrouping.FamilyNames(text ?? string.Empty).Select(n => Find(dynasties, n)?.NomeCompleto ?? n));

    public static Dinastia? Find(IReadOnlyList<Dinastia> dynasties, string name)
        => dynasties.FirstOrDefault(d => PeopleGrouping.FamilyComparer.Equals(d.Nome.Trim(), name.Trim()));

    public static string DisplayName(string nome, string? nomeLatino)
    {
        var latin = (nomeLatino ?? string.Empty).Trim();
        return latin.Length == 0 || string.Equals(latin, nome.Trim(), StringComparison.OrdinalIgnoreCase) ? nome.Trim() : $"{nome.Trim()} ({latin})";
    }
}

public static class TipiDinastia
{
    public const string Dinastia = "";
    public const string Gens = "gens";
}
