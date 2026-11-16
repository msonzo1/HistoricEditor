using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Dinastia, gens di nascita e gens d'adozione di un personaggio romano: si modificano solo qui, non nella scheda. Gens e
// rami si scelgono dall'elenco «Gentes e rami» (2026-11-15).
public partial class DynastyGensWindow : Window
{
    private readonly PeopleStore store;
    private IReadOnlyList<Dinastia> dynasties = [];
    private IReadOnlyList<GensRomana> gentes = [];
    private GensChoice birth;
    private GensChoice adoptive;

    public string ResultFamily { get; private set; } = string.Empty;
    public GensChoice ResultGens { get; private set; } = GensChoice.None;
    public GensChoice ResultAdoptiveGens { get; private set; } = GensChoice.None;

    public DynastyGensWindow(PeopleStore store, string family, GensChoice gens, GensChoice adoptiveGens)
    {
        InitializeComponent();
        this.store = store;
        birth = gens;
        adoptive = adoptiveGens;
        DynastyInput.Text = family;
        LoadChoices();
        Loaded += (_, _) => DynastyInput.Focus();
    }

    private void LoadChoices()
    {
        var family = DynastyInput.Text;
        try
        {
            dynasties = store.GetDynasties();
            gentes = store.GetGentes();
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            dynasties = [];
            gentes = [];
        }

        DynastyInput.ItemsSource = dynasties.Where(d => !d.IsGens).Select(d => d.Nome).ToList();
        DynastyInput.Text = family;
        UpdatePaths();
    }

    private void UpdatePaths()
    {
        DynastyPathText.Text = DynastyDescriptions.Hint(dynasties, DynastyInput.Text, gens: false);
        DynastyButton.Content = DynastyMenu.ButtonText("Dinastia…", DynastyInput.Text);
        Show(GensText, GensPathText, ClearGensButton, birth, "Non indicata: è quasi sempre nota (la scheda si salva comunque).");
        Show(AdoptiveGensText, AdoptiveGensPathText, ClearAdoptiveGensButton, adoptive, string.Empty);
    }

    private void Show(TextBox value, TextBlock note, Button clear, GensChoice choice, string empty)
    {
        var (path, text, missing) = GensChoices.Describe(store, gentes, choice);
        value.Text = path.Length > 0 ? path : "Nessuna";
        note.Text = path.Length > 0 ? text : empty;
        note.SetResourceReference(TextBlock.ForegroundProperty, missing ? "DangerBrush" : "QuietBrush");
        note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        clear.IsEnabled = !choice.IsEmpty;
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DynastyPathText is not null && GensText is not null)
        {
            UpdatePaths();
        }
    }

    // Apre l'archivio delle dinastie su quella scelta (con più nomi nel campo, da un menu); se una viene rinominata, il
    // campo segue il nuovo nome.
    private void EditDynasty_Click(object sender, RoutedEventArgs e)
        => DynastyMenu.Choose(DynastyButton, dynasties, DynastyInput.Text, OpenArchive);

    private void OpenArchive(string? name)
    {
        var names = PeopleGrouping.FamilyNames(DynastyInput.Text);
        var window = new DynastiesWindow(store, name) { Owner = this };
        window.ShowDialog();
        if (names.Count > 0)
        {
            DynastyInput.Text = PeopleGrouping.JoinFamilyNames(names.Select(window.ResolveRename).Distinct(PeopleGrouping.FamilyComparer));
        }

        LoadChoices();
    }

    private void ChooseGens_Click(object sender, RoutedEventArgs e)
    {
        birth = Choose(birth);
        UpdatePaths();
    }

    private void ChooseAdoptiveGens_Click(object sender, RoutedEventArgs e)
    {
        adoptive = Choose(adoptive);
        UpdatePaths();
    }

    // L'elenco delle gentes si apre sulla scelta attuale; le gentes lette dall'import e non ancora salvate compaiono come
    // nuove e si possono correggere.
    private GensChoice Choose(GensChoice choice)
    {
        var pending = new[] { birth.Pending, adoptive.Pending }.OfType<PendingGens>().Distinct();
        var window = new GentesWindow(store, choice, pending, pick: true) { Owner = this };
        var result = window.ShowDialog() == true && window.Result is { } chosen ? chosen : choice;
        LoadChoices();
        return result;
    }

    private void ClearGens_Click(object sender, RoutedEventArgs e)
    {
        birth = GensChoice.None;
        UpdatePaths();
    }

    private void ClearAdoptiveGens_Click(object sender, RoutedEventArgs e)
    {
        adoptive = GensChoice.None;
        UpdatePaths();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultFamily = PeopleGrouping.JoinFamilyNames(PeopleGrouping.FamilyNames(DynastyInput.Text));
        ResultGens = birth;
        ResultAdoptiveGens = adoptive;
        DialogResult = true;
    }
}
