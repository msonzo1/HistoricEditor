using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Dinastia, gens di nascita e gens d'adozione di un personaggio romano: si modificano solo qui, non nella scheda.
public partial class DynastyGensWindow : Window
{
    private readonly PeopleStore store;
    private IReadOnlyList<Dinastia> dynasties = [];

    public string ResultFamily { get; private set; } = string.Empty;
    public string ResultGens { get; private set; } = string.Empty;
    public string ResultAdoptiveGens { get; private set; } = string.Empty;

    public DynastyGensWindow(PeopleStore store, string family, string gens, string adoptiveGens)
    {
        InitializeComponent();
        this.store = store;
        DynastyInput.Text = family;
        GensInput.Text = gens;
        AdoptiveGensInput.Text = adoptiveGens;
        LoadChoices();
        Loaded += (_, _) => DynastyInput.Focus();
    }

    private void LoadChoices()
    {
        var (family, gens, adoptive) = (DynastyInput.Text, GensInput.Text, AdoptiveGensInput.Text);
        try
        {
            dynasties = store.GetDynasties();
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            dynasties = [];
        }

        DynastyInput.ItemsSource = dynasties.Where(d => !d.IsGens).Select(d => d.Nome).ToList();
        var gentes = dynasties.Where(d => d.IsGens).Select(d => d.Nome).ToList();
        GensInput.ItemsSource = gentes;
        AdoptiveGensInput.ItemsSource = gentes;
        DynastyInput.Text = family;
        GensInput.Text = gens;
        AdoptiveGensInput.Text = adoptive;
        UpdatePaths();
    }

    private void UpdatePaths()
    {
        DynastyPathText.Text = DynastyDescriptions.Hint(dynasties, DynastyInput.Text, gens: false);
        GensPathText.Text = GensInput.Text.Trim().Length == 0
            ? "Non indicata: è quasi sempre nota (la scheda si salva comunque)."
            : DynastyDescriptions.Hint(dynasties, GensInput.Text, gens: true);
        AdoptiveGensPathText.Text = DynastyDescriptions.Hint(dynasties, AdoptiveGensInput.Text, gens: true);
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DynastyPathText is not null && GensPathText is not null && AdoptiveGensPathText is not null)
        {
            UpdatePaths();
        }
    }

    private void EditDynasty_Click(object sender, RoutedEventArgs e) => EditDynasties(DynastyInput);

    private void EditGens_Click(object sender, RoutedEventArgs e) => EditDynasties(GensInput);

    private void EditAdoptiveGens_Click(object sender, RoutedEventArgs e) => EditDynasties(AdoptiveGensInput);

    // Apre l'archivio delle dinastie sulla prima indicata; se viene rinominata, il campo segue il nuovo nome.
    private void EditDynasties(ComboBox input)
    {
        var names = PeopleGrouping.FamilyNames(input.Text);
        var window = new DynastiesWindow(store, names.FirstOrDefault()) { Owner = this };
        window.ShowDialog();
        if (names.Count > 0)
        {
            input.Text = PeopleGrouping.JoinFamilyNames(names.Select(window.ResolveRename).Distinct(PeopleGrouping.FamilyComparer));
        }

        LoadChoices();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultFamily = PeopleGrouping.JoinFamilyNames(PeopleGrouping.FamilyNames(DynastyInput.Text));
        ResultGens = PeopleGrouping.JoinFamilyNames(PeopleGrouping.FamilyNames(GensInput.Text));
        ResultAdoptiveGens = PeopleGrouping.JoinFamilyNames(PeopleGrouping.FamilyNames(AdoptiveGensInput.Text));
        DialogResult = true;
    }
}
