using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HistoricEditor;

public partial class PlacesEditorWindow : Window
{
    // Box: casella di testo, oppure tendina per regione e provincia italiane (2026-11-15).
    private sealed record LevelRow(string Tipo, Control Box, CheckBox? Absent);

    // Voce di una tendina: dall'elenco del codice, già nell'archivio (icona) o «Nuova provincia…» in fondo.
    public sealed record LevelChoice(string Nome, bool InArchive, bool IsNew)
    {
        public string Icon => IsNew ? "\uE710" : InArchive ? "\uE1D3" : string.Empty;

        public string? IconTip => InArchive ? "Già nell'archivio" : null;

        public FontStyle Style => IsNew ? FontStyles.Italic : FontStyles.Normal;

        public override string ToString() => Nome;
    }

    private readonly PeopleStore? store;
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> absent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Control, string> lastSuggested = [];
    private readonly List<LevelRow> rows = [];
    // Livelli proposti (di solito dall'import): nomi nelle altre lingue e coordinate passano al livello salvato
    // se l'utente ne conferma il nome.
    private readonly Dictionary<string, GeoLevel> prefilled = new(StringComparer.OrdinalIgnoreCase);
    private readonly string prefilledLeafName = string.Empty;
    private readonly string prefilledLatitudeText = string.Empty;
    private readonly string prefilledLongitudeText = string.Empty;
    private bool ready;
    private bool rowsForItaly;

    // Luogo storico: i livelli più bassi (il comune, anche la provincia) possono restare vuoti, perché un luogo che non
    // esiste più non sempre si riconduce a un solo comune di oggi; il luogo storico sta allora nell'ultimo livello indicato.
    private readonly bool allowPartial;

    public List<GeoLevel> ResultLevels { get; private set; } = [];
    public double? ResultLatitude { get; private set; }
    public double? ResultLongitude { get; private set; }

    // showCoordinates false: senza latitudine e longitudine, per il luogo che contiene un luogo storico (2026-11-17), dove
    // sembrerebbero quelle del luogo storico; le coordinate dei livelli lette all'import restano.
    private readonly bool showCoordinates;

