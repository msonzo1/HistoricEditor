using Xunit;

namespace HistoricEditor.Tests;

public class ImportProgressTests
{
    private sealed class Collector : IProgress<ImportProgress>
    {
        public List<ImportProgress> Items { get; } = [];
        public void Report(ImportProgress value) => Items.Add(value);
    }

    [Theory]
    [InlineData(1, 8, 0, 0)]
    [InlineData(3, 8, 0.5, 31.25)]
    [InlineData(8, 8, 1, 100)]
    [InlineData(2, 4, 3, 50)]
    public void Percent_CombinesStepAndFraction(int step, int total, double fraction, double expected)
    {
        Assert.Equal(expected, new ImportProgress(step, total, "x", fraction).Percent, 3);
    }

    [Fact]
    public void Text_ShowsStepAndDetail()
    {
        Assert.Equal("Passo 3 di 8 · Lettura (2 di 5 edizioni lette)", new ImportProgress(3, 8, "Lettura", 0.4, "2 di 5 edizioni lette").Text);
        Assert.Equal("Passo 1 di 9 · Ricerca", new ImportProgress(1, 9, "Ricerca").Text);
    }

    [Fact]
    public void Reporter_ShiftsStepsByOffsetAndKeepsMessageForSubProgress()
    {
        var collector = new Collector();
        var reporter = new ImportProgressReporter(collector, offset: 1);

        reporter.Step(3, "Edizioni");
        reporter.Sub(0.5, "1 di 2");

        Assert.Equal(9, reporter.TotalSteps);
        Assert.Equal(new ImportProgress(4, 9, "Edizioni"), collector.Items[0]);
        Assert.Equal(new ImportProgress(4, 9, "Edizioni", 0.5, "1 di 2"), collector.Items[1]);
    }
}
