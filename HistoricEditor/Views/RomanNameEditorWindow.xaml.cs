using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Modulo di un nome romano della scheda: righe delle parti (tipo, latino, italiano) nell'ordine in cui si leggono,
// filiazione e tribù comprese, nome latino libero, periodo, motivo e il nome nelle altre lingue (forme delle stesse righe).
public partial class RomanNameEditorWindow : Window
{
    private sealed record LanguageRow(string Code, string Name, string FullName);

    private readonly HistoricalPerson? father;
    private readonly ObservableCollection<RomanNamePart> parts = [];
    private readonly List<(string Code, string Name)> schedaLanguages;
    // Lingue mostrate: quelle della scheda e quelle aggiunte in questo nome.
    private readonly List<(string Code, string Name)> nameLanguages;

    public RomanNameRecord Result { get; private set; }

    public RomanNameEditorWindow(RomanNameRecord name, HistoricalPerson? father, IEnumerable<(string Code, string Name)> schedaLanguages)
    {
        InitializeComponent();
        this.father = father;
        Result = name.Clone();
        Title = name.IsEmpty() ? "Nuovo nome romano" : $"Nome romano – {name.DisplayName}";

        // Un nome nuovo parte con praenomen, nomen e cognomen.
        var initial = name.OrderedParts.Count > 0
            ? name.OrderedParts.Select(p => p.Clone())
            : new[] { TipiParteNome.Praenomen, TipiParteNome.Nomen, TipiParteNome.Cognomen }.Select(t => new RomanNamePart { Tipo = t });
        foreach (var part in initial)
        {
            AddPart(part, parts.Count);
        }

        parts.CollectionChanged += (_, _) => { UpdatePreview(); UpdatePartButtons(); RefreshLanguages(); };
        PartsGrid.ItemsSource = parts;
        foreach (var tipo in TipiParteNome.All)
        {
            var item = new MenuItem { Header = tipo };
            item.Click += (_, _) => InsertPart(tipo);
            AddPartMenu.Items.Add(item);
        }

        LatinNameInput.Text = name.NomeLatino;
        FromInput.Text = name.Dal;
        ToInput.Text = name.Al;
        ReasonInput.Text = name.Motivo;

        this.schedaLanguages = [.. schedaLanguages.Where(l => l.Code.Length > 0).DistinctBy(l => l.Code)];
        nameLanguages = [.. this.schedaLanguages];
        foreach (var code in parts.SelectMany(p => p.Traduzioni).Select(t => t.Lingua).Distinct().Where(c => nameLanguages.All(l => l.Code != c)))
        {
            nameLanguages.Add((code, LanguageName(code)));
        }

        RefreshLanguages();
        UpdatePreview();
        UpdatePartButtons();
    }

    private void AddPart(RomanNamePart part, int index)
    {
        part.AutoItalian = true;
        part.PropertyChanged += Part_PropertyChanged;
        parts.Insert(index, part);
    }

    private static string LanguageName(string code)
        => LocalizedPersonName.EditableLanguages.FirstOrDefault(l => l.Code == code).Name ?? code;

    // Righe di «In altre lingue»: nome completo dalle forme delle parti, con le parti mancanti indicate.
    private void RefreshLanguages()
    {
        if (LanguagesGrid is null || nameLanguages is null)
        {
            return;
        }

        var selected = (LanguagesGrid.SelectedItem as LanguageRow)?.Code;
        var filled = parts.Where(p => !p.IsEmpty() && TipiParteNome.IsNameWord(p.Tipo)).ToList();
        var rows = nameLanguages.Select(l =>
        {
            var name = string.Join(' ', filled.Select(p => p.Translation(l.Code)).Where(v => v.Length > 0));
            var missing = filled.Count(p => p.Translation(l.Code).Length == 0);
            var text = name.Length == 0 ? "(da completare)"
                : missing > 0 ? $"{name}  ({RomanNameLanguageWindow.Missing(missing, filled.Count)})"
                : name;
            return new LanguageRow(l.Code, l.Name, text);
        }).ToList();
        LanguagesGrid.ItemsSource = rows;
        LanguagesGrid.SelectedItem = rows.FirstOrDefault(r => r.Code == selected);
        UpdateLanguageButtons();
    }

    private void LanguagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateLanguageButtons();

    private void UpdateLanguageButtons()
    {
        var selected = LanguagesGrid.SelectedItem is LanguageRow;
        EditLanguageButton.IsEnabled = selected;
        RemoveLanguageButton.IsEnabled = selected;
        AddLanguageButton.IsEnabled = LocalizedPersonName.EditableLanguages.Any(l => nameLanguages.All(n => n.Code != l.Code));
    }

