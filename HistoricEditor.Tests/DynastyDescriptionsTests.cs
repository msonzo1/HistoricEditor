using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class DynastyDescriptionsTests
{
    private static readonly Dinastia[] Dynasties =
    [
        new() { Id = 1, Nome = "gens Flavia", NomeLatino = "gens Flavia", Tipo = TipiDinastia.Gens },
        new() { Id = 2, Nome = "Flavii Sabini", NomeLatino = "Flavii Sabini", ParentId = 1, Tipo = TipiDinastia.Gens },
        new() { Id = 3, Nome = "Casa d'Asburgo" },
        new() { Id = 4, Nome = "Asburgo di Spagna", ParentId = 3 },
        new() { Id = 5, Nome = "Lorena" },
    ];

    // La gens del ramo non si ripete (2026-11-14): «gens Flavia; Flavii Sabini» → «gens Flavia › Flavii Sabini».
    [Theory]
    [InlineData("gens Flavia; Flavii Sabini", "gens Flavia › Flavii Sabini")]
    [InlineData("Flavii Sabini", "gens Flavia › Flavii Sabini")]
    [InlineData("gens Flavia", "gens Flavia")]
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
        var (path, note) = DynastyDescriptions.Summarize(Dynasties, "gens Annia", gens: true);
        Assert.Equal("gens Annia", path);
        Assert.Equal("Nuova gens, da registrare al salvataggio", note);
        Assert.Equal("Nuova gens, da registrare al salvataggio: gens Annia", DynastyDescriptions.Summarize(Dynasties, "gens Flavia; gens Annia", gens: true).Note);
        Assert.Equal("Nuova gens, da registrare al salvataggio", DynastyDescriptions.Hint(Dynasties, "gens Annia", gens: true));
        Assert.Equal("gens Flavia › Flavii Sabini", DynastyDescriptions.Hint(Dynasties, "Flavii Sabini", gens: true));
    }

    // Nome latino accanto a quello italiano (2026-11-14); una gens senza nome latino si segnala, una dinastia no.
    [Fact]
    public void Summarize_ShowsLatinNamesAndNotesMissingOnes()
    {
        Dinastia[] dynasties =
        [
            new() { Id = 1, Nome = "gens Giulia", NomeLatino = "gens Iulia", Tipo = TipiDinastia.Gens },
            new() { Id = 2, Nome = "Iulii Caesares", NomeLatino = "Iulii Caesares", ParentId = 1, Tipo = TipiDinastia.Gens },
            new() { Id = 3, Nome = "gens Ottavia", Tipo = TipiDinastia.Gens },
            new() { Id = 4, Nome = "Casa d'Asburgo" },
        ];
        Assert.Equal("gens Giulia (gens Iulia) › Iulii Caesares", DynastyDescriptions.Summarize(dynasties, "gens Giulia; Iulii Caesares", gens: true).Path);
        Assert.Equal("gens Giulia (gens Iulia)", DynastyDescriptions.Hint(dynasties, "gens Giulia", gens: true));
        Assert.Equal(string.Empty, DynastyDescriptions.MissingLatin(dynasties, "Iulii Caesares; Casa d'Asburgo"));
        Assert.Equal("Nome latino da completare nell'archivio delle dinastie: gens Ottavia", DynastyDescriptions.MissingLatin(dynasties, "gens Ottavia"));
        Assert.Equal("gens Giulia (gens Iulia); Casa d'Asburgo; gens Nuova", Dinastia.WithLatinNames("gens Giulia; Casa d'Asburgo; gens Nuova", dynasties));
        Assert.Equal("Dinastia…", DynastyMenu.ButtonText("Dinastia…", "Casa d'Asburgo"));
        Assert.Equal("Gens… ▾", DynastyMenu.ButtonText("Gens…", "gens Giulia; Iulii Caesares"));
    }

    // Nome latino delle famiglie dalle etichette incoerenti di Wikidata (2026-11-14).
    [Theory]
    [InlineData("Iulii", "", "Julia gens", WikipediaImportService.LatinFamilyKind.Gens, "gens Iulia")]
    [InlineData("gens Octavia", "", "Octavia gens", WikipediaImportService.LatinFamilyKind.Gens, "gens Octavia")]
    [InlineData("", "", "Julia gens", WikipediaImportService.LatinFamilyKind.Gens, "gens Iulia")]
    [InlineData("Julii Caesares", "", "Julii Caesares", WikipediaImportService.LatinFamilyKind.Branch, "Iulii Caesares")]
    [InlineData("", "Flavii Sabini", "Flavii Sabini", WikipediaImportService.LatinFamilyKind.Branch, "Flavii Sabini")]
    [InlineData("Domus Iulio-Claudia", "", "Julio-Claudian dynasty", WikipediaImportService.LatinFamilyKind.Dynasty, "Domus Iulio-Claudia")]
    [InlineData("Gens Flavia", "", "Flavian dynasty", WikipediaImportService.LatinFamilyKind.Dynasty, "")]
    [InlineData("", "", "Nerva–Antonine dynasty", WikipediaImportService.LatinFamilyKind.Dynasty, "")]
    public void LatinFamilyName_NormalizesWikidataLabels(string la, string mul, string en, WikipediaImportService.LatinFamilyKind kind, string expected)
        => Assert.Equal(expected, WikipediaImportService.LatinFamilyName(new Dictionary<string, string> { ["la"] = la, ["mul"] = mul, ["en"] = en }, kind));
}
