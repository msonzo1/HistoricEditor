using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HistoricEditor;

/// <summary>
/// Led delle date di un titolo: dal titolo (con le sue fonti) ricava visibilità, colore, dettagli e nome accessibile.
/// Il parametro indica cosa restituire: Visibility, Fill, Details o Name.
/// </summary>
public sealed class TitleDateLedConverter : IValueConverter
{
    private static readonly Brush Orange = CreateOrange();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var result = TitleDateConsensus.Evaluate((value as PersonTitle)?.FontiDate);
        return (parameter as string) switch
        {
            "Visibility" => result.Level == ConsensusLevel.Hidden ? Visibility.Collapsed : Visibility.Visible,
            "Fill" => result.Level switch
            {
                ConsensusLevel.Green => Resource("GreenBrush", Brushes.ForestGreen),
                ConsensusLevel.Orange => Orange,
                ConsensusLevel.Red => Resource("DangerBrush", Brushes.Firebrick),
                _ => Brushes.Transparent,
            },
            "Details" => result.Details,
            "Name" => result.Level switch
            {
                ConsensusLevel.Hidden => "Date del titolo: una sola fonte",
                ConsensusLevel.Green => $"Date del titolo: verde. {result.Summary}",
                ConsensusLevel.Orange => $"Date del titolo: arancione. {result.Summary}",
                _ => $"Date del titolo: rosso. {result.Summary}",
            },
            _ => null,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Resource(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static Brush CreateOrange()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x1E));
        brush.Freeze();
        return brush;
    }
}
