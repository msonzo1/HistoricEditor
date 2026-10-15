using System.Windows;

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
    }

    private void UpdateSelection()
    {
        var node = PlacesTree.SelectedNode;
        SelectionText.Text = node is null ? "Nessun luogo selezionato." : $"Selezionato: {node.Path}";
        PickerMessage.Text = string.Empty;
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
            PickerMessage.Text = "Seleziona un luogo.";
            return;
        }

        ResultSpecificoId = node.IsSpecific ? node.Id : null;
        ResultLuogoId = node.IsSpecific ? null : node.Id;
        confirmed = true;
        DialogResult = true;
    }
}
