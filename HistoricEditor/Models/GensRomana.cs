using System.Text.Json.Serialization;

namespace HistoricEditor;

// Gens romana (2026-11-15): la stirpe, separata dai suoi rami e dalle dinastie. Si salvano le forme latine così come sono
// scritte, senza ricavarle con regole di declinazione: il nome («Iulia»), il nomen maschile singolare («Iulius») e il nome
// plurale («Iulii»); in italiano, facoltativi, il nome («Giulia») e il plurale («Giulii»). Le etichette composte («Gens
// Iulia», «Iulii Caesares») non si salvano mai: si ricavano da questi campi (GensLabels).
public sealed class GensRomana
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Nomen { get; set; } = string.Empty;
    public string Plurale { get; set; } = string.Empty;
    public string NomeItaliano { get; set; } = string.Empty;
    public string PluraleItaliano { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    [JsonIgnore]
    public List<RamoGens> Rami { get; set; } = [];

    // «Gens Giulia», o in latino se manca il nome italiano.
    [JsonIgnore]
    public string Etichetta => GensLabels.Gens(this);

    // «Gens Iulia».
    [JsonIgnore]
    public string EtichettaLatina => GensLabels.Gens(this, latin: true);

    // Forme da completare (il nomen e il plurale sono attesi): vuoto se c'è tutto.
    [JsonIgnore]
    public string DaCompletare => string.Join(", ", new[] { (Nomen, "nomen"), (Plurale, "plurale") }.Where(f => f.Item1.Trim().Length == 0).Select(f => f.Item2));
}

// Ramo (familia, stirps) di una gens: i Caesares della gens Iulia. Si salvano il cognomen singolare, facoltativo
// («Caesar»), e il nome plurale («Caesares»); in italiano, facoltativo, il plurale («Cesari»). «Iulii Caesares» si ricava
// dal plurale della gens e da quello del ramo.
public sealed class RamoGens
{
    public int Id { get; set; }
    public int GensId { get; set; }

    [JsonIgnore]
    public GensRomana? Gens { get; set; }

    public string Cognomen { get; set; } = string.Empty;
    public string Plurale { get; set; } = string.Empty;
    public string NomeItaliano { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    // «Giulii Cesari», o in latino dove manca l'italiano: serve la gens caricata.
    [JsonIgnore]
    public string Etichetta => GensLabels.Branch(Gens, this);

    [JsonIgnore]
    public string EtichettaLatina => GensLabels.Branch(Gens, this, latin: true);
}

// Etichette ricavate dai campi di gens e rami (2026-11-15).
public static class GensLabels
{
    private static string Pick(string italian, string latin, bool useLatin)
        => !useLatin && italian.Trim().Length > 0 ? italian.Trim() : latin.Trim();

    // «Gens Giulia» / «Gens Iulia»; vuoto senza nome.
    public static string Gens(GensRomana? gens, bool latin = false)
        => gens is null || Pick(gens.NomeItaliano, gens.Nome, latin) is not { Length: > 0 } name ? string.Empty : $"Gens {name}";

    // «Giulii Cesari» / «Iulii Caesares»: plurale della gens e plurale del ramo; senza il plurale della gens, solo il ramo.
    public static string Branch(GensRomana? gens, RamoGens? branch, bool latin = false)
    {
        if (branch is null)
        {
            return string.Empty;
        }

        var gensPlural = gens is null ? string.Empty : latin ? gens.Plurale.Trim() : Pick(gens.PluraleItaliano, gens.Plurale, false);
        var branchPlural = Pick(branch.NomeItaliano, branch.Plurale, latin);
        if (branchPlural.Length == 0)
        {
            branchPlural = branch.Cognomen.Trim();
        }

        return string.Join(' ', new[] { gensPlural, branchPlural }.Where(p => p.Length > 0));
    }

    // Gens ed eventuale ramo di una scheda: «Gens Giulia › Giulii Cesari».
    public static string Path(GensRomana? gens, RamoGens? branch, bool latin = false)
    {
        var owner = branch?.Gens ?? gens;
        return string.Join(" › ", new[] { Gens(owner, latin), Branch(owner, branch, latin) }.Where(p => p.Length > 0));
    }

    // Percorso con il latino tra parentesi dove è diverso: «Gens Giulia (Gens Iulia) › Giulii Cesari (Iulii Caesares)».
    public static string PathWithLatin(GensRomana? gens, RamoGens? branch)
    {
        var owner = branch?.Gens ?? gens;
        static string Both(string italian, string latin)
            => latin.Length == 0 || string.Equals(italian, latin, StringComparison.OrdinalIgnoreCase) ? italian : $"{italian} ({latin})";
        return string.Join(" › ", new[]
        {
            Both(Gens(owner), Gens(owner, latin: true)),
            Both(Branch(owner, branch), Branch(owner, branch, latin: true)),
        }.Where(p => p.Length > 0));
    }
}
