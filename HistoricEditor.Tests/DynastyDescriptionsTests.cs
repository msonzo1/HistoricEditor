using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class DynastyDescriptionsTests
{
    private static readonly Dinastia[] Dynasties =
    [
        new() { Id = 1, Nome = "gens Flavia", Tipo = TipiDinastia.Gens },
        new() { Id = 2, Nome = "Flavii Sabini", ParentId = 1, Tipo = TipiDinastia.Gens },
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
}
