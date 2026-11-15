using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Gestione dei luoghi mostrata come scheda della finestra principale.
public partial class PlacesView : UserControl
{
    private PeopleStore? store;

    // Segnala che i luoghi sono cambiati: le schede collegate mostrano testi nuovi e il backup va aggiornato.
    public event EventHandler? PlacesChanged;

    public PlacesView()
    {
        InitializeComponent();
        PlacesTree.SelectedNodeChanged += (_, _) => ShowSelected();
        Details.MessageBlock = ManagerMessage;
        Details.Saved += (_, id) =>
        {
            PlacesTree.Reload(id);
            PlacesChanged?.Invoke(this, EventArgs.Empty);
            Feedback.Success(ManagerMessage, "Modifiche salvate.");
        };
        Details.Changed += (_, _) => PlacesChanged?.Invoke(this, EventArgs.Empty);
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
        DeleteButton.IsEnabled = selected is not null;
        EditSpecificButton.IsEnabled = selected?.IsSpecific == true;
        if (store is not null)
        {
            Details.Load(store, selected);
        }
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
            PlacesChanged?.Invoke(this, EventArgs.Empty);
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
        PlacesChanged?.Invoke(this, EventArgs.Empty);
        Feedback.Success(ManagerMessage, "Luogo eliminato.");
    }
}
