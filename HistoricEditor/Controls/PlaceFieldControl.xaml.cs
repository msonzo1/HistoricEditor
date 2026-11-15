using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace HistoricEditor;

public partial class PlaceFieldControl : UserControl
{
    private string title = string.Empty;

    public PlaceFieldControl()
    {
        InitializeComponent();
    }

    public PeopleStore? Store { get; set; }

    public string Title
    {
        get => title;
        set
        {
            title = value;
            TitleLabel.Content = value;
            AutomationProperties.SetName(EditButton, $"Modifica: {value}");
            AutomationProperties.SetName(ClearButton, $"Rimuovi: {value}");
            AutomationProperties.SetName(SpecificButton, $"Aggiungi o modifica luogo specifico: {value}");
            AutomationProperties.SetName(ClearSpecificButton, $"Rimuovi luogo specifico: {value}");
            AutomationProperties.SetName(HistoricToggle, $"Contesto storico: {value}");
            AutomationProperties.SetName(HistoricNameInput, $"Nome all’epoca: {value}");
            AutomationProperties.SetName(PolityInput, $"Entità politica storica: {value}");
            AutomationProperties.SetName(HistoricAdminInput, $"Unità dell’epoca: {value}");
            AutomationProperties.SetName(PeriodInput, $"Periodo: {value}");
        }
    }

    public int? LuogoId { get; private set; }
    public int? LuogoSpecificoId { get; private set; }

    // Dati di ricerca importati (Wikidata), conservati sulla scheda insieme al contesto storico.
    public List<HistoricalPlaceRecord> ImportedRecords { get; set; } = [];

    public string DisplayText { get; private set; } = string.Empty;

    public void SetPlace(int? luogoId, int? specificoId)
    {
        LuogoSpecificoId = specificoId;
        LuogoId = specificoId is null ? luogoId : null;
        Refresh();
    }

    public void Refresh()
    {
        if (Store is null)
        {
            return;
        }

        // Scarta riferimenti a luoghi eliminati nel frattempo.
        if (LuogoSpecificoId is int specificId && !Store.SpecificPlaceExists(specificId))
        {
            LuogoSpecificoId = null;
        }

        if (LuogoId is int classicId && !Store.PlaceExists(classicId))
        {
            LuogoId = null;
        }

        var description = Store.DescribeRole(LuogoId, LuogoSpecificoId);
        ShowClassic(description.Classic);
        var hasSpecific = LuogoSpecificoId is not null;
        SpecificText.Text = description.Specific.Length > 0 ? description.Specific : "Nessun luogo specifico";
        SpecificRow.Visibility = hasSpecific || LuogoId is not null ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = hasSpecific || LuogoId is not null ? Visibility.Visible : Visibility.Collapsed;
        ClearSpecificButton.Visibility = hasSpecific ? Visibility.Visible : Visibility.Collapsed;
        SpecificText.ToolTip = SpecificText.Text;
        DisplayText = description.DisplayText;
    }

    // Le parti che sono luoghi storici (non esistono più) in corsivo, con l'icona della storia davanti:
    // «Aquae Cutiliae, Castel Sant'Angelo, provincia di Rieti, Lazio, Italia».
    private void ShowClassic(string text)
    {
        ClassicText.Inlines.Clear();
        int? placeId = LuogoSpecificoId is int specificId ? Store?.GetSpecificPlace(specificId)?.LuogoId : LuogoId;
        var parts = text.Length > 0 && Store is not null ? Store.DescribePlaceParts(placeId) : [];
        var historic = parts.Where(p => p.Storico).Select(p => p.Nome).ToList();
        if (parts.Count == 0 || historic.Count == 0)
        {
            ClassicText.Text = text.Length > 0 ? text : "Nessun luogo selezionato";
        }
        else
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    ClassicText.Inlines.Add(new Run(", "));
                }

                ClassicText.Inlines.Add(parts[i].Storico ? new Run(parts[i].Nome) { FontStyle = FontStyles.Italic } : new Run(parts[i].Nome));
            }
        }

        HistoricIcon.Visibility = historic.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var tooltip = historic.Count > 0
            ? $"{text}\nLuogo storico, non esiste più: {string.Join(", ", historic)}"
            : ClassicText.Text;
        ClassicText.ToolTip = tooltip;
        AutomationProperties.SetName(ClassicText, historic.Count > 0 ? $"{text}. Luogo storico, non esiste più: {string.Join(", ", historic)}" : ClassicText.Text);
    }

    public void Clear()
    {
        ImportedRecords = [];
        SetHistoricalContext(null);
        SetPlace(null, null);
    }

    public HistoricalPlaceContext? GetHistoricalContext()
    {
        if (HistoricToggle.IsChecked != true)
        {
            return null;
        }

        var context = new HistoricalPlaceContext
        {
            NameAtTime = HistoricNameInput.Text.Trim(),
            PoliticalEntity = PolityInput.Text.Trim(),
            AdministrativeUnit = HistoricAdminInput.Text.Trim(),
            Period = PeriodInput.Text.Trim(),
        };

        return new[] { context.NameAtTime, context.PoliticalEntity, context.AdministrativeUnit, context.Period }.All(v => v.Length == 0)
            ? null
            : context;
    }

    public void SetHistoricalContext(HistoricalPlaceContext? context)
    {
        HistoricNameInput.Text = context?.NameAtTime ?? string.Empty;
        PolityInput.Text = context?.PoliticalEntity ?? string.Empty;
        HistoricAdminInput.Text = context?.AdministrativeUnit ?? string.Empty;
        PeriodInput.Text = context?.Period ?? string.Empty;
        HistoricToggle.IsChecked = context is not null;
    }

    private void HistoricToggle_Changed(object sender, RoutedEventArgs e)
        => HistoricPanel.Visibility = HistoricToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void Specific_Click(object sender, RoutedEventArgs e)
    {
        if (Store is null)
        {
            return;
        }

        var editor = new SpecificPlaceEditorWindow(Store, LuogoSpecificoId, LuogoId) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true && editor.ResultId is int id)
        {
            SetPlace(null, id);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => SetPlace(null, null);

    // Toglie solo il luogo specifico, mantenendo il luogo che lo contiene.
    private void ClearSpecific_Click(object sender, RoutedEventArgs e)
    {
        int? parentId = LuogoSpecificoId is int id ? Store?.GetSpecificPlace(id)?.LuogoId : LuogoId;
        SetPlace(parentId, null);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Store is null)
        {
            return;
        }

        // Un luogo storico (Falacrine) si mostra tra i luoghi storici, che il selettore apre per primi.
        var historic = LuogoSpecificoId is null && LuogoId is int luogoId && Store.GetPlace(luogoId)?.Storico == true;
        var dialog = new PlacePickerWindow(Store, Title, allowSpecific: true, LuogoId, LuogoSpecificoId, allowClear: true, startHistoric: historic) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            SetPlace(dialog.ResultLuogoId, dialog.ResultSpecificoId);
        }
        else if (dialog.PlacesEdited)
        {
            Refresh();
        }
    }
}
