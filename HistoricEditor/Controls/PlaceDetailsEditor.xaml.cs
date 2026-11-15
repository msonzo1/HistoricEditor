using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Dati di un luogo attuale o storico (nome, coordinate, periodo, nome latino, nomi storici, note, allegati), usati nella
// sezione Luoghi e nella finestra «Modifica luogo» del selettore (2026-11-15): i due modi di modificare restano uguali.
public partial class PlaceDetailsEditor : UserControl
{
    private static readonly TextBlock NoMessage = new();
    private PeopleStore? store;
    private PlaceNode? node;
    private int? parentId;

    public PlaceDetailsEditor()
    {
        InitializeComponent();
    }

    // Dove compaiono gli esiti e gli errori (la riga dei messaggi della sezione o della finestra).
    public TextBlock? MessageBlock { get; set; }

    // Nella finestra il salvataggio è il pulsante «Salva» in basso.
    public bool ShowSaveButton
    {
        get => SaveButton.Visibility == Visibility.Visible;
        set => SaveButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public PlaceNode? Node => node;

    // Salvataggio riuscito, con il luogo salvato.
    public event EventHandler<int>? Saved;

    // Nomi storici da Wikidata o allegati cambiati: le schede collegate e il backup vanno aggiornati.
    public event EventHandler? Changed;

    private TextBlock Message => MessageBlock ?? NoMessage;

    public void Load(PeopleStore peopleStore, PlaceNode? selected)
    {
        store = peopleStore;
        node = selected;
        Feedback.Info(Message, string.Empty);

        if (selected?.Source is not { } luogo)
        {
            EditPanel.IsEnabled = false;
            UsageText.Visibility = Visibility.Collapsed;
            PathText.Text = selected is null ? string.Empty : $"{selected.Path}\nLuogo specifico: usare «Modifica luogo specifico…» o fare doppio clic.";
            return;
        }

        EditPanel.IsEnabled = true;
        PathText.Text = selected.Path;
        TipoText.Text = selected.Tipo;
        TipoText.Visibility = luogo.Storico ? Visibility.Collapsed : Visibility.Visible;
        TipoBox.Visibility = luogo.Storico ? Visibility.Visible : Visibility.Collapsed;
        TipoBox.ItemsSource = luogo.Storico ? store.GetHistoricPlaceTypes() : null;
        TipoBox.Text = luogo.Tipo;
        NomeBox.Text = selected.Nome;
        LatBox.Text = CoordinateText.Format(luogo.Latitudine);
        LonBox.Text = CoordinateText.Format(luogo.Longitudine);
        StoricoBox.IsChecked = luogo.Storico;
        PeriodoBox.Text = luogo.Periodo;
        var current = store.GetPlace(luogo.Id) ?? luogo;
        NoteEditor.Text = current.Note;
        LoadHistoricNames(current.NomiStorici);
        LatinBox.Text = HistoricPlaceLatin.Get(current);
        LatinExpander.IsExpanded = false;
        AttachmentsView.Load(store, AmbitiAllegato.Luogo, luogo.Id);

        // Un luogo storico si può spostare in un altro contenitore; la catena dei luoghi attuali segue lo schema del Paese.
        parentId = current.ParentId;
        ParentPanel.Visibility = luogo.Storico ? Visibility.Visible : Visibility.Collapsed;
        UpdateParent();

        var count = store.CountPeopleUsingPlace(luogo.Id);
        UsageText.Text = count == 0 ? string.Empty
            : $"Collegato a {count} {(count == 1 ? "personaggio" : "personaggi")}, anche tramite i luoghi che contiene: le modifiche valgono per tutti.";
        UsageText.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;

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

    public void FocusName()
    {
        NomeBox.Focus();
        NomeBox.SelectAll();
    }

    public bool Save()
    {
        if (store is null || node is not { Source: not null } selected)
        {
            return false;
        }

        var nome = NomeBox.Text.Trim();
        if (nome.Length == 0)
        {
            Feedback.Error(Message, "Il nome non può essere vuoto.");
            NomeBox.Focus();
            return false;
        }

        if (!CoordinateText.TryParse(LatBox.Text, out var lat))
        {
            Feedback.Error(Message, "Latitudine non valida.");
            LatBox.Focus();
            return false;
        }

        if (!CoordinateText.TryParse(LonBox.Text, out var lon))
        {
            Feedback.Error(Message, "Longitudine non valida.");
            LonBox.Focus();
            return false;
        }

        var storico = StoricoBox.IsChecked == true;
        var senzaLivello = SenzaLivelloBox.Visibility == Visibility.Visible && SenzaLivelloBox.IsChecked == true;
        if (ReadHistoricNames() is not { } names)
        {
            return false;
        }

        try
        {
            if (storico && ParentPanel.Visibility == Visibility.Visible && !store.MovePlace(selected.Id, parentId))
            {
                Feedback.Error(Message, "Un luogo non può essere contenuto in sé stesso o in un luogo che contiene.");
                return false;
            }

            // Il tipo cambia solo per un luogo che era già storico: quello di un luogo attuale segue il livello.
            var tipo = TipoBox.Visibility == Visibility.Visible ? TipoBox.Text.Trim() : null;
            store.UpdatePlace(selected.Id, nome, lat, lon, storico, storico ? PeriodoBox.Text : string.Empty, NoteEditor.Text, senzaLivello, tipo);
            store.SetPlaceHistoricNames(selected.Id, names);
            store.SetPlaceLatinName(selected.Id, storico ? LatinBox.Text : string.Empty);
        }
        catch (DbUpdateException)
        {
            Feedback.Error(Message, "Esiste già un luogo con questo nome allo stesso livello.");
            return false;
        }

        Saved?.Invoke(this, selected.Id);
        return true;
    }

    // Intestazione della tendina chiusa: «Nome in latino: Vicus Phalacrinae».
    private void LatinBox_Changed(object sender, TextChangedEventArgs e)
        => LatinExpanderHeader.Text = LatinBox.Text.Trim().Length > 0 ? $"Nome in latino: {LatinBox.Text.Trim()}" : "Nome in latino";

    private void StoricoBox_Changed(object sender, RoutedEventArgs e)
        => PeriodoPanel.Visibility = StoricoBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void UpdateParent()
    {
        var text = store?.DescribePlace(parentId) ?? string.Empty;
        ParentText.Text = text.Length > 0 ? text : "Nessuno (luogo di primo livello)";
    }

    private void ChooseParent_Click(object sender, RoutedEventArgs e)
    {
        if (store is null)
        {
            return;
        }

        var startHistoric = parentId is int id && store.GetPlace(id)?.Storico == true;
        var picker = new PlacePickerWindow(store, "Luogo che contiene il luogo storico", allowSpecific: false, parentId, null, allowClear: true, startHistoric: startHistoric)
        {
            Owner = Window.GetWindow(this),
        };
        if (picker.ShowDialog() == true)
        {
            parentId = picker.ResultLuogoId;
            UpdateParent();
        }
        else if (picker.PlacesEdited)
        {
            UpdateParent();
        }
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