    private void AddLanguage_Click(object sender, RoutedEventArgs e)
    {
        AddLanguageMenu.Items.Clear();
        foreach (var (code, languageName) in LocalizedPersonName.EditableLanguages.Where(l => nameLanguages.All(n => n.Code != l.Code)))
        {
            var item = new MenuItem { Header = $"{languageName} ({code})" };
            item.Click += (_, _) =>
            {
                nameLanguages.Add((code, languageName));
                RefreshLanguages();
                EditLanguage(code, languageName);
            };
            AddLanguageMenu.Items.Add(item);
        }

        AddLanguageMenu.PlacementTarget = AddLanguageButton;
        AddLanguageMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AddLanguageMenu.IsOpen = true;
    }

    private void LanguagesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LanguagesGrid.SelectedItem is LanguageRow row && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(LanguagesGrid, source) is DataGridRow)
        {
            EditLanguage(row.Code, row.Name);
        }
    }

    private void EditLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (LanguagesGrid.SelectedItem is LanguageRow row)
        {
            EditLanguage(row.Code, row.Name);
        }
    }

    // Modulo della lingua con le righe compilate di «Parti del nome».
    private void EditLanguage(string code, string languageName)
    {
        var filled = parts.Where(p => !p.IsEmpty() && TipiParteNome.IsNameWord(p.Tipo)).ToList();
        if (filled.Count == 0)
        {
            EditorMessage.Text = "Compilare prima le parti del nome.";
            return;
        }

        var window = new RomanNameLanguageWindow(filled, code, languageName, ReadForm().DisplayName) { Owner = this };
        if (window.ShowDialog() == true)
        {
            for (var i = 0; i < filled.Count; i++)
            {
                filled[i].SetTranslation(code, window.Result[i]);
            }
        }

        RefreshLanguages();
    }

    // Toglie le forme della lingua; una lingua della scheda resta nell'elenco, da completare.
    private void RemoveLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (LanguagesGrid.SelectedItem is not LanguageRow row)
        {
            return;
        }

        foreach (var part in parts)
        {
            part.SetTranslation(row.Code, string.Empty);
        }

        if (schedaLanguages.All(l => l.Code != row.Code))
        {
            nameLanguages.RemoveAll(l => l.Code == row.Code);
        }

        RefreshLanguages();
    }

    private void Part_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdatePreview();

    private void Part_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    // Una parte nuova va dopo l'ultima dello stesso tipo, altrimenti dopo quelle che di solito la precedono
    // (praenomen, nomen, filiazione, tribù, cognomen, agnomen); i titoli in fondo, da spostare con ↑ ↓ (Caesar va prima
    // del praenomen). La filiazione nuova si compone dal praenomen del padre, se è noto.
    private void InsertPart(string tipo)
    {
        var order = TipiParteNome.All.ToList();
        var index = parts.Select((p, i) => (p, i)).LastOrDefault(x => x.p.Tipo == tipo, (null!, -1)).Item2;
        if (index < 0)
        {
            index = parts.Select((p, i) => (p, i)).LastOrDefault(x => order.IndexOf(x.p.Tipo) < order.IndexOf(tipo) && !TipiParteNome.IsTitle(x.p.Tipo), (null!, -1)).Item2;
            if (TipiParteNome.IsTitle(tipo))
            {
                index = parts.Count - 1;
            }
        }

        var part = new RomanNamePart { Tipo = tipo, Latino = tipo == TipiParteNome.Filiazione ? FiliationFromFather() : string.Empty };
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
        name.Parti = [.. parts.Where(p => !p.IsEmpty()).Select(p =>
        {
            var part = p.Clone();
            (part.Latino, part.Italiano) = (p.Latino.Trim(), p.Italiano.Trim());
            return part;
        })];
        // Filiazione e tribù sono righe: senza righe restano vuote.
        (name.Filiazione, name.Tribu) = (string.Empty, string.Empty);
        name.FieldsFromParts();
        name.NomeLatino = LatinNameInput.Text.Trim();
        name.Nome = string.Empty;
        name.Dal = FromInput.Text.Trim();
        name.Al = ToInput.Text.Trim();
        name.Motivo = ReasonInput.Text.Trim();
        return name;
    }

    private void UpdatePreview()
    {
        if (LatinPreview is null || LatinNameInput is null)
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

    // Filiazione proposta dal praenomen del padre («C. f.»); vuota, con la spiegazione, se il padre o il suo praenomen mancano.
    private string FiliationFromFather()
    {
        if (father is null)
        {
            EditorMessage.Text = "Filiazione da completare: nella scheda «Parentele e relazioni» non è indicato il padre.";
            return string.Empty;
        }

        var filiation = RomanNaming.Filiation(father.Praenomen);
        EditorMessage.Text = filiation.Length == 0 ? $"Filiazione da completare: nella scheda di {father.DisplayName} manca il praenomen." : string.Empty;
        return filiation;
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
        DialogResult = true;
    }
}
