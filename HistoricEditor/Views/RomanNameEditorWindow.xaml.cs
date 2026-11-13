using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Modulo di un nome romano della scheda: righe delle parti (tipo, latino, italiano) nell'ordine in cui si leggono,
// filiazione, tribù, nome latino libero, periodo e motivo. Per il nome principale anche le parti nelle altre lingue.
public partial class RomanNameEditorWindow : Window
{
    private readonly HistoricalPerson? father;
    private readonly List<LocalizedPersonName> languages;
    private readonly ObservableCollection<RomanNamePart> parts = [];

    public RomanNameRecord Result { get; private set; }

    // Parti nelle altre lingue modificate (solo per il nome principale); null se non mostrate.
    public IReadOnlyList<LocalizedPersonName>? ResultLanguages { get; private set; }

    public RomanNameEditorWindow(RomanNameRecord name, HistoricalPerson? father, IEnumerable<LocalizedPersonName>? principalLanguages)
    {
        InitializeComponent();
        this.father = father;
        Result = name.Clone();
        Title = name.IsEmpty() ? "Nuovo nome romano" : $"Nome romano – {name.DisplayName}";
        TribeInput.ItemsSource = RomanNaming.Tribes;

        // Un nome nuovo parte con praenomen, nomen e cognomen.
        var initial = name.OrderedParts.Count > 0
            ? name.OrderedParts.Select(p => p.Clone())
            : new[] { TipiParteNome.Praenomen, TipiParteNome.Nomen, TipiParteNome.Cognomen }.Select(t => new RomanNamePart { Tipo = t });
        foreach (var part in initial)
        {
            AddPart(part, parts.Count);
        }

        parts.CollectionChanged += (_, _) => { UpdatePreview(); UpdatePartButtons(); };
        PartsGrid.ItemsSource = parts;
        foreach (var tipo in TipiParteNome.All)
        {
            var item = new MenuItem { Header = tipo };
            item.Click += (_, _) => InsertPart(tipo);
            AddPartMenu.Items.Add(item);
        }

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
        UpdatePartButtons();
    }

    private void AddPart(RomanNamePart part, int index)
    {
        part.AutoItalian = true;
        part.PropertyChanged += Part_PropertyChanged;
        parts.Insert(index, part);
    }

    private void Part_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdatePreview();

    private void Part_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    // Una parte nuova va dopo l'ultima dello stesso tipo, altrimenti dopo quelle che di solito la precedono
    // (praenomen, nomen, cognomen, agnomen); i titoli in fondo, da spostare con ↑ ↓ (Caesar va prima del praenomen).
    private void InsertPart(string tipo)
    {
        var order = TipiParteNome.All.ToList();
        var index = parts.Select((p, i) => (p, i)).LastOrDefault(x => x.p.Tipo == tipo, (null!, -1)).Item2;
        if (index < 0)
        {
            index = parts.Select((p, i) => (p, i)).LastOrDefault(x => order.IndexOf(x.p.Tipo) < order.IndexOf(tipo) && x.p.Tipo != TipiParteNome.Titolo, (null!, -1)).Item2;
            if (tipo == TipiParteNome.Titolo)
            {
                index = parts.Count - 1;
            }
        }

        var part = new RomanNamePart { Tipo = tipo };
        AddPart(part, index + 1);
        PartsGrid.SelectedItem = part;
        PartsGrid.ScrollIntoView(part);
    }

    private void AddPart_Click(object sender, RoutedEventArgs e)
    {
        AddPartMenu.PlacementTarget = AddPartButton;
        AddPartMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AddPartMenu.IsOpen = true;
    }

    private RomanNamePart? SelectedPart => PartsGrid.SelectedItem as RomanNamePart;

    private void PartsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePartButtons();

    private void UpdatePartButtons()
    {
        var index = SelectedPart is { } part ? parts.IndexOf(part) : -1;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < parts.Count - 1;
        RemovePartButton.IsEnabled = index >= 0;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int offset)
    {
        if (SelectedPart is not { } part)
        {
            return;
        }

        var index = parts.IndexOf(part);
        var target = index + offset;
        if (target < 0 || target >= parts.Count)
        {
            return;
        }

        parts.Move(index, target);
        PartsGrid.SelectedItem = part;
        UpdatePartButtons();
    }

    private void RemovePart_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPart is { } part)
        {
            part.PropertyChanged -= Part_PropertyChanged;
            parts.Remove(part);
        }
    }

    private RomanNameRecord ReadForm()
    {
        var name = Result.Clone();
        name.Parti = [.. parts.Where(p => !p.IsEmpty()).Select(p => new RomanNamePart { Tipo = p.Tipo, Latino = p.Latino.Trim(), Italiano = p.Italiano.Trim() })];
        name.FieldsFromParts();
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
        if (LatinPreview is null || FiliationInput is null || TribeInput is null || LatinNameInput is null)
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
        PartsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        LanguagesGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        var name = ReadForm();
        if (!name.HasParts)
        {
            EditorMessage.Text = "Indicare almeno una parte del nome, in latino o in italiano.";
            return;
        }

        Result = name;
        ResultLanguages = LanguagesPanel.Visibility == Visibility.Visible ? languages : null;
        DialogResult = true;
    }
}
