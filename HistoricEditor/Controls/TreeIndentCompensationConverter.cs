using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace HistoricEditor;

/// <summary>
/// Restituisce un margine sinistro che annulla il rientro dei livelli del TreeView,
/// così le colonne delle righe restano allineate alle intestazioni a qualunque profondità.
/// </summary>
public sealed class TreeIndentCompensationConverter : IValueConverter
{
    public double IndentPerLevel { get; set; } = 19;

    public int ReferenceLevel { get; set; } = 2;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var level = 0;
        DependencyObject? current = value as DependencyObject;
        while (current is not null and not TreeView)
        {
            if (current is TreeViewItem)
                level++;
            current = VisualTreeHelper.GetParent(current);
        }
        var offset = (level - ReferenceLevel) * IndentPerLevel;
        return parameter as string == "Name"
            ? new Thickness(offset, 0, 8, 0)
            : new Thickness(-offset, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
