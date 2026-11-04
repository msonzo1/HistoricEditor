using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HistoricEditor;

// Sezione Dinastie della finestra principale: albero delle casate con i rami e, per ciascuna, i membri.
public partial class DynastiesSection : UserControl
{
    private PeopleStore? store;
    private IReadOnlyList<Dinastia> dynasties = [];
    private List<HistoricalPerson> people = [];

    // Sollevato quando dalla sezione si sono potute cambiare schede o dinastie.
    public event EventHandler? DataChanged;

    // Chiede alla finestra principale di mostrare gli eventi della dinastia indicata.
    public event EventHandler<int>? ShowEventsRequested;

    public DynastiesSection()
    {
        InitializeComponent();
    }

    private Window Owner => Window.GetWindow(this);

    private Dinastia? SelectedDynasty => (DynastyTreeView.SelectedItem as DynastyNode)?.Dinastia;

    private DynastyMemberRow? SelectedMember => MembersGrid.SelectedItem as DynastyMemberRow;

    public void Activate(PeopleStore peopleStore)
    {
        store = peopleStore;
        Reload(SelectedDynasty?.Id, SelectedMember?.Id);
    }

    private void Reload(int? dynastyId, int? personId)
    {
        if (store is null)
        {
            return;
        }

        dynasties = store.GetDynasties();
        people = store.LoadAll();
        var roots = DynastyTree.Build(dynasties);
        var all = roots.SelectMany(r => r.SelfAndDescendants()).ToList();
        foreach (var root in roots)
        {
            root.IsExpanded = true;
        }

        var selected = all.FirstOrDefault(n => n.Dinastia.Id == dynastyId) ?? all.FirstOrDefault();
        if (selected is not null)
        {
            foreach (var node in all.Where(n => n.SelfAndDescendants().Contains(selected)))
            {
                node.IsExpanded = true;
            }

            selected.IsSelected = true;
        }

        DynastyTreeView.ItemsSource = roots;
        CountText.Text = $"{dynasties.Count} {(dynasties.Count == 1 ? "dinastia" : "dinastie")}";
        ShowDynasty(selected?.Dinastia);
        if (personId is int id && MembersGrid.ItemsSource is IEnumerable<DynastyMemberRow> members)
        {
            MembersGrid.SelectedItem = members.FirstOrDefault(m => m.Id == id);
        }
    }

    private void ShowDynasty(Dinastia? dynasty)
    {
        var members = dynasty is null ? [] : DynastySheet.MemberRows(dynasty, dynasties, people);
        DynastyTitle.Text = dynasty is null ? string.Empty : DynastySheet.Path(dynasty, dynasties);
        DynastySummary.Text = dynasty is null ? string.Empty
            : $"{members.Count} {(members.Count == 1 ? "membro" : "membri")} nell'archivio"
                + (dynasty.WikidataId.Trim().Length > 0 ? $" · Wikidata {dynasty.WikidataId.Trim()}" : string.Empty);
        DynastyNotes.Text = dynasty?.Note ?? string.Empty;
        DynastyNotes.Visibility = RichNotes.IsEmpty(dynasty?.Note) ? Visibility.Collapsed : Visibility.Visible;
        var attachments = dynasty is null || store is null ? 0 : store.GetAttachments(AmbitiAllegato.Dinastia, dynasty.Id).Count;
        AttachmentsButtonText.Text = attachments > 0 ? $"Allegati ({attachments})…" : "Allegati…";
        MembersGrid.ItemsSource = members;
        EmptyText.Text = dynasties.Count == 0
            ? "Ancora nessuna dinastia.\n\nUsa «Gestisci dinastie…» per creare casate e rami (es. Asburgo di Spagna sotto Asburgo)."
            : "Nessun membro: il campo Famiglia delle schede deve contenere il nome della dinastia o di un suo ramo.";
        EmptyText.Visibility = members.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        ViewButton.IsEnabled = SelectedMember is not null;
        EditButton.IsEnabled = SelectedMember is not null;
        EventsButton.IsEnabled = SelectedDynasty is not null;
        TimelineButton.IsEnabled = SelectedDynasty is not null;
        AttachmentsButton.IsEnabled = SelectedDynasty is not null;
    }

    private void Attachments_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || SelectedDynasty is not { } dynasty)
        {
            return;
        }

        var window = new AttachmentsWindow(store, AmbitiAllegato.Dinastia, dynasty.Id, dynasty.Nome) { Owner = Owner };
        window.ShowDialog();
        if (window.Changed)
        {
            Reload(dynasty.Id, SelectedMember?.Id);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DynastyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => ShowDynasty(SelectedDynasty);

    private void MembersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommands();

    private void MembersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(MembersGrid, source) is DataGridRow)
        {
            Open(RecordNavigator.OpenPerson);
        }
    }

    private void MembersGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedMember is not null)
        {
            e.Handled = true;
            Open(RecordNavigator.OpenPerson);
        }
    }

    private void View_Click(object sender, RoutedEventArgs e) => Open(RecordNavigator.ViewPerson);

    private void Edit_Click(object sender, RoutedEventArgs e) => Open(RecordNavigator.EditPerson);

    private void Manage_Click(object sender, RoutedEventArgs e)
    {
        if (store is null)
        {
            return;
        }

        new DynastiesWindow(store, SelectedDynasty?.Nome) { Owner = Owner }.ShowDialog();
        AfterChange(SelectedDynasty?.Id, SelectedMember?.Id);
    }

    private void Events_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDynasty is { } dynasty)
        {
            ShowEventsRequested?.Invoke(this, dynasty.Id);
        }
    }

    private void Timeline_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || SelectedDynasty is not { } dynasty)
        {
            return;
        }

        var window = new TimelineWindow(store, SelectedMember?.Id, dynasty.Nome) { Owner = Owner };
        window.ShowDialog();
        if (window.Changed)
        {
            AfterChange(dynasty.Id, SelectedMember?.Id);
        }
    }

    private void Open(Func<Window, PeopleStore, int, bool> open)
    {
        if (store is not null && SelectedMember is { } row && open(Owner, store, row.Id))
        {
            AfterChange(SelectedDynasty?.Id, row.Id);
        }
    }

    private void AfterChange(int? dynastyId, int? personId)
    {
        Reload(dynastyId, personId);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
