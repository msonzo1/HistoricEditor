using System.IO;
using System.Windows;
using System.Windows.Controls;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Regioni e province italiane nel codice (2026-11-15): tendine della finestra dei livelli, nomi uniformi all'import e
// niente luoghi scritti nell'archivio all'avvio.
public class ItalianDivisionsTests
{
    [Fact]
    public void List_HasAllRegionsAndProvinces()
    {
        Assert.Equal(20, ItalianDivisions.Regions.Count);
        Assert.Equal(109, ItalianDivisions.Regions.Sum(r => r.Province.Count));
        var ids = ItalianDivisions.Regions.Select(r => r.WikidataId).Concat(ItalianDivisions.Regions.SelectMany(r => r.Province).Select(p => p.WikidataId)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Empty(ItalianDivisions.ProvincesOf("Valle d'Aosta"));
        Assert.Equal(["Provincia di Matera", "Provincia di Potenza"], ItalianDivisions.ProvincesOf("Basilicata").Select(p => p.Nome));
        Assert.DoesNotContain(ItalianDivisions.ProvincesOf("Puglia"), p => p.Nome == "Provincia di Matera");
    }

    [Fact]
    public void Find_UsesWikidataIdOrNameIgnoringCase()
    {
        Assert.Equal("Città metropolitana di Napoli", ItalianDivisions.Find("Provincia", "città metropolitana di Napoli")!.Nome);
        Assert.Equal("Provincia di Rieti", ItalianDivisions.Find("Provincia", "qualsiasi", "Q16267")!.Nome);
        Assert.Equal("Trentino-Alto Adige", ItalianDivisions.Find("Regione", "Trentino-Alto Adige/Südtirol")!.Nome);
        Assert.Null(ItalianDivisions.Find("Comune", "Roma"));

        var levels = new List<GeoLevel> { new("Stato", "Italia"), new("Regione", "Campania"), new("Provincia", "città metropolitana di Napoli"), new("Comune", "Nola") };
        ItalianDivisions.Normalize(levels, [new PlaceDivision { Name = "città metropolitana di Napoli", WikidataId = "Q18241891" }]);
        Assert.Equal("Città metropolitana di Napoli", levels[2].Nome);
        Assert.Equal("Nola", levels[3].Nome);
    }

    [Fact]
    public void NewArchive_HasNoPlaces_AndProvinceKeepsItsWikidataId()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            Assert.Empty(store.GetAllPlaces());

            var cittareale = store.EnsurePlace([new("Stato", "Italia"), new("Regione", "Lazio"), new("Provincia", "Provincia di Rieti"), new("Comune", "Cittareale")], null, null)!.Value;
            var rieti = store.GetPlace(cittareale)!.ParentId!.Value;
            Assert.Equal("Q16267", store.GetPlace(rieti)!.WikidataId);
            Assert.Equal(4, store.GetAllPlaces().Count);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void LevelsWindow_OffersRegionsAndProvincesWithArchivedOnesMarked()
    {
        var dir = NewTempDir();
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var cittareale = store.EnsurePlace([new("Stato", "Italia"), new("Regione", "Lazio"), new("Provincia", "Provincia di Rieti"), new("Comune", "Cittareale")], null, null)!.Value;
                // Provincia rinominata nell'archivio: resta collegata alla voce dell'elenco dal codice Wikidata.
                var rieti = store.GetPlace(cittareale)!.ParentId!.Value;
                store.UpdatePlace(rieti, "Provincia di Rieti (Sabina)", null, null, false, string.Empty, string.Empty, false);

                var window = new PlacesEditorWindow([new("Stato", "Italia"), new("Regione", "Lazio"), new("Provincia", string.Empty), new("Comune", string.Empty)], store: store)
                {
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                };
                window.Show();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var combos = window.LevelsPanel.Children.OfType<DockPanel>().Select(d => d.Children.OfType<Control>().Last()).ToList();
                var region = Assert.IsType<ComboBox>(combos[0]);
                var province = Assert.IsType<ComboBox>(combos[1]);
                Assert.IsType<TextBox>(combos[2]);

                var regions = region.ItemsSource.Cast<PlacesEditorWindow.LevelChoice>().ToList();
                Assert.Equal(21, regions.Count);
                Assert.Equal(new PlacesEditorWindow.LevelChoice("Nuova regione…", false, true), regions[^1]);
                Assert.True(regions.Single(r => r.Nome == "Lazio").InArchive);
                Assert.False(regions.Single(r => r.Nome == "Toscana").InArchive);

                var provinces = province.ItemsSource.Cast<PlacesEditorWindow.LevelChoice>().ToList();
                Assert.Equal(
                    ["Città metropolitana di Roma Capitale", "Provincia di Frosinone", "Provincia di Latina", "Provincia di Rieti (Sabina)", "Provincia di Viterbo", "Nuova provincia…"],
                    provinces.Select(p => p.Nome));
                Assert.True(provinces.Single(p => p.Nome == "Provincia di Rieti (Sabina)").InArchive);

                // Cambiando regione cambiano le province proposte.
                region.Text = "Umbria";
                region.SelectedItem = regions.Single(r => r.Nome == "Umbria");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Equal(["Provincia di Perugia", "Provincia di Terni", "Nuova provincia…"], province.ItemsSource.Cast<PlacesEditorWindow.LevelChoice>().Select(p => p.Nome));

                // «Nuova provincia…» svuota la casella per scrivere il nome.
                province.SelectedItem = province.ItemsSource.Cast<PlacesEditorWindow.LevelChoice>().Last();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Equal(string.Empty, province.Text);
                Assert.Equal("Scrivere il nome della nuova provincia.", window.EditorMessage.Text);
                window.Close();
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-italy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
