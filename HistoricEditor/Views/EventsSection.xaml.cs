using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HistoricEditor;

// Sezione Eventi della finestra principale: cronologia con ricerca e filtri per tipo, dinastia e personaggio.
public partial class EventsSection : UserControl
{
    private PeopleStore? store;
    private IReadOnlyList<EventRow> rows = [];
    private IReadOnlyList<Dinastia> dynasties = [];
    private List<HistoricalPerson> people = [];
    private bool loading;

    // Sollevato quando dalla sezione si sono potute cambiare schede di personaggi.
    public event EventHandler? DataChanged;

    public EventsSection()
    {
        InitializeComponent();
    }

    private Window Owner => Window.GetWindow(this);

    private EventRow? Selected => EventsGrid.SelectedItem as EventRow;

    public void Activate(PeopleStore peopleStore)
    {
        store = peopleStore;
        Reload(Selected?.Id);
    }

    // Mostra gli eventi della dinastia indicata (es. dalla sezione Dinastie).
    public void ShowDynasty(int dynastyId)
    {
        loading = true;
        SearchBox.Text = string.Empty;
        TypeCombo.SelectedIndex = 0;
        PersonCombo.SelectedIndex = 0;
        DynastyCombo.SelectedItem = DynastyCombo.Items.OfType<ChoiceOption>().FirstOrDefault(o => o.Id == dynastyId) ?? DynastyCombo.Items[0];
        loading = false;
        ApplyFilter(null);
    }

    private void Reload(int? selectId)
    {
        if (store is null)
        {
            return;
        }

        loading = true;
        var names = store.GetPersonSummaries().ToDictionary(p => p.Id, p => p.Nome);
        var places = new Dictionary<(int?, int?), string>();
        string PlaceOf(Evento e)
        {
            if (e.LuogoId is null && e.LuogoSpecificoId is null)
            {
                return string.Empty;
            }

            if (!places.TryGetValue((e.LuogoId, e.LuogoSpecificoId), out var text))
            {
                text = store.DescribeRole(e.LuogoId, e.LuogoSpecificoId).DisplayText;
                places[(e.LuogoId, e.LuogoSpecificoId)] = text;
            }

            return text;
        }

        var events = store.GetEvents();
        rows = EventSheet.Rows(events, id => names.GetValueOrDefault(id, $"Scheda {id}"), PlaceOf);
        dynasties = store.GetDynasties();
        people = store.LoadAll();

        var type = (TypeCombo.SelectedItem as ChoiceOption)?.Valore;
        TypeCombo.ItemsSource = new[] { new ChoiceOption("Tutti i tipi") }
            .Concat(store.GetEventTypes().Where(t => t.Trim().Length > 0).Select(t => new ChoiceOption(t, Valore: t)))
            .ToList();
        TypeCombo.SelectedItem = TypeCombo.Items.OfType<ChoiceOption>().FirstOrDefault(o => o.Valore == type) ?? TypeCombo.Items[0];

        var dynastyId = (DynastyCombo.SelectedItem as ChoiceOption)?.Id;
        DynastyCombo.ItemsSource = new[] { new ChoiceOption("Tutte le dinastie") }
            .Concat(dynasties.Select(d => new ChoiceOption(DynastySheet.Path(d, dynasties), d.Id))
                .OrderBy(o => o.Etichetta, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        DynastyCombo.SelectedItem = DynastyCombo.Items.OfType<ChoiceOption>().FirstOrDefault(o => o.Id == dynastyId) ?? DynastyCombo.Items[0];

        var personId = (PersonCombo.SelectedItem as ChoiceOption)?.Id;
        PersonCombo.ItemsSource = new[] { new ChoiceOption("Tutti i personaggi") }
            .Concat(events.SelectMany(e => e.Partecipanti.Select(p => p.PersonaId)).Distinct()
                .Select(id => new ChoiceOption(names.GetValueOrDefault(id, $"Scheda {id}"), id))
                .OrderBy(o => o.Etichetta, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        PersonCombo.SelectedItem = PersonCombo.Items.OfType<ChoiceOption>().FirstOrDefault(o => o.Id == personId) ?? PersonCombo.Items[0];
        loading = false;

        ApplyFilter(selectId);
    }

    private void ApplyFilter(int? selectId)
    {
        if (loading || store is null)
        {
            return;
        }

        IReadOnlySet<int>? involved = null;
        if ((DynastyCombo.SelectedItem as ChoiceOption)?.Id is int dynastyId)
        {
            involved = DynastySheet.Members(dynastyId, dynasties, people).Select(p => p.Id).ToHashSet();
        }

        if ((PersonCombo.SelectedItem as ChoiceOption)?.Id is int personId)
        {
            involved = involved is null || involved.Contains(personId) ? new HashSet<int> { personId } : new HashSet<int>();
        }

        var visible = EventSheet.Filter(rows, SearchBox.Text, (TypeCombo.SelectedItem as ChoiceOption)?.Valore, involved);
        EventsGrid.ItemsSource = visible;
        EventsGrid.SelectedItem = visible.FirstOrDefault(r => r.Id == selectId);
        if (EventsGrid.SelectedItem is not null)
        {
            EventsGrid.ScrollIntoView(EventsGrid.SelectedItem);
        }

        CountText.Text = visible.Count == rows.Count
            ? $"{rows.Count} {(rows.Count == 1 ? "evento" : "eventi")}"
            : $"{visible.Count} di {rows.Count} eventi";
        EmptyText.Text = rows.Count == 0
            ? "Ancora nessun evento.\n\nUsa «Nuovo» per registrare battaglie, trattati, incontri e scoperte con i loro partecipanti."
            : "Nessun evento corrisponde ai filtri.";
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        ViewButton.IsEnabled = Selected is not null;
        EditButton.IsEnabled = Selected is not null;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter(Selected?.Id);

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        loading = true;
        SearchBox.Text = string.Empty;
        TypeCombo.SelectedIndex = 0;
        DynastyCombo.SelectedIndex = 0;
        PersonCombo.SelectedIndex = 0;
        loading = false;
        ApplyFilter(Selected?.Id);
        SearchBox.Focus();
    }

    private void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommands();

    private void EventsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(EventsGrid, source) is DataGridRow)
        {
            Open(RecordNavigator.OpenEvent);
        }
    }

    private void EventsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected is not null)
        {
            e.Handled = true;
            Open(RecordNavigator.OpenEvent);
        }
    }

    private void View_Click(object sender, RoutedEventArgs e) => Open(RecordNavigator.ViewEvent);

    private void Edit_Click(object sender, RoutedEventArgs e) => Open((owner, s, id) => RecordNavigator.EditEvent(owner, s, id));

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (store is not null && RecordNavigator.EditEvent(Owner, store, null))
        {
            AfterChange(Selected?.Id);
        }
    }

    private void Open(Func<Window, PeopleStore, int, bool> open)
    {
        if (store is not null && Selected is { } row && open(Owner, store, row.Id))
        {
            AfterChange(row.Id);
        }
    }

    private void AfterChange(int? selectId)
    {
        Reload(selectId);
        DataChanged?.Invoke(this, EventArgs.Empty);
        EventsGrid.Focus();
    }
}
