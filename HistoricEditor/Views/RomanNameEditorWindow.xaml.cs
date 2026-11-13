using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Modulo di un nome romano della scheda: parti in italiano e in latino, filiazione, tribù, periodo e motivo.
// Per il nome principale anche le parti nelle altre lingue della scheda.
public partial class RomanNameEditorWindow : Window
{
    private readonly HistoricalPerson? father;
    private readonly List<LocalizedPersonName> languages;

    public RomanNameRecord Result { get; private set; }

    // Parti nelle altre lingue modificate (solo per il nome principale); null se non mostrate.
    public IReadOnlyList<LocalizedPersonName>? ResultLanguages { get; private set; }

    public RomanNameEditorWindow(RomanNameRecord name, HistoricalPerson? father, IEnumerable<LocalizedPersonName>? principalLanguages)
    {
        InitializeComponent();
        this.father = father;
        Result = name.Clone();
        Title = name.IsEmpty() ? "Nuovo nome romano" : $"Nome romano – {name.DisplayName}";
        PraenomenInput.ItemsSource = RomanNaming.Praenomina;
        TribeInput.ItemsSource = RomanNaming.Tribes;

        PrenomeInput.Text = name.Prenome;
        GentilizioInput.Text = name.Gentilizio;
        CognomeRomanoInput.Text = name.CognomeRomano;
        AgnomeInput.Text = name.Agnome;
        PraenomenInput.Text = name.Praenomen;
        NomenInput.Text = name.Nomen;
        CognomenInput.Text = name.Cognomen;
        AgnomenInput.Text = name.Agnomen;
        FiliationInput.Text = name.Filiazione;
        TribeInput.Text = name.Tribu;
        LatinNameInput.Text = name.NomeLatino;
        FromInput.Text = name.Dal;
        ToInput.Text = name.Al;
        ReasonInput.Text = name.Motivo;

        languages = (principalLanguages ?? []).Select(l => l.Clone()).ToList();
        if (principalLanguages is not null && languages.Count > 0)
        {
            LanguagesGrid.ItemsSource = languages;
            LanguagesPanel.Visibility = Visibility.Visible;
        }

        UpdatePreview();
        Loaded += (_, _) => PrenomeInput.Focus();
    }

    private void Part_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private RomanNameRecord ReadForm()
    {
        var name = Result.Clone();
        name.Prenome = PrenomeInput.Text.Trim();
        name.Gentilizio = GentilizioInput.Text.Trim();
        name.CognomeRomano = CognomeRomanoInput.Text.Trim();
        name.Agnome = AgnomeInput.Text.Trim();
        name.Praenomen = PraenomenInput.Text.Trim();
        name.Nomen = NomenInput.Text.Trim();
        name.Cognomen = CognomenInput.Text.Trim();
        name.Agnomen = AgnomenInput.Text.Trim();
        name.Filiazione = FiliationInput.Text.Trim();
        name.Tribu = TribeInput.Text.Trim();
        name.NomeLatino = LatinNameInput.Text.Trim();
        name.Nome = string.Empty;
        name.Dal = FromInput.Text.Trim();
        name.Al = ToInput.Text.Trim();
        name.Motivo = ReasonInput.Text.Trim();
        return name;
    }

    private void UpdatePreview()
    {
        if (LatinPreview is null)
        {
            return;
        }

        var name = ReadForm();
        var latin = name.LatinName;
        var epigraphic = name.Epigraphic;
        LatinPreview.Text = latin.Length == 0
            ? "Compilare le parti in latino: il nome latino si compone da solo."
            : epigraphic.Length > 0 && !string.Equals(epigraphic, latin, StringComparison.Ordinal)
                ? $"Nome latino: {latin}  ·  Forma epigrafica: {epigraphic}"
                : $"Nome latino: {latin}";
    }

    private void FiliationFromFather_Click(object sender, RoutedEventArgs e)
    {
        if (father is null)
        {
            EditorMessage.Text = "Indicare prima il padre nella scheda «Parentele e relazioni».";
            return;
        }

        var filiation = RomanNaming.Filiation(father.Praenomen);
        if (filiation.Length == 0)
        {
            EditorMessage.Text = $"Nella scheda di {father.DisplayName} manca il praenomen.";
            return;
        }

        FiliationInput.Text = filiation;
        EditorMessage.Text = string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        LanguagesGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        var name = ReadForm();
        if (!name.HasParts)
        {
            EditorMessage.Text = "Indicare almeno una parte del nome, in italiano o in latino.";
            PrenomeInput.Focus();
            return;
        }

        Result = name;
        ResultLanguages = LanguagesPanel.Visibility == Visibility.Visible ? languages : null;
        DialogResult = true;
    }
}
