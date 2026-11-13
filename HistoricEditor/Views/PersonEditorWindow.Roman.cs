using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Onomastica romana nella scheda: praenomen, nomen, cognomen, agnomen, filiazione, tribù, nome latino e nomi nel tempo.
public partial class PersonEditorWindow
{
    private readonly ObservableCollection<RomanNameRecord> romanNames = [];

    private bool romanForm;

    private void InitializeRomanInputs()
    {
        PraenomenInput.ItemsSource = RomanNaming.Praenomina;
        TribeInput.ItemsSource = RomanNaming.Tribes;
        RomanNamesGrid.ItemsSource = romanNames;
        NamingInput.Items.Add(new ComboBoxItem { Tag = Onomastiche.Automatica });
        NamingInput.Items.Add(new ComboBoxItem { Tag = Onomastiche.Romana, Content = "Romana: praenomen, nomen, cognomen e gens" });
        NamingInput.Items.Add(new ComboBoxItem { Tag = Onomastiche.Standard, Content = "Standard: nome, cognome e famiglia" });
        NamingInput.SelectedIndex = 0;

        // Con l'onomastica automatica la scheda segue le date: si aggiorna mentre si scrivono.
        BirthInput.TextChanged += (_, _) => UpdateNamingMode();
        DeathInput.TextChanged += (_, _) => UpdateNamingMode();
        SurnameInput.TextChanged += (_, _) => UpdateNamingMode();
        SuffixInput.TextChanged += (_, _) => UpdateNamingMode();
        UpdateNamingMode();
    }

    private string SelectedNaming => (NamingInput.SelectedItem as ComboBoxItem)?.Tag as string ?? Onomastiche.Automatica;

