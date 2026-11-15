using System.Windows;
using System.Windows.Input;

namespace HistoricEditor;

public partial class PlacePickerWindow : Window
{
    private readonly PeopleStore store;
    private readonly bool allowSpecific;
    private bool confirmed;

    public int? ResultLuogoId { get; private set; }
    public int? ResultSpecificoId { get; private set; }

    public PlacePickerWindow(PeopleStore store, string title, bool allowSpecific, int? luogoId, int? specificoId, bool allowClear, bool startHistoric = false)
    {
        InitializeComponent();
        this.store = store;
        this.allowSpecific = allowSpecific;
        Title = title;
        HeaderText.Text = title;
        ClearButton.Visibility = allowClear ? Visibility.Visible : Visibility.Collapsed;

        PlacesTree.IncludeSpecific = allowSpecific;
        PlacesTree.SelectedNodeChanged += (_, _) => UpdateSelection();
        PlacesTree.NodeActivated += (_, _) => Confirm();
        PlacesTree.Initialize(store, specificoId ?? luogoId, specificoId is not null, startHistoric);
        Loaded += (_, _) => PlacesTree.FocusSearch();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None && PlacesTree.SelectedNode is not null)
            {
                e.Handled = true;
                EditSelected();
            }
        };
    }

    private void UpdateSelection()
    {
        var node = PlacesTree.SelectedNode;
        SelectionText.Text = node is null ? "Nessun luogo selezionato." : $"Selezionato: {node.Path}";
        EditButton.IsEnabled = node is not null;
        PickerMessage.Text = string.Empty;
    }

    // Vero se nel selettore è stato modificato un luogo: chi lo apre aggiorna il testo del luogo già scelto, anche se
    // il selettore si chiude con «Annulla».
    public bool PlacesEdited { get; private set; }

    private void EditPlace_Click(object sender, RoutedEventArgs e) => EditSelected();

    // Modifica del luogo selezionato (2026-11-15): un luogo specifico nella sua finestra, gli altri con i campi della
    // sezione Luoghi. Dopo il salvataggio l'albero si ricarica con lo stesso luogo selezionato.
    private void EditSelected()
    {
        if (PlacesTree.SelectedNode is not { } node)
        {
            PickerMessage.Text = "Selezionare un luogo.";
            return;
        }

        bool changed;
        if (node.IsSpecific)
        {
            changed = new SpecificPlaceEditorWindow(store, node.Id, null) { Owner = this }.ShowDialog() == true;
        }
        else
        {
            var editor = new PlaceEditorWindow(store, node) { Owner = this };
            editor.ShowDialog();
            changed = editor.Changed;
        }

        if (changed)
        {
            PlacesEdited = true;
            PlacesTree.Reload(node.Id, node.IsSpecific);
        }
    }

    private void NewPlace_Click(object sender, RoutedEventArgs e)
    {
        if (PlaceCreation.Create(store, this, PlacesTree.SelectedNode, allowSpecific) is { } created)
        {
            PlacesTree.Reload(created.Id, created.IsSpecific);
        }
    }

    private void Select_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ResultLuogoId = null;
        ResultSpecificoId = null;
        confirmed = true;
        DialogResult = true;
    }

    private void Confirm()
    {
        if (confirmed)
        {
            return;
        }

        if (PlacesTree.SelectedNode is not { } node)
        {
            PickerMessage.Text = "Selezionare un luogo.";
            return;
        }

        ResultSpecificoId = node.IsSpecific ? node.Id : null;
        ResultLuogoId = node.IsSpecific ? null : node.Id;
        confirmed = true;
        DialogResult = true;
    }
}
