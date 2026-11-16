using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class DynastyDescriptionsTests
{
    private static readonly Dinastia[] Dynasties =
    [
        new() { Id = 1, Nome = "Gens Flavia", NomeLatino = "Gens Flavia", Tipo = TipiDinastia.Gens },
        new() { Id = 2, Nome = "Flavii Sabini", NomeLatino = "Flavii Sabini", ParentId = 1, Tipo = TipiDinastia.Gens },
        new() { Id = 3, Nome = "Casa d'Asburgo" },
        new() { Id = 4, Nome = "Asburgo di Spagna", ParentId = 3 },
        new() { Id = 5, Nome = "Lorena" },
    ];

    // La gens del ramo non si ripete (2026-11-14): «Gens Flavia; Flavii Sabini» → «Gens Flavia › Flavii Sabini».
    [Theory]
    [InlineData("Gens Flavia; Flavii Sabini", "Gens Flavia › Flavii Sabini")]
    [InlineData("Flavii Sabini", "Gens Flavia › Flavii Sabini")]
    [InlineData("Gens Flavia", "Gens Flavia")]
    [InlineData("Asburgo di Spagna; Casa d'Asburgo", "Casa d'Asburgo › Asburgo di Spagna")]
    [InlineData("Casa d'Asburgo; Lorena", "Casa d'Asburgo · Lorena")]
    [InlineData("", "")]
    public void Summarize_ShowsEachFamilyOnceAsPath(string text, string expected)
    {
        var (path, note) = DynastyDescriptions.Summarize(Dynasties, text, gens: true);
        Assert.Equal(expected, path);
        Assert.Equal(string.Empty, note);
    }

    [Fact]
    public void Summarize_NotesUnregisteredFamilies()
    {
        var (path, note) = DynastyDescriptions.Summarize(Dynasties, "Gens Annia", gens: true);
        Assert.Equal("Gens Annia", path);
        Assert.Equal("Nuova gens, da registrare al salvataggio", note);
        Assert.Equal("Nuova gens, da registrare al salvataggio: Gens Annia", DynastyDescriptions.Summarize(Dynasties, "Gens Flavia; Gens Annia", gens: true).Note);
        Assert.Equal("Nuova gens, da registrare al salvataggio", DynastyDescriptions.Hint(Dynasties, "Gens Annia", gens: true));
        Assert.Equal("Gens Flavia › Flavii Sabini", DynastyDescriptions.Hint(Dynasties, "Flavii Sabini", gens: true));
    }

    // Nome latino accanto a quello italiano (2026-11-14); una gens senza nome latino si segnala, una dinastia no.
    [Fact]
    public void Summarize_ShowsLatinNamesAndNotesMissingOnes()
    {
        Dinastia[] dynasties =
        [
            new() { Id = 1, Nome = "Gens Giulia", NomeLatino = "Gens Iulia", Tipo = TipiDinastia.Gens },
            new() { Id = 2, Nome = "Iulii Caesares", NomeLatino = "Iulii Caesares", ParentId = 1, Tipo = TipiDinastia.Gens },
            new() { Id = 3, Nome = "Gens Ottavia", Tipo = TipiDinastia.Gens },
            new() { Id = 4, Nome = "Casa d'Asburgo" },
        ];
        Assert.Equal("Gens Giulia (Gens Iulia) › Iulii Caesares", DynastyDescriptions.Summarize(dynasties, "Gens Giulia; Iulii Caesares", gens: true).Path);
        Assert.Equal("Gens Giulia (Gens Iulia)", DynastyDescriptions.Hint(dynasties, "Gens Giulia", gens: true));
        Assert.Equal(string.Empty, DynastyDescriptions.MissingLatin(dynasties, "Iulii Caesares; Casa d'Asburgo"));
        Assert.Equal("Nome latino da completare nell'archivio delle dinastie: Gens Ottavia", DynastyDescriptions.MissingLatin(dynasties, "Gens Ottavia"));
        Assert.Equal("Gens Giulia (Gens Iulia); Casa d'Asburgo; Gens Nuova", Dinastia.WithLatinNames("Gens Giulia; Casa d'Asburgo; Gens Nuova", dynasties));
        // La matita della dinastia (2026-11-17): con più nomi il suggerimento dice che si sceglie dal menu.
        Assert.DoesNotContain("menu", DynastyMenu.ButtonToolTip("Casa d'Asburgo"));
        Assert.EndsWith("si sceglie dal menu", DynastyMenu.ButtonToolTip("Asburgo; Lorena"));
    }

    // Nome latino delle famiglie dalle etichette incoerenti di Wikidata (2026-11-14).
    [Theory]
    [InlineData("Iulii", "", "Julia gens", WikipediaImportService.LatinFamilyKind.Gens, "Gens Iulia")]
    [InlineData("Gens Octavia", "", "Octavia gens", WikipediaImportService.LatinFamilyKind.Gens, "Gens Octavia")]
    [InlineData("", "", "Julia gens", WikipediaImportService.LatinFamilyKind.Gens, "Gens Iulia")]
    [InlineData("Julii Caesares", "", "Julii Caesares", WikipediaImportService.LatinFamilyKind.Branch, "Iulii Caesares")]
    [InlineData("", "Flavii Sabini", "Flavii Sabini", WikipediaImportService.LatinFamilyKind.Branch, "Flavii Sabini")]
    [InlineData("Domus Iulio-Claudia", "", "Julio-Claudian dynasty", WikipediaImportService.LatinFamilyKind.Dynasty, "Domus Iulio-Claudia")]
    [InlineData("Gens Flavia", "", "Flavian dynasty", WikipediaImportService.LatinFamilyKind.Dynasty, "")]
    [InlineData("", "", "Nerva–Antonine dynasty", WikipediaImportService.LatinFamilyKind.Dynasty, "")]
    public void LatinFamilyName_NormalizesWikidataLabels(string la, string mul, string en, WikipediaImportService.LatinFamilyKind kind, string expected)
        => Assert.Equal(expected, WikipediaImportService.LatinFamilyName(new Dictionary<string, string> { ["la"] = la, ["mul"] = mul, ["en"] = en }, kind));
}
