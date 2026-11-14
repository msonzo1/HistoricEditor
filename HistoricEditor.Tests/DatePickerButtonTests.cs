using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Editor della data nel calendario accanto ai campi (2026-11-14).
public class DatePickerButtonTests
{
    [Fact]
    public void Editor_ComposesEveryKindOfDate()
    {
        WpfTestThread.Run(() =>
        {
            var picker = new DatePickerButton();
            Assert.Equal("12/07/100 a.C.; 13/07/100 a.C.",
                picker.ResultFor(null, UncertainDateKind.Alternatives, [new DatePoint(-100, 7, 12), new DatePoint(-100, 7, 13)]));
            Assert.Equal("tra 1503 e 1519", picker.ResultFor(null, UncertainDateKind.Between, [new DatePoint(1503), new DatePoint(1519)]));
            Assert.Equal("c. XV secolo", picker.ResultFor(null, UncertainDateKind.Exact, [new DatePoint(null, Century: 15)], circa: true));
            Assert.Equal("prima di 03/1500", picker.ResultFor(null, UncertainDateKind.Before, [new DatePoint(1500, 3)]));
            Assert.Equal("dopo il 1500", picker.ResultFor(null, UncertainDateKind.After, [new DatePoint(1500)]));

            // Con «tra» servono due date; con «più date» una sola vale come data semplice.
            Assert.Null(picker.ResultFor(null, UncertainDateKind.Between, [new DatePoint(1503)]));
            Assert.Equal("1540", picker.ResultFor(null, UncertainDateKind.Alternatives, [new DatePoint(1540)]));
        });
    }
}