    public PlacesEditorWindow(IEnumerable<GeoLevel> prefill, double? latitude = null, double? longitude = null, string? notice = null, PeopleStore? store = null, bool allowPartial = false, bool showCoordinates = true)
    {
        InitializeComponent();
        this.store = store;
        this.allowPartial = allowPartial;
        this.showCoordinates = showCoordinates;
        CoordinatesPanel.Visibility = showCoordinates ? Visibility.Visible : Visibility.Collapsed;

        var levels = prefill.ToList();
        foreach (var level in levels)
        {
            values[level.Tipo] = level.Nome;
            absent[level.Tipo] = level.Assente;
            prefilled[level.Tipo] = level;
        }

        foreach (var country in GeoSchemas.KnownCountries)
        {
            StatoBox.Items.Add(country);
        }

        // Senza coordinate esplicite si propongono quelle del livello più basso (es. Cittareale, comune proposto
        // per Falacrine): restano sue solo se il nome non viene cambiato (vedi Save_Click).
        var leaf = levels.LastOrDefault(l => !l.Assente);
        if (latitude is null && longitude is null && leaf is not null)
        {
            latitude = leaf.Latitudine;
            longitude = leaf.Longitudine;
        }

        prefilledLeafName = leaf?.Nome.Trim() ?? string.Empty;
        StatoBox.Text = levels.FirstOrDefault()?.Nome ?? string.Empty;
        LatitudeBox.Text = CoordinateText.Format(latitude);
        LongitudeBox.Text = CoordinateText.Format(longitude);
        prefilledLatitudeText = LatitudeBox.Text;
        prefilledLongitudeText = LongitudeBox.Text;
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
        var italy = IsItaly(CurrentCountry);
        if (rows.Count > 0 && italy == rowsForItaly && schema.Skip(1).SequenceEqual(rows.Select(r => r.Tipo), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        LevelsPanel.Children.Clear();
        rows.Clear();
        lastSuggested.Clear();
        rowsForItaly = italy;

        // Il primo livello (Stato) è gestito da StatoBox; qui generiamo i successivi.
        for (var i = 1; i < schema.Length; i++)
        {
            var tipo = schema[i];
            var isLeaf = i == schema.Length - 1;
            var value = values.GetValueOrDefault(tipo, string.Empty);
            Control box = italy && IsChoiceLevel(tipo) ? CreateChoiceBox(tipo, value) : new TextBox { Text = value };
            AutomationProperties.SetName(box, tipo);
            var label = new Label { Content = isLeaf && !allowPartial ? $"{tipo} *" : tipo, Target = box };
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
            lastSuggested[box] = TextOf(box).Trim();
            box.LostFocus += (_, _) => SuggestAbsence(row);
            LevelsPanel.Children.Add(label);
            LevelsPanel.Children.Add(line);
            rows.Add(row);
        }

        RefreshChoices();
    }

    private static bool IsItaly(string country) => string.Equals(country.Trim(), ItalianDivisions.Country, StringComparison.OrdinalIgnoreCase);

    private static bool IsChoiceLevel(string tipo)
        => string.Equals(tipo, ItalianDivisions.RegionType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(tipo, ItalianDivisions.ProvinceType, StringComparison.OrdinalIgnoreCase);

    private static string TextOf(Control box) => box is ComboBox combo ? combo.Text : ((TextBox)box).Text;

    private static void SetText(Control box, string text)
    {
        if (box is ComboBox combo)
        {
            combo.Text = text;
        }
        else
        {
            ((TextBox)box).Text = text;
        }
    }

    // Tendina modificabile: si sceglie dall'elenco o si scrive (anche un nome nuovo); «Nuova regione…» in fondo svuota
    // la casella per scriverlo.
    private ComboBox CreateChoiceBox(string tipo, string value)
    {
        var combo = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            ItemTemplate = (DataTemplate)FindResource("LevelChoiceTemplate"),
            Text = value,
        };
        TextSearch.SetTextPath(combo, nameof(LevelChoice.Nome));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is LevelChoice { IsNew: true })
            {
                Dispatcher.BeginInvoke(() =>
                {
                    combo.SelectedItem = null;
                    combo.Text = string.Empty;
                    combo.Focus();
                    EditorMessage.Text = $"Scrivere il nome della nuova {tipo.ToLowerInvariant()}.";
                }, DispatcherPriority.Background);
            }
            else if (string.Equals(tipo, ItalianDivisions.RegionType, StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(RefreshChoices, DispatcherPriority.Background);
            }
        };
        if (string.Equals(tipo, ItalianDivisions.RegionType, StringComparison.OrdinalIgnoreCase))
        {
            combo.LostFocus += (_, _) => RefreshChoices();
        }

        return combo;
    }

    // Voci delle tendine: regioni (o province della regione scelta) dell'elenco e quelle già nell'archivio, che sostituiscono
    // la voce dell'elenco con lo stesso codice Wikidata (anche se rinominate), poi «Nuova …».
    private void RefreshChoices()
    {
        if (!rowsForItaly)
        {
            return;
        }

        var regionRow = rows.FirstOrDefault(r => string.Equals(r.Tipo, ItalianDivisions.RegionType, StringComparison.OrdinalIgnoreCase));
        var italiaId = store?.FindPlace(null, "Stato", ItalianDivisions.Country);
        var regionName = regionRow is null ? string.Empty : TextOf(regionRow.Box).Trim();
        foreach (var row in rows)
        {
            if (row.Box is not ComboBox combo)
            {
                continue;
            }

            var isRegion = row == regionRow;
            IEnumerable<ItalianDivisions.Division> fixedItems = isRegion ? ItalianDivisions.RegionDivisions : ItalianDivisions.ProvincesOf(regionName);
            int? parentId = isRegion ? italiaId
                : italiaId is int id && regionName.Length > 0 ? store?.FindPlace(id, ItalianDivisions.RegionType, regionName) : null;
            IReadOnlyList<(string Nome, string WikidataId)> archived = store is not null && parentId is not null ? store.GetChildPlaces(parentId, row.Tipo) : [];

            var choices = fixedItems
                .Where(d => !archived.Any(a => a.WikidataId == d.WikidataId || string.Equals(a.Nome, d.Nome, StringComparison.OrdinalIgnoreCase)))
                .Select(d => new LevelChoice(d.Nome, false, false))
                .Concat(archived.Select(a => new LevelChoice(a.Nome, true, false)))
                .OrderBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase)
                .Append(new LevelChoice($"Nuova {row.Tipo.ToLowerInvariant()}…", false, true))
                .ToList();
            var text = combo.Text;
            combo.ItemsSource = choices;
            combo.Text = text;
        }
    }