    private void SelectNaming(string? onomastica)
        => NamingInput.SelectedItem = NamingInput.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == (onomastica ?? string.Empty))
            ?? NamingInput.Items[0];

    private bool FormHasRomanParts => RomanNaming.HasRomanParts(
        PraenomenInput.Text, NomenInput.Text, CognomenInput.Text, AgnomenInput.Text, LatinNameInput.Text,
        PrenomeInput.Text, GentilizioInput.Text, CognomeRomanoInput.Text, AgnomeInput.Text);

    private bool IsRomanForm => RomanNaming.IsRoman(SelectedNaming, BirthInput.Text, DeathInput.Text, FormHasRomanParts);

    private void NamingInput_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateNamingMode();

    private void RomanPart_Changed(object sender, TextChangedEventArgs e) => UpdateNamingMode();

    // Mostra i campi del nome romano o quelli standard e aggiorna l'anteprima del nome latino.
    private void UpdateNamingMode()
    {
        if (NamingInput.Items.Count == 0)
        {
            return;
        }

        var era = RomanNaming.IsRomanEra(BirthInput.Text, DeathInput.Text);
        var automatic = (ComboBoxItem)NamingInput.Items[0];
        automatic.Content = FormHasRomanParts || era
            ? "Automatica: romana " + (FormHasRomanParts ? "(nome romano indicato)" : "(per le date)")
            : "Automatica: standard";

        var roman = IsRomanForm;
        var changed = roman != romanForm;
        romanForm = roman;
        RomanPanel.Visibility = roman ? Visibility.Visible : Visibility.Collapsed;

        // Cognome e suffisso restano visibili anche nella scheda romana se contengono già un valore, per non nasconderlo.
        SurnamePanel.Visibility = !roman || SurnameInput.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SuffixPanel.Visibility = !roman || SuffixInput.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(FamilyPanel, SurnamePanel.Visibility == Visibility.Visible ? 1 : 0);
        SurnameLabel.Content = roman ? "Cognome (scheda standard)" : "Cognome";
        NameLabel.Content = roman ? "Nome italiano *" : "Nome *";
        NameInput.ToolTip = roman ? "Come compare in italiano, ad esempio «Gaio Giulio Cesare»: è il nome mostrato negli elenchi" : null;
        // La gens ha un campo proprio nel riquadro del nome romano; qui resta la dinastia (es. «Dinastia flavia»).
        FamilyLabel.Content = roman ? "Dinastia" : "Famiglia";
        System.Windows.Automation.AutomationProperties.SetName(FamilyInput, roman ? "Dinastia" : "Famiglia");
        FamilyInput.ToolTip = roman
            ? "La dinastia (es. «Dinastia flavia»), non la gens, che ha un campo proprio nel riquadro del nome romano. Un nome nuovo verrà aggiunto alle dinastie al salvataggio"
            : "Scegli una dinastia registrata o scrivi un nome nuovo: verrà aggiunto alle dinastie al salvataggio";

        var latin = RomanNaming.LatinName(PraenomenInput.Text, NomenInput.Text, CognomenInput.Text, AgnomenInput.Text, LatinNameInput.Text);
        var epigraphic = RomanNaming.Epigraphic(PraenomenInput.Text, NomenInput.Text, FiliationInput.Text, TribeInput.Text, CognomenInput.Text, AgnomenInput.Text);
        LatinPreview.Text = latin.Length == 0
            ? "Compila le parti del nome: il nome latino si compone da solo."
            : epigraphic.Length > 0 && !string.Equals(epigraphic, latin, StringComparison.Ordinal)
                ? $"Nome latino: {latin}  ·  Forma epigrafica: {epigraphic}"
                : $"Nome latino: {latin}";
        // A tendina chiusa il nome latino resta leggibile nell'intestazione.
        LatinExpanderHeader.Text = latin.Length > 0 ? $"Nome in latino: {latin}" : "Nome in latino";
        HeaderLatin.Text = roman ? latin : string.Empty;
        HeaderLatin.Visibility = HeaderLatin.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (changed)
        {
            UpdateDynastyPath();
        }
    }

    private void FillRomanName(HistoricalPerson person)
    {
        RomanNamesGrid.CommitEdit();
        person.Onomastica = SelectedNaming;
        person.Praenomen = PraenomenInput.Text.Trim();
        person.Nomen = NomenInput.Text.Trim();
        person.Cognomen = CognomenInput.Text.Trim();
        person.Agnomen = AgnomenInput.Text.Trim();
        person.Filiazione = FiliationInput.Text.Trim();
        person.Tribu = TribeInput.Text.Trim();
        person.NomeLatino = LatinNameInput.Text.Trim();
        person.NomiNelTempo = RomanNaming.Normalize(romanNames);

        // Le parti in italiano (la scheda è già tornata all'italiano prima del salvataggio).
        var italian = ItalianNames();
        person.Prenome = italian.Prenome.Trim();
        person.Gentilizio = italian.Gentilizio.Trim();
        person.CognomeRomano = italian.CognomeRomano.Trim();
        person.Agnome = italian.Agnome.Trim();
    }

    private void LoadRomanName(HistoricalPerson person)
    {
        SelectNaming(person.Onomastica);
        PraenomenInput.Text = person.Praenomen;
        NomenInput.Text = person.Nomen;
        CognomenInput.Text = person.Cognomen;
        AgnomenInput.Text = person.Agnomen;
        FiliationInput.Text = person.Filiazione;
        TribeInput.Text = person.Tribu;
        LatinNameInput.Text = person.NomeLatino;
        PrenomeInput.Text = person.Prenome;
        GentilizioInput.Text = person.Gentilizio;
        CognomeRomanoInput.Text = person.CognomeRomano;
        AgnomeInput.Text = person.Agnome;
        SetRomanNames(person.NomiNelTempo ?? []);
        UpdateNamingMode();
    }

    // Dall'import arrivano le parti del nome; filiazione, tribù e nomi nel tempo non sono su Wikidata e restano quelli della scheda.
    // Una parte che Wikidata non indica non cancella quella scritta a mano (aggiornamento di una scheda esistente).
    private void PrefillRomanName(WikipediaImportResult import)
    {
        static void Apply(TextBox input, string value)
        {
            if (value.Trim().Length > 0)
            {
                input.Text = value.Trim();
            }
        }

        if (import.Praenomen.Trim().Length > 0)
        {
            PraenomenInput.Text = import.Praenomen.Trim();
        }

        Apply(NomenInput, import.Nomen);
        Apply(CognomenInput, import.Cognomen);
        Apply(AgnomenInput, import.Agnomen);
        Apply(LatinNameInput, import.NomeLatino);
        Apply(PrenomeInput, import.Prenome);
        Apply(GentilizioInput, import.Gentilizio);
        Apply(CognomeRomanoInput, import.CognomeRomano);
        Apply(AgnomeInput, import.Agnome);
        UpdateNamingMode();
    }

    private void SetRomanNames(IEnumerable<RomanNameRecord> source)
    {
        romanNames.Clear();
        foreach (var name in source)
        {
            romanNames.Add(name.Clone());
        }
    }

    private string RomanSnapshot()
    {
        RomanNamesGrid.CommitEdit();
        var italian = ItalianNames();
        return string.Join("|",
            italian.Prenome.Trim(), italian.Gentilizio.Trim(), italian.CognomeRomano.Trim(), italian.Agnome.Trim(),
            SelectedNaming, PraenomenInput.Text.Trim(), NomenInput.Text.Trim(), CognomenInput.Text.Trim(), AgnomenInput.Text.Trim(),
            FiliationInput.Text.Trim(), TribeInput.Text.Trim(), LatinNameInput.Text.Trim(),
            string.Join("\u001e", romanNames.Select(n => $"{n.Nome.Trim()}|{n.Dal.Trim()}|{n.Al.Trim()}|{n.Motivo.Trim()}")));
    }

    private void AddRomanName_Click(object sender, RoutedEventArgs e)
    {
        RomanNamesGrid.CommitEdit();
        // Il primo nome proposto è quello attuale, da completare con il periodo.
        var current = RomanNaming.LatinName(PraenomenInput.Text, NomenInput.Text, CognomenInput.Text, AgnomenInput.Text, LatinNameInput.Text);
        var name = new RomanNameRecord { Nome = romanNames.Count == 0 ? current : string.Empty };
        romanNames.Add(name);
        RomanNamesGrid.SelectedItem = name;
        RomanNamesGrid.ScrollIntoView(name);
        RomanNamesGrid.Focus();
    }

    private void RemoveRomanName_Click(object sender, RoutedEventArgs e)
    {
        RomanNamesGrid.CommitEdit();
        if (RomanNamesGrid.SelectedItem is RomanNameRecord name)
        {
            romanNames.Remove(name);
        }
    }

    private void FiliationFromFather_Click(object sender, RoutedEventArgs e)
    {
        var father = editingPerson?.FatherId is int fatherId ? store.GetPerson(fatherId) : null;
        if (father is null)
        {
            Feedback.Error(EditorMessage, "Indica prima il padre nella tab «Parentele e relazioni».");
            return;
        }

        var filiation = RomanNaming.Filiation(father.Praenomen);
        if (filiation.Length == 0)
        {
            Feedback.Error(EditorMessage, $"Nella scheda di {father.DisplayName} manca il praenomen.");
            return;
        }

        FiliationInput.Text = filiation;
        Feedback.Success(EditorMessage, $"Filiazione composta dal praenomen di {father.DisplayName}.");
    }
}
