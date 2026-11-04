using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Scheda di lettura: tutte le informazioni del personaggio in una sola pagina, senza campi modificabili.
public partial class PersonViewWindow : Window
{
    private sealed record EventItem(int Id, string Etichetta);

    private sealed record ArtworkItem(int Id, string Titolo, string Etichetta, System.Windows.Media.ImageSource? Miniatura)
    {
        public Visibility MiniaturaVisibile => Miniatura is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly PeopleStore store;
    private HistoricalPerson person;

    // Vero se dalla scheda (o da finestre aperte da qui) sono stati salvati cambiamenti all'archivio.
    public bool Changed { get; private set; }

    public PersonViewWindow(PeopleStore store, HistoricalPerson person)
    {
        InitializeComponent();
        this.store = store;
        this.person = person;
        Load();
        Loaded += (_, _) => EditButton.Focus();
    }

    private void Load()
    {
        person = store.GetPerson(person.Id) ?? person;
        Title = $"Scheda di lettura – {person.DisplayName}";
        HeaderText.Text = person.DisplayName.Length > 0 ? person.DisplayName : "Personaggio senza nome";
        HeaderSubtitle.Text = string.Join(" · ", new[] { person.LifeSpan, person.MainTitle.Trim() }.Where(s => s.Length > 0));

        MainList.ItemsSource = PersonSheet.MainFields(person);
        DynastyPathText.Text = LoadDynastyPath();
        DynastyPathText.Visibility = DynastyPathText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var languages = PersonSheet.LocalizedNames(person.LocalizedNames);
        LanguagesList.ItemsSource = languages;
        LanguagesCard.Visibility = languages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        LoadLife();

        var biography = person.WikipediaExtract.Trim();
        BiographyText.Text = biography;
        BiographyCard.Visibility = biography.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        LoadFamily();
        LoadTitles();
        LoadEvents();
        LoadArtworks();
        LoadNotesAndAttachments();

        var sources = PersonSheet.Sources(person);
        SourcesList.ItemsSource = sources;
        NoSourcesText.Visibility = sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string LoadDynastyPath()
    {
        try
        {
            return PersonSheet.DynastyPath(person.Family, store.GetDynasties());
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private void LoadLife()
    {
        var fields = new List<SheetField>();

        void AddField(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields.Add(new SheetField(label, value.Trim()));
            }
        }

        void AddDate(string label, string date, string calendario, string stile)
        {
            AddField(label, PersonSheet.DescribeDate(date, calendario, stile));
            AddField("Nota sul calendario", HistoricalCalendar.Describe(date, calendario, stile));
        }

        void AddPlace(string label, string fallback, int? luogoId, int? specificoId, List<HistoricalPlaceRecord> places, string contextLabel, int? year)
        {
            // Col nome dell'epoca: «Costantinopoli (oggi Istanbul)».
            var text = luogoId is null && specificoId is null ? fallback : store.DescribeRoleAt(luogoId, specificoId, year).DisplayText;
            AddField(label, text.Trim().Length > 0 ? text : fallback);
            AddField(contextLabel, PersonSheet.DescribeHistoricalContext(places));
        }

        var birthYear = HistoricalCalendar.ModernYear(person.Birth, person.BirthStile);
        var deathYear = HistoricalCalendar.ModernYear(person.Death, person.DeathStile);
        AddDate("Nascita", person.Birth, person.BirthCalendario, person.BirthStile);
        AddPlace("Luogo di nascita", person.BirthPlace, person.BirthLuogoId, person.BirthLuogoSpecificoId, person.BirthPlaces, "Contesto storico (nascita)", birthYear);
        AddDate("Morte", person.Death, person.DeathCalendario, person.DeathStile);
        AddPlace("Luogo di morte", person.DeathPlace, person.DeathLuogoId, person.DeathLuogoSpecificoId, person.DeathPlaces, "Contesto storico (morte)", deathYear);
        AddPlace("Sepoltura", person.BurialPlace, person.BurialLuogoId, person.BurialLuogoSpecificoId, person.BurialPlaces, "Contesto storico (sepoltura)", deathYear);

        LifeList.ItemsSource = fields;
        LifeEmptyText.Visibility = fields.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var showNotice = DateDiscrepancy.HasAny(person.BirthDateEvidence, person.DeathDateEvidence);
        DateNoticePanel.Visibility = showNotice ? Visibility.Visible : Visibility.Collapsed;
        DateNoticeText.Text = showNotice ? DateDiscrepancy.DescribeAll(person.BirthDateEvidence, person.DeathDateEvidence) : string.Empty;
    }

    private void LoadNotesAndAttachments()
    {
        NotesView.Text = person.Note;
        NotesCard.Visibility = RichNotes.IsEmpty(person.Note) ? Visibility.Collapsed : Visibility.Visible;
        AttachmentsView.Load(store, AmbitiAllegato.Persona, person.Id);
        var portrait = store.GetPortrait(person.Id);
        PortraitImage.Source = portrait is null ? null : ArtworkImages.LoadThumbnail(store.AttachmentsDirectory, portrait.File);
        PortraitFrame.Visibility = PortraitImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(PortraitImage, $"Ritratto di {person.DisplayName}");
    }

    private void ManageAttachments_Click(object sender, RoutedEventArgs e)
    {
        var window = new AttachmentsWindow(store, AmbitiAllegato.Persona, person.Id, person.DisplayName) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            Changed = true;
            Reload();
        }
    }

    private void LoadFamily()
    {
        var family = store.GetFamily(person.Id);
        SetPersonLink(FatherLink, family.Padre, "padre");
        SetPersonLink(MotherLink, family.Madre, "madre");

        var unions = family.Unioni.Where(u => u.HasConiuge || u.Figli.Count > 0).ToList();
        UnionsList.ItemsSource = unions;
        NoUnionsText.Visibility = unions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        OtherChildrenList.ItemsSource = family.AltriFigli;
        OtherChildrenPanel.Visibility = family.AltriFigli.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var relations = store.GetRelationsForPerson(person.Id);
        RelationsList.ItemsSource = relations;
        NoRelationsText.Visibility = relations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetPersonLink(Button link, PersonSummary? relative, string role)
    {
        link.Content = relative?.Etichetta ?? "Non indicato";
        link.Tag = relative?.Id;
        link.IsEnabled = relative is not null;
        link.ToolTip = relative is null ? null : "Apri la scheda di lettura";
        System.Windows.Automation.AutomationProperties.SetName(link, relative is null ? $"{role}: non indicato" : $"Apri la scheda di {relative.Etichetta}");
    }

    private void LoadTitles()
    {
        var titles = PersonSheet.Titles(person.Titles);
        TitlesList.ItemsSource = titles;
        NoTitlesText.Visibility = titles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadEvents()
    {
        var events = store.GetEventsForPerson(person.Id)
            .Select(e =>
            {
                var role = e.Partecipanti.FirstOrDefault(p => p.PersonaId == person.Id)?.Ruolo ?? string.Empty;
                var details = string.Join(" · ", new[] { e.DataTesto, e.Tipo, role }.Where(s => !string.IsNullOrWhiteSpace(s)));
                return new EventItem(e.Id, details.Length > 0 ? $"{e.Titolo} — {details}" : e.Titolo);
            })
            .ToList();
        EventsList.ItemsSource = events;
        NoEventsText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadArtworks()
    {
        var names = store.GetPersonSummaries().ToDictionary(p => p.Id, p => p.Nome);
        var artworks = store.GetArtworksForPerson(person.Id)
            .Select(o => new ArtworkItem(
                o.Id,
                o.Titolo,
                OperaRuoli.DescribeForPerson(o, person.Id, id => names.GetValueOrDefault(id, string.Empty)),
                ArtworkImages.LoadThumbnail(store.ImagesDirectory, o.Immagine)))
            .ToList();
        ArtworksList.ItemsSource = artworks;
        NoArtworksText.Visibility = artworks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenArtwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ArtworkItem item })
        {
            return;
        }

        if (RecordNavigator.ViewArtwork(this, store, item.Id))
        {
            Changed = true;
        }

        Reload();
    }

    private void Reload()
    {
        if (store.GetPerson(person.Id) is null)
        {
            Close();
            return;
        }

        Load();
    }

    private void OpenPerson_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int id } || store.GetPerson(id) is not { } relative)
        {
            return;
        }

        var window = new PersonViewWindow(store, relative) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            Changed = true;
            Reload();
        }
    }

    private void OpenTitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SheetTitle title })
        {
            return;
        }

        var window = new OfficesWindow(store, person.Id, title.Titolo) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            Changed = true;
            Reload();
        }
    }

    private void OpenEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EventItem item })
        {
            return;
        }

        if (RecordNavigator.ViewEvent(this, store, item.Id))
        {
            Changed = true;
        }

        Reload();
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SheetLink link } || PersonEditorWindow.SourceLinkUri(link.Url) is not { } uri)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Feedback.Error(StatusMessage, "Impossibile aprire il collegamento nel browser.");
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var editor = new PersonEditorWindow(store, existing: person) { Owner = this };
        editor.ShowDialog();
        if (editor.Saved)
        {
            Changed = true;
            if (editor.Deleted)
            {
                Close();
                return;
            }

            Reload();
            Feedback.Success(StatusMessage, "Scheda aggiornata.");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
