using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class PlacesEditorWindowTests
{
    // Luogo storico sotto il solo Stato (2026-11-15, da Pompeo): la Regio V Picenum non sta in una regione di oggi, si
    // salva sotto l'Italia lasciando vuoti regione, provincia e comune.
    [Fact]
    public void HistoricPlace_CanBeSavedUnderTheStateOnly()
    {
        var (saved, levels, message) = SaveWith(allowPartial: true);

        Assert.True(saved, message);
        Assert.Equal(["Stato Italia"], levels!.Select(l => $"{l.Tipo} {l.Nome}"));
    }

    // Un luogo di oggi resta completo fino al comune.
    [Fact]
    public void CurrentPlace_StillRequiresTheLowestLevel()
    {
        var (saved, _, message) = SaveWith(allowPartial: false);

        Assert.False(saved);
        Assert.Contains("Regione", message);
    }

    private static (bool Saved, IReadOnlyList<GeoLevel>? Levels, string Message) SaveWith(bool allowPartial)
    {
        (bool, IReadOnlyList<GeoLevel>?, string) result = default;
        WpfTestThread.Run(() =>
        {
            var window = new PlacesEditorWindow([new GeoLevel("Stato", "Italia")], allowPartial: allowPartial)
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
            };
            var save = typeof(PlacesEditorWindow).GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                save.Invoke(window, [window, new RoutedEventArgs()]);
                if (window.IsVisible)
                {
                    result = (false, null, ((TextBlock)window.FindName("EditorMessage")).Text);
                    window.Close();
                }
            });
            var saved = window.ShowDialog() == true;
            result = saved ? (true, window.ResultLevels, string.Empty) : result;
        });
        return result;
    }
}
