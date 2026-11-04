using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class PersonPickerWindow : Window
{
    private readonly PeopleStore store;
    private readonly string newPersonSex;
    private readonly int excludeId;
    private readonly IReadOnlyList<PersonSummary> all;

    public int? SelectedId { get; private set; }

    public PersonPickerWindow(PeopleStore store, string title, int excludeId, string newPersonSex)
    {
        InitializeComponent();
        this.store = store;
        this.newPersonSex = newPersonSex;
        this.excludeId = excludeId;
        Title = title;
        all = [.. store.GetPersonSummaries().Where(p => p.Id != excludeId)];
        ApplyFilter();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        PeopleList.ItemsSource = query.Length == 0
            ? all
            : all.Where(p => compare.IndexOf(p.Nome, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToList();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void PeopleList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Select_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (PeopleList.SelectedItem is not PersonSummary person)
        {
            PickerMessage.Text = "Seleziona una persona.";
            return;
        }

        SelectedId = person.Id;
        DialogResult = true;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NewNameBox.Text.Trim();
        if (name.Length == 0)
        {
            PickerMessage.Text = "Scrivi il nome della nuova scheda.";
            NewNameBox.Focus();
            return;
        }

        var duplicates = store.FindPossibleDuplicates(new HistoricalPerson { Name = name }, excludeId);
        if (duplicates.Count > 0)
        {
            var list = string.Join(Environment.NewLine, duplicates.Take(5).Select(d => $"• {d.Etichetta}"));
            var answer = MessageBox.Show(
                this,
                $"In archivio c’è già una scheda con questo nome:{Environment.NewLine}{Environment.NewLine}{list}{Environment.NewLine}{Environment.NewLine}Sì: usa la scheda esistente ({duplicates[0].Etichetta}).{Environment.NewLine}No: crea comunque una nuova scheda.{Environment.NewLine}Annulla: torna alla scelta.",
                "Scheda già presente",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel)
            {
                return;
            }

            if (answer == MessageBoxResult.Yes)
            {
                SelectedId = duplicates[0].Id;
                DialogResult = true;
                return;
            }
        }

        try
        {
            SelectedId = store.CreatePerson(name, newPersonSex);
        }
        catch (DbUpdateException)
        {
            PickerMessage.Text = "Impossibile creare la scheda.";
            return;
        }

        DialogResult = true;
    }
}
