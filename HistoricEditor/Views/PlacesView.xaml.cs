using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Gestione dei luoghi mostrata come scheda della finestra principale.
public partial class PlacesView : UserControl
{
    private PeopleStore? store;

    public PlacesView()
    {
        InitializeComponent();
        PlacesTree.SelectedNodeChanged += (_, _) => ShowSelected();
        PlacesTree.NodeActivated += (_, _) =>
        {
            if (PlacesTree.SelectedNode is { IsSpecific: true })
            {
                EditSpecific_Click(this, new RoutedEventArgs());
            }
        };
    }

    private Window? Owner => Window.GetWindow(this);

    // Al primo ingresso carica l'albero; ai successivi lo ricarica (i luoghi possono cambiare dalle schede persona).
    public void Activate(PeopleStore peopleStore)
    {
        if (store is null)
        {
            store = peopleStore;
            PlacesTree.Initialize(store);
        }
        else
        {
            PlacesTree.Reload(PlacesTree.SelectedNode?.Id, PlacesTree.SelectedNode?.IsSpecific == true);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(PlacesTree.FocusSearch));
    }

    private void ShowSelected()
    {
        var selected = PlacesTree.SelectedNode;
        Feedback.Info(ManagerMessage, string.Empty);
        DeleteButton.IsEnabled = selected is not null;
        EditSpecificButton.IsEnabled = selected?.IsSpecific == true;

        if (selected?.Source is not { } luogo)
        {
            EditPanel.IsEnabled = false;
            PathText.Text = selected is null ? string.Empty : $"{selected.Path}\nLuogo specifico: usa «Modifica luogo specifico…» o fai doppio clic.";
            return;
        }

        EditPanel.IsEnabled = true;
        PathText.Text = selected.Path;
        TipoText.Text = selected.Tipo;
        NomeBox.Text = selected.Nome;
        LatBox.Text = CoordinateText.Format(luogo.Latitudine);
        LonBox.Text = CoordinateText.Format(luogo.Longitudine);
        StoricoBox.IsChecked = luogo.Storico;
        PeriodoBox.Text = luogo.Periodo;
        NoteBox.Text = luogo.Note;

        // Il flag ha senso solo per luoghi attuali con un altro livello prima della foglia.
        var (schema, index) = SchemaPosition(selected);
        if (!luogo.Storico && index >= 0 && index + 2 < schema.Length)
        {
            SenzaLivelloBox.Content = $"{selected.Tipo} senza {schema[index + 1]}";
            SenzaLivelloBox.IsChecked = luogo.SenzaLivelloInferiore;
            SenzaLivelloBox.Visibility = Visibility.Visible;
        }
        else
        {
            SenzaLivelloBox.IsChecked = false;
            SenzaLivelloBox.Visibility = Visibility.Collapsed;
        }
    }

    private void StoricoBox_Changed(object sender, RoutedEventArgs e)
        => PeriodoPanel.Visibility = StoricoBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || PlacesTree.SelectedNode is not { Source: not null } selected)
        {
            return;
        }

        var nome = NomeBox.Text.Trim();
        if (nome.Length == 0)
        {
            Feedback.Error(ManagerMessage, "Il nome non può essere vuoto.");
            return;
        }

        if (!CoordinateText.TryParse(LatBox.Text, out var lat))
        {
            Feedback.Error(ManagerMessage, "Latitudine non valida.");
            return;
        }

        if (!CoordinateText.TryParse(LonBox.Text, out var lon))
        {
            Feedback.Error(ManagerMessage, "Longitudine non valida.");
            return;
        }

        var storico = StoricoBox.IsChecked == true;
        var senzaLivello = SenzaLivelloBox.Visibility == Visibility.Visible && SenzaLivelloBox.IsChecked == true;
        try
        {
            store.UpdatePlace(selected.Id, nome, lat, lon, storico, storico ? PeriodoBox.Text : string.Empty, NoteBox.Text, senzaLivello);
        }
        catch (DbUpdateException)
        {
            Feedback.Error(ManagerMessage, "Esiste già un luogo con questo nome allo stesso livello.");
            return;
        }

        PlacesTree.Reload(selected.Id);
        Feedback.Success(ManagerMessage, "Modifiche salvate.");
    }

    private void NewPlace_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || Owner is not { } owner)
        {
            return;
        }

        if (PlaceCreation.Create(store, owner, PlacesTree.SelectedNode) is { } created)
        {
            PlacesTree.Reload(created.Id, created.IsSpecific);
            Feedback.Success(ManagerMessage, "Luogo creato.");
        }
    }

    private void EditSpecific_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || PlacesTree.SelectedNode is not { IsSpecific: true } node)
        {
            return;
        }

        var editor = new SpecificPlaceEditorWindow(store, node.Id, null) { Owner = Owner };
        if (editor.ShowDialog() == true)
        {
            PlacesTree.Reload(node.Id, selectSpecific: true);
            Feedback.Success(ManagerMessage, "Luogo specifico aggiornato.");
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || PlacesTree.SelectedNode is not { } selected || Owner is not { } owner)
        {
            return;
        }

        var message = selected.IsSpecific
            ? $"Eliminare il luogo specifico «{selected.Nome}»? I personaggi collegati perderanno questo luogo."
            : $"Eliminare «{selected.Nome}» e tutto ciò che contiene, compresi i luoghi specifici? I personaggi collegati perderanno questi luoghi.";
        if (MessageBox.Show(owner, message, "Conferma eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (selected.IsSpecific)
            {
                store.DeleteSpecificPlace(selected.Id);
            }
            else
            {
                store.DeletePlace(selected.Id);
            }
        }
        catch (DbUpdateException)
        {
            Feedback.Error(ManagerMessage, "Impossibile eliminare il luogo.");
            return;
        }

        PlacesTree.Reload();
        Feedback.Success(ManagerMessage, "Luogo eliminato.");
    }

    private static (string[] Schema, int Index) SchemaPosition(PlaceNode node)
    {
        var root = node;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        var schema = GeoSchemas.For(root.Nome);
        return (schema, Array.FindIndex(schema, t => string.Equals(t, node.Tipo, StringComparison.OrdinalIgnoreCase)));
    }
}
