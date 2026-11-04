using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class TitleTranslatorTests
{
    [Theory]
    [InlineData("Monarch of England", "Regno d'Inghilterra", Sessi.Maschio, "Re d'Inghilterra")]
    [InlineData("Monarch of England", "Regno d'Inghilterra", Sessi.Femmina, "Regina d'Inghilterra")]
    [InlineData("King of Ireland", "Regno d'Irlanda", Sessi.Maschio, "Re d'Irlanda")]
    [InlineData("Duke of Milan", "Ducato di Milano", Sessi.Maschio, "Duca di Milano")]
    [InlineData("King of France", "Francia", Sessi.Maschio, "Re di Francia")]
    [InlineData("Archduke of Austria", "Arciducato d'Austria", Sessi.Maschio, "Arciduca d'Austria")]
    [InlineData("King of Granada", "Kingdom of Granada", Sessi.Maschio, "Re di Granada")]
    public void Translate_BuildsItalianTitle(string english, string territory, string sesso, string expected)
        => Assert.Equal(expected, TitleTranslator.Translate(english, territory, sesso));

    [Fact]
    public void Translate_UnknownPattern_ReturnsNull()
        => Assert.Null(TitleTranslator.Translate("Lord Warden of the Marches", "Inghilterra", Sessi.Maschio));
}
