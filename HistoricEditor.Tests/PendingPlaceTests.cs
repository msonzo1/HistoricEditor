using System.IO;
using System.Reflection;
using System.Windows;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Luoghi importati tenuti in memoria fino al salvataggio (2026-11-15): annullando l'import non resta nulla nell'archivio.
public class PendingPlaceTests
{
    [Fact]
    public void PendingPlace_IsCreatedOnlyWhenSaved()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var seeded = store.GetAllPlaces().Count;
            var pending = ImportedPlaceResolver.Prepare(store, Firenze(), "Luogo di nascita", null!)!;

            // Preparato senza scrivere: nell'archivio ci sono solo i luoghi di partenza (Stati e regioni) e il testo viene dall'import.
            Assert.Equal(seeded, store.GetAllPlaces().Count);
            Assert.Equal("Firenze, Città metropolitana di Firenze, Toscana, Italia", pending.Description.DisplayText);
            // Il selettore si aprirebbe sul luogo esistente più vicino.
            var existing = ImportedPlaceResolver.FindExisting(store, pending);
            Assert.Equal(((int?)null, (int?)null), (existing.LuogoId, existing.SpecificoId));
            Assert.Equal("Toscana", store.GetPlace(existing.Nearest!.Value)!.Nome);

            // Un salvataggio che fallisce non lascia luoghi.
            Assert.Throws<InvalidOperationException>(() => store.SaveTogether(() =>
            {
                ImportedPlaceResolver.Create(store, pending);
                throw new InvalidOperationException("Salvataggio fallito.");
            }));
            Assert.Equal(seeded, store.GetAllPlaces().Count);

            var (luogoId, specificoId) = ImportedPlaceResolver.Create(store, pending);
            Assert.Null(specificoId);
            Assert.Equal("Firenze", store.GetPlace(luogoId!.Value)!.Nome);
            Assert.Equal((luogoId, (int?)null, luogoId), ImportedPlaceResolver.FindExisting(store, pending));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void ImportedPerson_CreatesPlacesOnlyWhenSaved()
    {
        var dir = NewTempDir();
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var seeded = store.GetAllPlaces().Count;
                var import = new WikipediaImportResult { Name = "Dante", Surname = "Alighieri", BirthPlaces = [Firenze()] };
                var editor = new PersonEditorWindow(store, null, import)
                {
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                };
                editor.Show();
                editor.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                // Dopo l'import il campo mostra il luogo, ma nell'archivio non c'è ancora.
                Assert.NotNull(editor.BirthPlaceField.Pending);
                Assert.Null(editor.BirthPlaceField.LuogoId);
                Assert.Equal(Visibility.Visible, editor.BirthPlaceField.PendingNote.Visibility);
                Assert.Equal(seeded, store.GetAllPlaces().Count);

                var saved = (bool)typeof(PersonEditorWindow).GetMethod("EnsureSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
                Assert.True(saved);
                Assert.Null(editor.BirthPlaceField.Pending);
                Assert.Equal(Visibility.Collapsed, editor.BirthPlaceField.PendingNote.Visibility);
                editor.Close();

                var person = Assert.Single(store.LoadAll());
                Assert.Equal("Firenze", store.GetPlace(person.BirthLuogoId!.Value)!.Nome);
                Assert.Equal("Firenze, Città metropolitana di Firenze, Toscana, Italia", person.BirthPlace);
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static HistoricalPlaceRecord Firenze() => new()
    {
        WikidataId = "Qcomune",
        CurrentName = "Firenze",
        CurrentCountry = "Italia",
        AdministrativeDivisions =
        [
            new PlaceDivision { Relation = "Unità amministrativa (P131)", WikidataId = "Qprov", SubjectWikidataId = "Qcomune", Name = "Città metropolitana di Firenze" },
            new PlaceDivision { Relation = "Unità amministrativa (P131)", WikidataId = "Qreg", SubjectWikidataId = "Qprov", Name = "Toscana" },
            new PlaceDivision { Relation = "Unità amministrativa (P131)", WikidataId = "Qit", SubjectWikidataId = "Qreg", Name = "Italia" },
            new PlaceDivision { Relation = "Paese (P17)", WikidataId = "Qit", SubjectWikidataId = "Qcomune", Name = "Italia" },
        ],
    };

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-pending-place-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
