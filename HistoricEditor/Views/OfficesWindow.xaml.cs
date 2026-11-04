using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HistoricEditor;

// Elenco delle cariche dell'archivio e, per ciascuna, la cronologia dei titolari con predecessori e successori.
public partial class OfficesWindow : Window
{
    private readonly PeopleStore store;
    private List<OfficeChronology> offices = [];
    private int? focusPersonId;

    // Vero se dalla finestra sono state modificate delle schede.
    public bool Changed { get; private set; }

    private readonly PersonTitle? focusTitle;

    // Con un titolo del personaggio, si apre sulla carica di quel titolo invece che su quella principale;
    // con officeKey si apre direttamente sulla carica indicata.
    public OfficesWindow(PeopleStore store, int? personId = null, PersonTitle? title = null, string? officeKey = null)
    {
        InitializeComponent();
        this.store = store;
        focusPersonId = personId;
        focusTitle = title;
        Reload(officeKey);
        Loaded += (_, _) => (OfficeList.SelectedItem is null ? (Control)SearchBox : ChronologyGrid).Focus();
    }

    private void Reload(string? selectKey)
    {
        offices = OfficeSuccession.Build(store.LoadAll());
        // Aperta da un personaggio, mostra la sua carica principale (o la prima).
        if (selectKey is null && focusPersonId is int person && store.GetPerson(person) is { } focus)
        {
            var main = OfficeSuccession.Normalize(focus.MainTitle);
            var mine = offices.Where(o => o.Righe.Any(r => r.Mandato.PersonId == person)).ToList();
            var titled = focusTitle is { } t
                ? mine.FirstOrDefault(o => o.Righe.Any(r => r.Mandato.PersonId == person
                    && OfficeSuccession.Normalize(r.Mandato.Titolo.Titolo) == OfficeSuccession.Normalize(t.Titolo)
                    && r.Mandato.Titolo.Inizio.Trim() == t.Inizio.Trim()))
                : null;
            selectKey = (titled ?? mine.FirstOrDefault(o => OfficeSuccession.Normalize(o.Nome) == main) ?? mine.FirstOrDefault())?.Key;
        }

        ApplyFilter(selectKey);
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

        if (visible.Count == 0)
        {
            ShowOffice(null);
            OfficeSummary.Text = offices.Count == 0
                ? "Nessuna carica in archivio: aggiungi i titoli nella scheda dei personaggi o importali da Wikipedia."
                : "Nessuna carica corrisponde alla ricerca.";
        }
    }

    private void Search_TextChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter((OfficeList.SelectedItem as OfficeChronology)?.Key);
        }
    }

    private void OfficeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ShowOffice(OfficeList.SelectedItem as OfficeChronology);

    private void ShowOffice(OfficeChronology? office)
    {
        ChronologyGrid.ItemsSource = office?.Righe;
        MissingList.ItemsSource = office?.Mancanti;
        OfficeTitle.Text = office?.Nome ?? string.Empty;
        if (office is null)
        {
            OfficeSummary.Text = string.Empty;
            return;
        }

        var notes = office.Righe.Sum(r => r.Note.Count);
        var parts = new List<string> { office.Riepilogo };
        if (office.WikidataId.Length > 0)
        {
            parts.Add($"Wikidata {office.WikidataId}");
        }

        parts.Add(notes == 0 ? "nessuna segnalazione" : $"{notes} {(notes == 1 ? "segnalazione" : "segnalazioni")}");
        if (office.Mancanti.Count > 0)
        {
            parts.Add($"{office.Mancanti.Count} citati mancanti");
        }

        OfficeSummary.Text = string.Join(" · ", parts);
        var focusRow = office.Righe.FirstOrDefault(r => r.Mandato.PersonId == focusPersonId);
        ChronologyGrid.SelectedItem = focusRow;
        if (focusRow is not null)
        {
            ChronologyGrid.ScrollIntoView(focusRow);
        }
    }

    private void ChronologyGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => OpenButton.IsEnabled = ChronologyGrid.SelectedItem is OfficeRow;

    private void ChronologyGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ChronologyGrid, source) is DataGridRow)
        {
            OpenSelected();
        }
    }

    private void ChronologyGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ChronologyGrid.SelectedItem is OfficeRow)
        {
            e.Handled = true;
            OpenSelected();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (ChronologyGrid.SelectedItem is not OfficeRow row || store.GetPerson(row.Mandato.PersonId) is not { } person)
        {
            return;
        }

        var editor = new PersonEditorWindow(store, existing: person) { Owner = this };
        editor.ShowDialog();
        if (!editor.Saved)
        {
            return;
        }

        Changed = true;
        focusPersonId = row.Mandato.PersonId;
        Reload((OfficeList.SelectedItem as OfficeChronology)?.Key);
        StatusText.Text = $"Scheda di {person.DisplayName} aggiornata: cronologia ricalcolata.";
    }
}
