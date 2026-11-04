using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HistoricEditor;

// Sezione Cariche della finestra principale: tutte le cariche (es. «Imperatore del Sacro Romano Impero») e i loro titolari.
public partial class OfficesSection : UserControl
{
    private PeopleStore? store;
    private List<OfficeChronology> offices = [];

    // Sollevato quando dalla sezione si sono potute cambiare schede di personaggi.
    public event EventHandler? DataChanged;

    public OfficesSection()
    {
        InitializeComponent();
    }

    private Window Owner => Window.GetWindow(this);

    private OfficeChronology? SelectedOffice => OfficeList.SelectedItem as OfficeChronology;

    private OfficeRow? SelectedHolder => HoldersGrid.SelectedItem as OfficeRow;

    public void Activate(PeopleStore peopleStore)
    {
        store = peopleStore;
        Reload(SelectedOffice?.Key, SelectedHolder?.Mandato.PersonId);
    }

    private void Reload(string? officeKey, int? personId)
    {
        if (store is null)
        {
            return;
        }

        offices = OfficeSuccession.Build(store.LoadAll());
        ApplyFilter(officeKey);
        if (personId is int id && HoldersGrid.ItemsSource is IEnumerable<OfficeRow> holders)
        {
            HoldersGrid.SelectedItem = holders.FirstOrDefault(r => r.Mandato.PersonId == id);
        }
    }

    private void ApplyFilter(string? selectKey)
    {
        var text = OfficeSuccession.Normalize(SearchBox.Text);
        var visible = offices
            .Where(o => text.Length == 0 || OfficeSuccession.Normalize(o.Nome).Contains(text, StringComparison.Ordinal))
            .Where(o => OnlyMultipleCheck.IsChecked != true || o.Righe.Select(r => r.Mandato.PersonId).Distinct().Count() > 1)
            .ToList();
        OfficeList.ItemsSource = visible;
        OfficeList.SelectedItem = visible.FirstOrDefault(o => o.Key == selectKey) ?? visible.FirstOrDefault();
        if (OfficeList.SelectedItem is not null)
        {
            OfficeList.ScrollIntoView(OfficeList.SelectedItem);
        }

        CountText.Text = visible.Count == offices.Count
            ? $"{offices.Count} {(offices.Count == 1 ? "carica" : "cariche")}"
            : $"{visible.Count} di {offices.Count} cariche";
        EmptyText.Text = offices.Count == 0
            ? "Ancora nessuna carica.\n\nLe cariche nascono dai titoli delle schede dei personaggi (scheda Titoli, oppure importando da Wikipedia)."
            : "Nessuna carica corrisponde alla ricerca.";
        ShowOffice();
    }

    private void ShowOffice()
    {
        var office = SelectedOffice;
        OfficeTitle.Text = office?.Nome ?? string.Empty;
        OfficeSummary.Text = office is null ? string.Empty
            : office.WikidataId.Length > 0 ? $"{office.Riepilogo} · Wikidata {office.WikidataId}" : office.Riepilogo;
        HoldersGrid.ItemsSource = office?.Righe;
        HoldersGrid.Visibility = office is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = office is null ? Visibility.Visible : Visibility.Collapsed;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        ViewButton.IsEnabled = SelectedHolder is not null;
        EditButton.IsEnabled = SelectedHolder is not null;
        ChronologyButton.IsEnabled = SelectedOffice is not null;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter(SelectedOffice?.Key);

    private void OfficeList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowOffice();

    private void HoldersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommands();

    private void HoldersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(HoldersGrid, source) is DataGridRow)
        {
            Open(RecordNavigator.OpenPerson);
        }
    }

    private void HoldersGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedHolder is not null)
        {
            e.Handled = true;
            Open(RecordNavigator.OpenPerson);
        }
    }

    private void View_Click(object sender, RoutedEventArgs e) => Open(RecordNavigator.ViewPerson);

    private void Edit_Click(object sender, RoutedEventArgs e) => Open(RecordNavigator.EditPerson);

    private void Chronology_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || SelectedOffice is not { } office)
        {
            return;
        }

        var window = new OfficesWindow(store, officeKey: office.Key) { Owner = Owner };
        window.ShowDialog();
        if (window.Changed)
        {
            AfterChange(office.Key, SelectedHolder?.Mandato.PersonId);
        }
    }

    private void Open(Func<Window, PeopleStore, int, bool> open)
    {
        if (store is not null && SelectedHolder is { } row && open(Owner, store, row.Mandato.PersonId))
        {
            AfterChange(SelectedOffice?.Key, row.Mandato.PersonId);
        }
    }

    private void AfterChange(string? officeKey, int? personId)
    {
        Reload(officeKey, personId);
        DataChanged?.Invoke(this, EventArgs.Empty);
        HoldersGrid.Focus();
    }
}