    private static void ApplyAbsent(Control box, bool isAbsent)
    {
        box.IsEnabled = !isAbsent;
        if (isAbsent)
        {
            SetText(box, string.Empty);
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

        var value = TextOf(row.Box).Trim();
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
            values[row.Tipo] = TextOf(row.Box);
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

        var levels = new List<GeoLevel> { WithPrefilledData(new(GeoSchemas.For(stato)[0], stato)) };
        var previousAbsent = false;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
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
                var nome = TextOf(row.Box).Trim();
                if (nome.Length == 0 && allowPartial
                    && rows.Skip(index).All(r => TextOf(r.Box).Trim().Length == 0 && r.Absent?.IsChecked != true))
                {
                    // Luogo storico sotto una provincia, una regione o direttamente sotto lo Stato (Regio V Picenum →
                    // Italia, 2026-11-15): i livelli vuoti in fondo si tralasciano.
                    while (levels.Count > 1 && levels[^1].Assente)
                    {
                        levels.RemoveAt(levels.Count - 1);
                    }

                    break;
                }

                if (nome.Length == 0)
                {
                    EditorMessage.Text = row.Absent is null
                        ? $"Completa il livello: {row.Tipo}."
                        : $"Completa il livello «{row.Tipo}» oppure indica che non esiste.";
                    row.Box.Focus();
                    return;
                }

                // Scritto a mano come una voce dell'elenco («provincia di rieti»): si usa il nome dell'elenco.
                if (rowsForItaly && IsChoiceLevel(row.Tipo) && ItalianDivisions.Find(row.Tipo, nome) is { } division
                    && string.Equals(division.Nome, nome, StringComparison.OrdinalIgnoreCase))
                {
                    nome = division.Nome;
                }

                levels.Add(WithPrefilledData(new GeoLevel(row.Tipo, nome)));
            }

            previousAbsent = isAbsent;
        }

        if (!showCoordinates)
        {
            ResultLevels = levels;
            ResultLatitude = null;
            ResultLongitude = null;
            DialogResult = true;
            return;
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

        // Coordinate proposte e non toccate, ma il luogo è cambiato (Cittareale → Amatrice): non sono del nuovo luogo.
        var leafName = levels.LastOrDefault(l => !l.Assente)?.Nome ?? string.Empty;
        if (!string.Equals(leafName, prefilledLeafName, StringComparison.OrdinalIgnoreCase)
            && LatitudeBox.Text == prefilledLatitudeText && LongitudeBox.Text == prefilledLongitudeText)
        {
            lat = null;
            lon = null;
        }

        ResultLevels = levels;
        ResultLatitude = lat;
        ResultLongitude = lon;
        DialogResult = true;
    }

    // Stesso livello e stesso nome della proposta: conserva i nomi nelle altre lingue e le coordinate lette all'import.
    private GeoLevel WithPrefilledData(GeoLevel level)
    {
        if (prefilled.TryGetValue(level.Tipo, out var proposed) && !proposed.Assente
            && string.Equals(proposed.Nome.Trim(), level.Nome, StringComparison.OrdinalIgnoreCase))
        {
            level.Nomi = [.. proposed.Nomi];
            level.Latitudine = proposed.Latitudine;
            level.Longitudine = proposed.Longitudine;
        }

        return level;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
