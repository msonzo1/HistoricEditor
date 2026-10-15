using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace HistoricEditor;

public partial class PlacesEditorWindow : Window
{
    private sealed record LevelRow(string Tipo, TextBox Box, CheckBox? Absent);

    private readonly PeopleStore? store;
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> absent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TextBox, string> lastSuggested = [];
    private readonly List<LevelRow> rows = [];
    private bool ready;

    public List<GeoLevel> ResultLevels { get; private set; } = [];
    public double? ResultLatitude { get; private set; }
    public double? ResultLongitude { get; private set; }

    public PlacesEditorWindow(IEnumerable<GeoLevel> prefill, double? latitude = null, double? longitude = null, string? notice = null, PeopleStore? store = null)
    {
        InitializeComponent();
        this.store = store;

        var levels = prefill.ToList();
        foreach (var level in levels)
        {
            values[level.Tipo] = level.Nome;
            absent[level.Tipo] = level.Assente;
        }

        foreach (var country in GeoSchemas.KnownCountries)
        {
            StatoBox.Items.Add(country);
        }

        StatoBox.Text = levels.FirstOrDefault()?.Nome ?? string.Empty;
        LatitudeBox.Text = CoordinateText.Format(latitude);
        LongitudeBox.Text = CoordinateText.Format(longitude);
        if (!string.IsNullOrWhiteSpace(notice))
        {
            NoticeText.Text = notice;
            NoticeText.Visibility = Visibility.Visible;
        }

        StatoBox.SelectionChanged += (_, _) => RegenerateRows();
        StatoBox.LostFocus += (_, _) => RegenerateRows();

        ready = true;
        RegenerateRows();
    }

    private string CurrentCountry => StatoBox.SelectedItem as string ?? StatoBox.Text.Trim();

    private void RegenerateRows()
    {
        if (!ready)
        {
            return;
        }

        CaptureCurrentValues();

        var schema = GeoSchemas.For(CurrentCountry);
        if (schema.Skip(1).SequenceEqual(rows.Select(r => r.Tipo), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        LevelsPanel.Children.Clear();
        rows.Clear();
        lastSuggested.Clear();

        // Il primo livello (Stato) è gestito da StatoBox; qui generiamo i successivi.
        for (var i = 1; i < schema.Length; i++)
        {
            var tipo = schema[i];
            var isLeaf = i == schema.Length - 1;
            var box = new TextBox { Text = values.GetValueOrDefault(tipo, string.Empty) };
            AutomationProperties.SetName(box, tipo);
            var label = new Label { Content = isLeaf ? $"{tipo} *" : tipo, Target = box };
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            CheckBox? absentBox = null;
            if (!isLeaf)
            {
                var description = $"{schema[i - 1]} senza {tipo}";
                absentBox = new CheckBox
                {
                    Content = description,
                    IsChecked = absent.GetValueOrDefault(tipo),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                };
                AutomationProperties.SetName(absentBox, description);
                DockPanel.SetDock(absentBox, Dock.Right);
                absentBox.Checked += (_, _) => ApplyAbsent(box, true);
                absentBox.Unchecked += (_, _) => ApplyAbsent(box, false);
                line.Children.Add(absentBox);
                ApplyAbsent(box, absentBox.IsChecked == true);
            }

            line.Children.Add(box);
            var row = new LevelRow(tipo, box, absentBox);
            lastSuggested[box] = box.Text.Trim();
            box.LostFocus += (_, _) => SuggestAbsence(row);
            LevelsPanel.Children.Add(label);
            LevelsPanel.Children.Add(line);
            rows.Add(row);
        }
    }

    private static void ApplyAbsent(TextBox box, bool isAbsent)
    {
        box.IsEnabled = !isAbsent;
        if (isAbsent)
        {
            box.Text = string.Empty;
        }
    }

    // Se il valore è una suddivisione nota senza livello inferiore (es. Comunità di Madrid), spunta il livello successivo.
    private void SuggestAbsence(LevelRow row)
    {
        var index = rows.IndexOf(row);
        if (store is null || index < 0 || index + 1 >= rows.Count || rows[index + 1].Absent is not { } nextAbsent)
        {
            return;
        }

        var value = row.Box.Text.Trim();
        if (lastSuggested.TryGetValue(row.Box, out var previous) && previous == value)
        {
            return;
        }

        lastSuggested[row.Box] = value;
        if (value.Length > 0 && store.SuggestsNoLowerLevel(CurrentCountry, row.Tipo, value))
        {
            nextAbsent.IsChecked = true;
        }
    }

    private void CaptureCurrentValues()
    {
        foreach (var row in rows)
        {
            values[row.Tipo] = row.Box.Text;
            absent[row.Tipo] = row.Absent?.IsChecked == true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var stato = CurrentCountry;
        if (stato.Length == 0)
        {
            EditorMessage.Text = "Indica lo Stato.";
            StatoBox.Focus();
            return;
        }

        var levels = new List<GeoLevel> { new(GeoSchemas.For(stato)[0], stato) };
        var previousAbsent = false;
        foreach (var row in rows)
        {
            var isAbsent = row.Absent?.IsChecked == true;
            if (isAbsent)
            {
                if (previousAbsent)
                {
                    EditorMessage.Text = "Non si possono omettere due livelli consecutivi.";
                    row.Absent!.Focus();
                    return;
                }

                levels.Add(new GeoLevel(row.Tipo, string.Empty) { Assente = true });
            }
            else
            {
                var nome = row.Box.Text.Trim();
                if (nome.Length == 0)
                {
                    EditorMessage.Text = row.Absent is null
                        ? $"Completa il livello: {row.Tipo}."
                        : $"Completa il livello «{row.Tipo}» oppure indica che non esiste.";
                    row.Box.Focus();
                    return;
                }

                levels.Add(new GeoLevel(row.Tipo, nome));
            }

            previousAbsent = isAbsent;
        }

        if (!CoordinateText.TryParse(LatitudeBox.Text, out var lat))
        {
            EditorMessage.Text = "Latitudine non valida.";
            LatitudeBox.Focus();
            return;
        }

        if (!CoordinateText.TryParse(LongitudeBox.Text, out var lon))
        {
            EditorMessage.Text = "Longitudine non valida.";
            LongitudeBox.Focus();
            return;
        }

        ResultLevels = levels;
        ResultLatitude = lat;
        ResultLongitude = lon;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
