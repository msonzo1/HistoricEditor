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

            // Preparato senza scrivere: l'archivio resta com'era e il testo viene dall'import.
            Assert.Equal(seeded, store.GetAllPlaces().Count);
            Assert.Equal("Firenze, Città metropolitana di Firenze, Toscana, Italia", pending.Description.DisplayText);
            // Archivio vuoto (niente più regioni scritte all'avvio): nessun luogo esistente da cui aprire il selettore.
            Assert.Equal(0, seeded);
            Assert.Equal((null, null, null), ImportedPlaceResolver.FindExisting(store, pending));

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

    // Il luogo specifico scritto o tolto prima del salvataggio sostituisce quello letto dall'import.
    [Fact]
    public void PendingPlace_CreatesTheSpecificDraftOrNone()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var pending = ImportedPlaceResolver.Prepare(store, Firenze(), "Luogo di nascita", null!)!;
            pending.SetSpecific(PalazzoVecchio());
            Assert.Equal("Palazzo Vecchio (Firenze, Città metropolitana di Firenze, Toscana, Italia)", pending.Description.DisplayText);
            Assert.Equal((null, null, null), ImportedPlaceResolver.FindExisting(store, pending));

            var (luogoId, specificoId) = ImportedPlaceResolver.Create(store, pending);
            Assert.Null(luogoId);
            var palazzo = store.GetSpecificPlace(specificoId!.Value)!;
            Assert.Equal(("Palazzo Vecchio", "palazzo", 43.769, "Q1"), (palazzo.Nome, palazzo.Tipo, palazzo.Latitudine, palazzo.Note));
            var firenze = palazzo.LuogoId;
            Assert.Equal("Firenze", store.GetPlace(firenze)!.Nome);

            // Lo stesso luogo specifico, già nell'archivio, si riusa.
            var again = ImportedPlaceResolver.Prepare(store, Firenze(), "Luogo di morte", null!)!;
            again.SetSpecific(PalazzoVecchio());
            Assert.Equal((null, specificoId, firenze), ImportedPlaceResolver.FindExisting(store, again));

            // Tolto il luogo specifico resta solo il comune.
            again.SetSpecific(null);
            Assert.Equal(string.Empty, again.Specific);
            Assert.Equal((firenze, (int?)null, firenze), ImportedPlaceResolver.FindExisting(store, again));
            Assert.Equal((firenze, (int?)null), ImportedPlaceResolver.Create(store, again));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Nel selettore le parti del luogo in attesa che mancano nell'archivio compaiono come luoghi nuovi, sotto l'ultima
    // che c'è già, e il selettore si apre sul luogo nuovo.
    [Fact]
    public void Picker_ShowsThePendingPlaceAsNew()
    {
        var dir = NewTempDir();
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var italia = store.EnsurePlace([new GeoLevel("Stato", "Italia")], null, null)!.Value;
                var pending = ImportedPlaceResolver.Prepare(store, Firenze(), "Luogo di nascita", null!)!;
                pending.SetSpecific(PalazzoVecchio());

                var picker = new PlacePickerWindow(store, "Luogo di nascita", allowSpecific: true, null, null, allowClear: true, pending: pending)
                {
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                };
                picker.Show();
                picker.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var target = picker.PlacesTree.PendingTarget!;
                Assert.Same(target, picker.PlacesTree.SelectedNode);
                Assert.True(target.IsPending && target.IsSpecific);
                Assert.Equal("Palazzo Vecchio", target.Nome);
                Assert.Contains("nuovo, sarà creato al salvataggio", target.Tipo);
                Assert.True(picker.EditButton.IsEnabled);
                Assert.Equal("Italia › Toscana › Città metropolitana di Firenze › Firenze › Palazzo Vecchio", target.Path);
                var toscana = target.Parent!.Parent!.Parent!;
                Assert.True(toscana.IsPending);
                Assert.False(toscana.Parent!.IsPending);
                Assert.Equal(italia, toscana.Parent.Id);

                // Una parte nuova che non è il luogo stesso non si sceglie: si crea insieme al luogo.
                toscana.IsSelected = true;
                typeof(PlacePickerWindow).GetMethod("Confirm", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
                Assert.False(picker.ResultPending);
                Assert.StartsWith("«Toscana» sarà creato insieme al luogo nuovo", picker.PickerMessage.Text);
                picker.Close();
                Assert.Single(store.GetAllPlaces());
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // Il campo del luogo specifico resta modificabile anche con un luogo in attesa; un luogo importato che esiste già
    // tutto si mostra come gli altri.
    [Fact]
    public void ImportedPerson_SpecificFieldStaysEditable()
    {
        var dir = NewTempDir();
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var editor = OpenImport(store);
                var field = editor.BirthPlaceField;
                Assert.NotNull(field.Pending);
                Assert.Equal(Visibility.Visible, field.SpecificRow.Visibility);
                Assert.Equal(Visibility.Visible, field.SpecificButton.Visibility);
                Assert.Equal(Visibility.Collapsed, field.ClearSpecificButton.Visibility);
                Assert.Equal("Nessun luogo specifico", field.SpecificText.Text);

                field.Pending!.SetSpecific(PalazzoVecchio());
                field.Refresh();
                Assert.Equal("Palazzo Vecchio", field.SpecificText.Text);
                Assert.Equal(Visibility.Visible, field.ClearSpecificButton.Visibility);
                Assert.Empty(store.GetSpecificPlaces());

                Assert.True(Save(editor));
                editor.Close();
                var person = Assert.Single(store.LoadAll());
                Assert.Equal("Palazzo Vecchio", store.GetSpecificPlace(person.BirthLuogoSpecificoId!.Value)!.Nome);

                // Ora Firenze c'è: il secondo import la mostra come un luogo qualsiasi, con il luogo specifico da aggiungere.
                var second = OpenImport(store);
                var place = second.BirthPlaceField;
                Assert.Null(place.Pending);
                Assert.True(place.HasImportedPlace);
                Assert.Equal("Firenze", store.GetPlace(place.LuogoId!.Value)!.Nome);
                Assert.Equal(Visibility.Collapsed, place.PendingNote.Visibility);
                Assert.Equal(Visibility.Visible, place.SpecificButton.Visibility);

                // Chiusura senza la domanda sulle modifiche non salvate, che fermerebbe il test.
                var editorType = typeof(PersonEditorWindow);
                editorType.GetField("savedSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(second, editorType.GetMethod("FormSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(second, null));
                second.Close();
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static PersonEditorWindow OpenImport(PeopleStore store)
    {
        var import = new WikipediaImportResult { Name = "Dante", Surname = "Alighieri", BirthPlaces = [Firenze()] };
        var editor = new PersonEditorWindow(store, null, import)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
        };
        editor.Show();
        editor.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return editor;
    }

    private static bool Save(PersonEditorWindow editor)
        => (bool)typeof(PersonEditorWindow).GetMethod("EnsureSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;

    private static LuogoSpecifico PalazzoVecchio() => new() { Nome = "Palazzo Vecchio", Tipo = "palazzo", Latitudine = 43.769, Note = "Q1" };

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
