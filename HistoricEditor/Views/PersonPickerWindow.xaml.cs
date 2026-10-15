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
    private readonly IReadOnlyList<PersonSummary> all;

    public int? SelectedId { get; private set; }

    public PersonPickerWindow(PeopleStore store, string title, int excludeId, string newPersonSex)
    {
        InitializeComponent();
        this.store = store;
        this.newPersonSex = newPersonSex;
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
