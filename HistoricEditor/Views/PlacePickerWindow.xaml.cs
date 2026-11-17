using System.Windows;
using System.Windows.Input;

namespace HistoricEditor;

public partial class PlacePickerWindow : Window
{
    private readonly PeopleStore store;
    private readonly bool allowSpecific;
    private readonly PendingPlace? pending;
    private bool confirmed;

    public int? ResultLuogoId { get; private set; }
    public int? ResultSpecificoId { get; private set; }

    // Vero se è stato scelto il luogo in attesa (2026-11-15): resta in memoria e si crea al salvataggio.
    public bool ResultPending { get; private set; }

    // Solo un luogo specifico (2026-11-17): il luogo specifico di un luogo storico, scelto nei luoghi attuali (i resti
    // ancora visibili, come il Sito archeologico di Pelusio).
    public bool SpecificOnly { get; init; }

    // Con un luogo importato non ancora nell'archivio (pending), le parti che mancano compaiono come luoghi nuovi e il
    // selettore si apre su di esse.
    public PlacePickerWindow(PeopleStore store, string title, bool allowSpecific, int? luogoId, int? specificoId, bool allowClear, bool startHistoric = false, PendingPlace? pending = null)
    {
        InitializeComponent();
        this.store = store;
        this.allowSpecific = allowSpecific;
        this.pending = pending;
        Title = title;
        HeaderText.Text = title;
        ClearButton.Visibility = allowClear ? Visibility.Visible : Visibility.Collapsed;

        PlacesTree.IncludeSpecific = allowSpecific;
        PlacesTree.Pending = pending;
        PlacesTree.SelectedNodeChanged += (_, _) => UpdateSelection();
        PlacesTree.NodeActivated += (_, _) => Confirm();
        PlacesTree.Initialize(store, specificoId ?? luogoId, specificoId is not null, startHistoric, selectPending: pending is not null);
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
        SelectionText.Text = node is null ? "Nessun luogo selezionato."
            : node.IsPending ? $"Selezionato: {node.Path} (nuovo luogo: sarà creato al salvataggio)"
            : $"Selezionato: {node.Path}";
        EditButton.IsEnabled = node is not null;
        PickerMessage.Text = string.Empty;
    }

    // Vero se nel selettore è stato modificato un luogo: chi lo apre aggiorna il testo del luogo già scelto, anche se
    // il selettore si chiude con «Annulla».
    public bool PlacesEdited { get; private set; }

    // Vero se è stato corretto il luogo in attesa (livelli o luogo specifico), anche se il selettore si chiude con «Annulla».
    public bool PendingEdited { get; private set; }

    private void EditPlace_Click(object sender, RoutedEventArgs e) => EditSelected();

    // Modifica del luogo selezionato (2026-11-15): un luogo specifico nella sua finestra, gli altri con i campi della
    // sezione Luoghi. Dopo il salvataggio l'albero si ricarica con lo stesso luogo selezionato. Una parte del luogo in
    // attesa si corregge senza scrivere nell'archivio: i livelli nella finestra della conferma dell'import, il luogo
    // specifico nella sua finestra.
    private void EditSelected()
    {
        if (PlacesTree.SelectedNode is not { } node)
        {
            PickerMessage.Text = "Selezionare un luogo.";
            return;
        }

        if (node.IsPending && pending is not null)
        {
            // Il luogo storico in attesa (Anzio) si corregge con gli stessi campi dei luoghi salvati (2026-11-17); un
            // livello in attesa sopra di esso nella finestra dei livelli.
            var edited = node.IsSpecific ? PendingPlaceEditing.EditSpecific(store, pending, this)
                : pending.IsHistoric && ReferenceEquals(node, PlacesTree.PendingTarget) ? PendingPlaceEditing.EditHistoric(store, pending, this)
                : PendingPlaceEditing.EditLevels(store, pending, this);
            if (edited)
            {
                PendingEdited = true;
                PlacesTree.Reload(selectPending: true);
            }

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
        // Un luogo nuovo non può stare dentro una parte del luogo in attesa: si parte dal primo luogo esistente sopra di essa.
        var context = PlacesTree.SelectedNode;
        while (context is { IsPending: true })
        {
            context = context.Parent;
        }

        if (PlaceCreation.Create(store, this, context, allowSpecific) is { } created)
        {
            PlacesEdited = true;
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

        if (pending is not null && ReferenceEquals(node, PlacesTree.PendingTarget))
        {
            ResultPending = true;
            confirmed = true;
            DialogResult = true;
            return;
        }

        if (node.IsPending)
        {
            PickerMessage.Text = $"«{node.Nome}» sarà creato insieme al luogo nuovo: selezionare il luogo nuovo o un luogo già nell’archivio.";
            return;
        }

        if (SpecificOnly && !node.IsSpecific)
        {
            PickerMessage.Text = "Selezionare un luogo specifico (un sito, un edificio…) oppure crearne uno con «Nuovo luogo…».";
            return;
        }

        ResultSpecificoId = node.IsSpecific ? node.Id : null;
        ResultLuogoId = node.IsSpecific ? null : node.Id;
        confirmed = true;
        DialogResult = true;
    }
}
