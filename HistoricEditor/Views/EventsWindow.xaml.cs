using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class EventsWindow : Window
{
    private sealed record EventItem(Evento Evento)
    {
        public string Dettaglio => string.Join(" · ", new[] { Evento.DataTesto, Evento.Tipo }.Where(s => s.Length > 0));

        public string Etichetta => Dettaglio.Length > 0 ? $"{Evento.Titolo}, {Dettaglio}" : Evento.Titolo;
    }

    private sealed class ParticipantRow(int personaId, string nome, string ruolo)
    {
        public int PersonaId { get; } = personaId;
        public string Nome { get; } = nome;
        public string Ruolo { get; set; } = ruolo;
    }

    private readonly PeopleStore store;
    private readonly int? focusPersonId;
    private readonly ObservableCollection<ParticipantRow> participants = [];
    private Dictionary<int, PersonSummary> people = [];
    private List<EventItem> items = [];
    private int currentId;
    private int? placeId;
    private int? specificPlaceId;
    private bool loading;

    // Vero se è stata creata almeno una nuova scheda dal selettore dei partecipanti.
    public bool PeopleChanged { get; private set; }

    // Con focusPersonId l'elenco può limitarsi agli eventi della persona, che diventa partecipante dei nuovi eventi.
    // selectEventId apre direttamente quell'evento; onlyFocusPerson stabilisce lo stato iniziale del filtro.
    public EventsWindow(PeopleStore store, int? focusPersonId = null, int? selectEventId = null, bool onlyFocusPerson = true)
    {
        InitializeComponent();
        this.store = store;
        this.focusPersonId = focusPersonId;
        Closed += (_, _) => wikiCancellation?.Cancel();
        QualifierBox.ItemsSource = DataEvento.Qualificatori;
        CalendarBox.ItemsSource = HistoricalCalendar.Calendari;
        StyleBox.ItemsSource = HistoricalCalendar.Stili;
        ParticipantList.ItemsSource = participants;
        LoadPeople();
        if (focusPersonId is int id && people.TryGetValue(id, out var person))
        {
            PersonFilter.Content = $"Solo eventi di {person.Nome}";
            PersonFilter.Visibility = Visibility.Visible;
            PersonFilter.IsChecked = onlyFocusPerson;
        }

        Reload(selectEventId);
        Loaded += (_, _) =>
        {
            if (selectEventId is null && focusPersonId is not null && onlyFocusPerson)
            {
                TitleBox.Focus();
            }
            else
            {
                SearchBox.Focus();
            }
        };
    }

    private void LoadPeople() => people = store.GetPersonSummaries().ToDictionary(p => p.Id);

    private void Reload(int? selectId)
    {
        items = [.. store.GetEvents().Select(e => new EventItem(e))];
        TypeBox.ItemsSource = store.GetEventTypes();
        ApplyFilter(selectId);
    }

    private void ApplyFilter(int? selectId)
    {
        var query = SearchBox.Text.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        bool Matches(string text) => compare.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

        var visible = items.Where(i =>
                (query.Length == 0 || Matches(i.Evento.Titolo) || Matches(i.Evento.Tipo))
                && (PersonFilter.IsChecked != true || i.Evento.Partecipanti.Any(p => p.PersonaId == focusPersonId)))
            .ToList();
        EventList.ItemsSource = visible;
        var selected = visible.FirstOrDefault(i => i.Evento.Id == selectId);
        if (selected is not null)
        {
            EventList.SelectedItem = selected;
            EventList.ScrollIntoView(selected);
        }
        else if (selectId is null)
        {
            ShowEvent(null);
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded || sender == PersonFilter)
        {
            ApplyFilter(currentId == 0 ? null : currentId);
        }
    }

    private void EventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventList.SelectedItem is EventItem item)
        {
            ShowEvent(item.Evento);
        }
    }

    private void ShowEvent(Evento? evento)
    {
        loading = true;
        currentId = evento?.Id ?? 0;
        TitleBox.Text = evento?.Titolo ?? string.Empty;
        TypeBox.Text = evento?.Tipo ?? string.Empty;
        QualifierBox.SelectedItem = DataEvento.Info(evento?.DataQualificatore);
        Date1Box.Text = evento?.Data1 ?? string.Empty;
        Date2Box.Text = evento?.Data2 ?? string.Empty;
        CalendarBox.SelectedItem = HistoricalCalendar.CalendarInfo(evento?.Calendario);
        StyleBox.SelectedItem = HistoricalCalendar.StyleInfo(evento?.Stile);
        DescriptionBox.Text = evento?.Descrizione ?? string.Empty;
        WikidataBox.Text = evento?.WikidataId ?? string.Empty;
        SetPlace(evento?.LuogoId, evento?.LuogoSpecificoId);
        participants.Clear();
        if (evento is not null)
        {
            foreach (var p in evento.Partecipanti.OrderBy(p => PersonLabel(p.PersonaId), StringComparer.CurrentCultureIgnoreCase))
            {
                participants.Add(new ParticipantRow(p.PersonaId, PersonLabel(p.PersonaId), p.Ruolo));
            }
        }
        else if (focusPersonId is int id && people.ContainsKey(id))
        {
            participants.Add(new ParticipantRow(id, PersonLabel(id), string.Empty));
        }

        DeleteButton.IsEnabled = evento is not null;
        AttachmentsButton.IsEnabled = evento is not null;
        loading = false;
        UpdateDateFields();
        EditorMessage.Text = string.Empty;
    }

    private string PersonLabel(int personId) => people.TryGetValue(personId, out var p) ? p.Etichetta : $"Scheda {personId}";

    private void SetPlace(int? luogoId, int? specificoId)
    {
        placeId = luogoId;
        specificPlaceId = specificoId;
        PlaceBox.Text = luogoId is null && specificoId is null ? string.Empty : store.DescribeRole(luogoId, specificoId).DisplayText;
        ClearPlaceButton.IsEnabled = luogoId is not null || specificoId is not null;
    }

    private void ChoosePlace_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PlacePickerWindow(store, "Luogo dell’evento", allowSpecific: true, placeId, specificPlaceId, allowClear: true) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            SetPlace(picker.ResultLuogoId, picker.ResultSpecificoId);
            PlaceBox.Focus();
        }
    }

    private void ClearPlace_Click(object sender, RoutedEventArgs e)
    {
        SetPlace(null, null);
        PlaceBox.Focus();
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (!loading)
        {
            UpdateDateFields();
        }
    }

    // Mostra la seconda data solo per gli intervalli e un'anteprima di come si leggerà la data.
    private void UpdateDateFields()
    {
        var info = QualifierBox.SelectedItem as DataQualificatoreInfo ?? DataEvento.Qualificatori[0];
        var visibility = info.Intervallo ? Visibility.Visible : Visibility.Collapsed;
        Date2Label.Visibility = visibility;
        Date2Box.Visibility = visibility;
        Date2Label.Content = info.Separatore;
        Date1Label.Content = info.Codice switch
        {
            DataEvento.DaA => "_Data di inizio",
            DataEvento.Tra => "_Date estreme",
            _ => "_Data",
        };
        Date1Box.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, info.Intervallo ? "Prima data" : "Data dell’evento");
        Date2Box.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, info.Codice == DataEvento.DaA ? "Data di fine" : "Seconda data");

        var calendar = SelectedCalendar;
        var style = SelectedStyle;
        var preview = DataEvento.Format(info.Codice, Date1Box.Text, Date2Box.Text);
        var error = DataEvento.Validate(info.Codice, Date1Box.Text, Date2Box.Text, style);
        var lines = new List<string>();
        if (error is not null)
        {
            lines.Add(error);
        }
        else if (preview.Length > 0)
        {
            var suffix = HistoricalCalendar.Suffix(calendar, style);
            lines.Add($"Si leggerà: {preview}{(suffix.Length > 0 ? $" ({suffix})" : string.Empty)}");
        }

        var first = HistoricalCalendar.Describe(Date1Box.Text, calendar, style);
        var second = info.Intervallo ? HistoricalCalendar.Describe(Date2Box.Text, calendar, style) : string.Empty;
        if (first.Length > 0)
        {
            lines.Add(second.Length > 0 ? $"Prima data. {first}" : first);
        }

        if (second.Length > 0)
        {
            lines.Add($"Seconda data. {second}");
        }

        DatePreview.Text = string.Join(Environment.NewLine, lines);
        var styleInfo = HistoricalCalendar.StyleInfo(style);
        StyleBox.ToolTip = styleInfo.Descrizione;
        System.Windows.Automation.AutomationProperties.SetHelpText(StyleBox, styleInfo.Descrizione);
    }

    private string SelectedCalendar => (CalendarBox.SelectedItem as CalendarOption)?.Codice ?? HistoricalCalendar.NonIndicato;

    private string SelectedStyle => (StyleBox.SelectedItem as YearStyleOption)?.Codice ?? HistoricalCalendar.StileComune;

    private void OpenConverter_Click(object sender, RoutedEventArgs e)
    {
        var calendar = SelectedCalendar;
        new CalendarConverterWindow(Date1Box.Text, calendar.Length > 0 ? calendar : HistoricalCalendar.Giuliano, SelectedStyle) { Owner = this }.Show();
    }

    private void ParticipantList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RemoveParticipantButton.IsEnabled = ParticipantList.SelectedItem is not null;

    private void AddParticipant_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PersonPickerWindow(store, "Aggiungi partecipante", 0, string.Empty) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedId is not int id)
        {
            return;
        }

        if (!people.ContainsKey(id))
        {
            PeopleChanged = true;
            LoadPeople();
        }

        var existing = participants.FirstOrDefault(p => p.PersonaId == id);
        if (existing is not null)
        {
            ParticipantList.SelectedItem = existing;
            EditorMessage.Text = $"{existing.Nome} è già tra i partecipanti.";
            return;
        }

        var row = new ParticipantRow(id, PersonLabel(id), string.Empty);
        participants.Add(row);
        ParticipantList.SelectedItem = row;
        ParticipantList.ScrollIntoView(row);
        EditorMessage.Text = string.Empty;
    }

    private void RemoveParticipant_Click(object sender, RoutedEventArgs e)
    {
        if (ParticipantList.SelectedItem is ParticipantRow row)
        {
            var index = participants.IndexOf(row);
            participants.Remove(row);
            if (participants.Count > 0)
            {
                ParticipantList.SelectedIndex = Math.Min(index, participants.Count - 1);
            }
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        EventList.SelectedItem = null;
        ShowEvent(null);
        TitleBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var data = new Evento
        {
            Id = currentId,
            Titolo = TitleBox.Text,
            Tipo = TypeBox.Text,
            DataQualificatore = (QualifierBox.SelectedItem as DataQualificatoreInfo)?.Codice ?? DataEvento.Esatta,
            Data1 = Date1Box.Text,
            Data2 = Date2Box.Text,
            Calendario = SelectedCalendar,
            Stile = SelectedStyle,
            Descrizione = DescriptionBox.Text,
            WikidataId = WikidataBox.Text,
            LuogoId = placeId,
            LuogoSpecificoId = specificPlaceId,
            Partecipanti = [.. participants.Select(p => new EventoPartecipante { PersonaId = p.PersonaId, Ruolo = p.Ruolo })],
        };

        try
        {
            var id = store.SaveEvent(data);
            currentId = id;
            Reload(id);
            if (EventList.SelectedItem is null)
            {
                // L'evento salvato non rientra nel filtro attivo: si azzera la ricerca per mostrarlo.
                SearchBox.Text = string.Empty;
                PersonFilter.IsChecked = false;
                ApplyFilter(id);
            }

            EditorMessage.Text = "Evento salvato.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            EditorMessage.Text = ex is InvalidOperationException ? ex.Message : "Impossibile salvare l’evento.";
        }
    }

    private void Attachments_Click(object sender, RoutedEventArgs e)
    {
        if (currentId == 0)
        {
            return;
        }

        new AttachmentsWindow(store, AmbitiAllegato.Evento, currentId, TitleBox.Text.Trim()) { Owner = this }.ShowDialog();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (currentId == 0)
        {
            return;
        }

        var answer = MessageBox.Show(this, $"Eliminare l’evento «{TitleBox.Text}»? Le schede dei partecipanti non vengono modificate.", "Elimina evento", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            store.DeleteEvent(currentId);
            currentId = 0;
            Reload(null);
            EditorMessage.Text = "Evento eliminato.";
        }
        catch (DbUpdateException)
        {
            EditorMessage.Text = "Impossibile eliminare l’evento.";
        }
    }
}
