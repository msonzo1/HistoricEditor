using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class GeoSchemasTests
{
    [Theory]
    [InlineData("Italia", true)]
    [InlineData("Francia", true)]
    [InlineData("Spagna", true)]
    [InlineData("Portogallo", true)]
    [InlineData("Narnia", false)]
    [InlineData("", false)]
    public void IsKnown_RecognisesConfiguredCountries(string country, bool expected)
        => Assert.Equal(expected, GeoSchemas.IsKnown(country));

    [Fact]
    public void For_ReturnsItalianSchema()
        => Assert.Equal(new[] { "Stato", "Regione", "Provincia", "Comune" }, GeoSchemas.For("Italia"));

    [Fact]
    public void For_ReturnsPortugueseSchema()
        => Assert.Equal(new[] { "Stato", "Regione", "Distretto", "Comune" }, GeoSchemas.For("Portogallo"));

    [Theory]
    [InlineData("Regione autonoma di Madera")]
    [InlineData("Azzorre")]
    public void HintsNoLowerLevel_IsTrue_ForPortugueseAutonomousRegions(string name)
        => Assert.True(GeoSchemas.HintsNoLowerLevel("Portogallo", "Regione", name));

    [Fact]
    public void For_ReturnsFallback_ForUnknownCountry()
        => Assert.Equal(GeoSchemas.Fallback, GeoSchemas.For("Narnia"));

    [Fact]
    public void HintsNoLowerLevel_IsTrue_ForMadrid()
        => Assert.True(GeoSchemas.HintsNoLowerLevel("Spagna", "Comunità autonoma", "Comunità di Madrid"));

    [Fact]
    public void HintsNoLowerLevel_IsFalse_ForOrdinaryRegion()
        => Assert.False(GeoSchemas.HintsNoLowerLevel("Italia", "Regione", "Lazio"));

    [Fact]
    public void HintsNoLowerLevel_IsFalse_ForEmptyName()
        => Assert.False(GeoSchemas.HintsNoLowerLevel("Spagna", "Comunità autonoma", ""));
}
