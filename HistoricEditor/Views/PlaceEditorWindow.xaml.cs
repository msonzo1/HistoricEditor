using System.Windows;

namespace HistoricEditor;

// Modifica di un luogo attuale o storico dal selettore dei luoghi (2026-11-15): gli stessi campi della sezione Luoghi.
public partial class PlaceEditorWindow : Window
{
    public PlaceEditorWindow(PeopleStore store, PlaceNode node)
    {
        InitializeComponent();
        Details.MessageBlock = EditorMessage;
        Details.ShowSaveButton = false;
        Details.Load(store, node);
        Details.Changed += (_, _) => Changed = true;
        Loaded += (_, _) => Details.FocusName();
    }

    // Luogo storico in attesa (2026-11-17): stessi campi, salvati in memoria fino al salvataggio della scheda.
    public PlaceEditorWindow(PeopleStore store, PendingPlace pending)
    {
        InitializeComponent();
        Details.MessageBlock = EditorMessage;
        Details.ShowSaveButton = false;
        Details.LoadPending(store, pending);
        Loaded += (_, _) => Details.FocusName();
    }

    // Vero anche se la finestra si chiude con «Annulla» dopo aver cambiato allegati o nomi storici da Wikidata, che si
    // salvano subito.
    public bool Changed { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Details.Save())
        {
            Changed = true;
            DialogResult = true;
        }
    }
}
