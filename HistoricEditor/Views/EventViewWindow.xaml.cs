using System.Windows;

namespace HistoricEditor;

// Scheda di lettura di un evento: data, luogo, descrizione e partecipanti.
public partial class EventViewWindow : Window
{
    private readonly PeopleStore store;
    private readonly int eventId;
    private string wikidataUrl = string.Empty;

    // Vero se dalla scheda (o da finestre aperte da qui) sono stati salvati cambiamenti all'archivio.
    public bool Changed { get; private set; }

    public EventViewWindow(PeopleStore store, int eventId)
    {
        InitializeComponent();
        this.store = store;
        this.eventId = eventId;
        Load();
        Loaded += (_, _) => EditButton.Focus();
    }

    private void Load()
    {
        if (store.GetEvent(eventId) is not { } evento)
        {
            if (IsLoaded)
            {
                Close();
            }

            return;
        }

        var names = store.GetPersonSummaries().ToDictionary(p => p.Id, p => p.Nome);
        Title = $"Evento – {evento.Titolo}";
        HeaderText.Text = evento.Titolo;
        HeaderSubtitle.Text = string.Join(" · ", new[] { evento.DataTesto, evento.Tipo.Trim() }.Where(s => s.Length > 0));

        // Il luogo compare col nome dell'epoca, es. «Costantinopoli (oggi Istanbul)».
        var place = evento.LuogoId is null && evento.LuogoSpecificoId is null
            ? string.Empty
            : store.DescribeRoleAt(evento.LuogoId, evento.LuogoSpecificoId, evento.Anno).DisplayText;
        var fields = EventSheet.MainFields(evento, place);
        MainList.ItemsSource = fields;
        MainEmptyText.Visibility = fields.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        DescriptionText.Text = evento.Descrizione;
        DescriptionCard.Visibility = RichNotes.IsEmpty(evento.Descrizione) ? Visibility.Collapsed : Visibility.Visible;
        AttachmentsView.Load(store, AmbitiAllegato.Evento, eventId);

        var people = EventSheet.Participants(evento, id => names.GetValueOrDefault(id, $"Scheda {id}"));
        PeopleList.ItemsSource = people;
        NoPeopleText.Visibility = people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        wikidataUrl = RecordSheet.WikidataUrl(evento.WikidataId);
        WikidataLink.Content = $"Wikidata: {evento.WikidataId.Trim()}";
        WikidataLink.ToolTip = wikidataUrl;
        SourcesCard.Visibility = wikidataUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenPerson_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int id } && RecordNavigator.ViewPerson(this, store, id))
        {
            Changed = true;
            Load();
        }
    }

    private void OpenWikidata_Click(object sender, RoutedEventArgs e)
    {
        if (wikidataUrl.Length > 0 && !RecordNavigator.OpenExternal(wikidataUrl))
        {
            Feedback.Error(StatusMessage, "Impossibile aprire il collegamento nel browser.");
        }
    }

    private void ManageAttachments_Click(object sender, RoutedEventArgs e)
    {
        var window = new AttachmentsWindow(store, AmbitiAllegato.Evento, eventId, HeaderText.Text) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            Changed = true;
            Load();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        RecordNavigator.EditEvent(this, store, eventId);
        Changed = true;
        Load();
        if (IsVisible)
        {
            Feedback.Success(StatusMessage, "Scheda aggiornata.");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
