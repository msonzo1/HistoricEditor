using System.IO;
using System.Windows;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Modifica di un luogo dal selettore dei luoghi (2026-11-15): stessi campi della sezione Luoghi, spostamento dei luoghi
// storici in un altro contenitore e conteggio dei personaggi collegati.
public class PlaceEditingTests
{
    [Fact]
    public void MovePlace_MovesHistoricPlaceAndUpdatesPersonTexts()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var (italia, marche, picenum) = Picenum(store);
            var person = new HistoricalPerson { Name = "Gneo Pompeo Magno", BirthLuogoId = picenum };
            store.Add(person);

            Assert.Equal(1, store.CountPeopleUsingPlace(picenum));
            Assert.Equal(1, store.CountPeopleUsingPlace(italia));
            Assert.Equal(0, store.CountPeopleUsingPlace(marche));

            // Un luogo non può finire dentro sé stesso o dentro un luogo che contiene.
            Assert.False(store.MovePlace(italia, picenum));
            Assert.False(store.MovePlace(picenum, picenum));
            Assert.Null(store.GetPlace(italia)!.ParentId);

            Assert.True(store.MovePlace(picenum, marche));
            Assert.Equal(marche, store.GetPlace(picenum)!.ParentId);
            Assert.Equal(1, store.CountPeopleUsingPlace(marche));
            Assert.Contains("Marche", store.GetPerson(person.Id)!.BirthPlace);

            // Il tipo di un luogo attuale resta quello del livello.
            store.UpdatePlace(marche, "Marche", null, null, false, string.Empty, string.Empty, false);
            Assert.Equal("Regione", store.GetPlace(marche)!.Tipo);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Picker_EditsTheSelectedPlace()
    {
        var dir = NewTempDir();
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var (_, _, picenum) = Picenum(store);
                var person = new HistoricalPerson { Name = "Gneo Pompeo Magno", BirthLuogoId = picenum };
                store.Add(person);

                // Il selettore mostrato (fuori dallo schermo) seleziona il luogo già scelto e attiva «Modifica luogo…».
                var picker = new PlacePickerWindow(store, "Luogo di nascita", allowSpecific: true, picenum, null, allowClear: true, startHistoric: true)
                {
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                };
                picker.Show();
                picker.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.True(picker.EditButton.IsEnabled);
                var node = picker.PlacesTree.SelectedNode!;
                Assert.Equal(picenum, node.Id);

                var editor = new PlaceEditorWindow(store, node);
                Assert.Equal("Regio V Picenum", editor.Details.NomeBox.Text);
                Assert.Equal(Visibility.Visible, editor.Details.TipoBox.Visibility);
                Assert.Equal("Regione dell'Italia augustea", editor.Details.TipoBox.Text);
                Assert.Equal(Visibility.Visible, editor.Details.ParentPanel.Visibility);
                Assert.Equal(Visibility.Visible, editor.Details.UsageText.Visibility);
                Assert.Contains("1 personaggio", editor.Details.UsageText.Text);
                Assert.False(editor.Details.SaveButton.IsVisible);

                // Nome vuoto: errore nella riga dei messaggi della finestra, niente salvato.
                editor.Details.NomeBox.Text = " ";
                Assert.False(editor.Details.Save());
                Assert.Equal("Il nome non può essere vuoto.", editor.EditorMessage.Text);

                editor.Details.NomeBox.Text = "Picenum";
                editor.Details.PeriodoBox.Text = "7 a.C. – 297";
                editor.Details.LatinBox.Text = "Picenum";
                editor.Details.TipoBox.Text = "Regio augustea";
                Assert.True(editor.Details.Save());
                editor.Close();
                picker.Close();

                var luogo = store.GetPlace(picenum)!;
                Assert.Equal(("Picenum", "Regio augustea", "7 a.C. – 297", true), (luogo.Nome, luogo.Tipo, luogo.Periodo, luogo.Storico));
                Assert.Equal("Picenum", HistoricPlaceLatin.Get(luogo));
                Assert.StartsWith("Picenum", store.GetPerson(person.Id)!.BirthPlace);
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static (int Italia, int Marche, int Picenum) Picenum(PeopleStore store)
    {
        var italia = store.EnsurePlace([new GeoLevel("Stato", "Italia")], null, null)!.Value;
        var marche = store.EnsurePlace([new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Marche")], null, null)!.Value;
        var picenum = store.AddHistoricPlace("Regio V Picenum", "Regione dell'Italia augustea", italia, string.Empty, null, null, string.Empty);
        return (italia, marche, picenum);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-place-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
