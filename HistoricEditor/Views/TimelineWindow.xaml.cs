using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HistoricEditor;

// Linea del tempo di un personaggio (eventualmente con i familiari) o di una dinastia: grafico a corsie ed elenco cronologico.
public partial class TimelineWindow : Window
{
    private const double LabelWidth = 190;
    private const double AxisHeight = 26;
    private const double LaneHeight = 34;
    private const double RightPadding = 24;

    private readonly PeopleStore store;
    private List<HistoricalPerson> people = [];
    private IReadOnlyList<Unione> unions = [];
    private IReadOnlyList<Evento> events = [];
    private IReadOnlyList<Dinastia> dynasties = [];
    private TimelineData data = new([], []);
    private readonly Dictionary<TimelineEntry, List<Shape>> markers = [];
    private bool loading;

    // Scelta del personaggio: ToString serve agli screen reader e alla ricerca per testo.
    internal sealed record PersonChoice(int Id, string Etichetta)
    {
        public override string ToString() => Etichetta;
    }

    // Vero se dalla finestra sono state modificate schede o eventi.
    public bool Changed { get; private set; }

    internal TimelineData Data => data;

    public TimelineWindow(PeopleStore store, int? personId = null, string? family = null)
    {
        InitializeComponent();
        this.store = store;
        loading = true;
        LoadData();
        PersonBox.SelectedItem = PersonBox.Items.OfType<PersonChoice>().FirstOrDefault(p => p.Id == personId)
            ?? PersonBox.Items.OfType<PersonChoice>().FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(family))
        {
            FamilyBox.SelectedItem = FamilyBox.Items.OfType<TimelineFamilyOption>()
                .FirstOrDefault(f => HistoricalMap.FamilyComparer.Equals(f.Nome, family.Trim()));
            FamilyMode.IsChecked = FamilyBox.SelectedItem is not null;
        }

        FamilyBox.SelectedItem ??= FamilyBox.Items.OfType<TimelineFamilyOption>().FirstOrDefault();
        loading = false;
        Rebuild();
        Loaded += (_, _) =>
        {
            FitZoom();
            (TimelineGrid.Items.Count > 0 ? (Control)TimelineGrid : PersonBox).Focus();
        };
    }

    private void LoadData()
    {
        people = store.LoadAll();
        unions = store.GetUnions();
        events = store.GetEvents();
        dynasties = store.GetDynasties();
        var selectedPerson = (PersonBox.SelectedItem as PersonChoice)?.Id;
        var selectedFamily = (FamilyBox.SelectedItem as TimelineFamilyOption)?.Nome;
        PersonBox.ItemsSource = people
            .Select(p => new PersonChoice(p.Id, PeopleGrouping.DescribeDates(p.Birth, p.Death) is { Length: > 0 } anni ? $"{p.DisplayName} ({anni})" : p.DisplayName))
            .OrderBy(p => p.Etichetta, HistoricalMap.FamilyComparer)
            .ToList();
        FamilyBox.ItemsSource = HistoricalTimeline.FamilyOptions(people, dynasties);
        PersonBox.SelectedItem = PersonBox.Items.OfType<PersonChoice>().FirstOrDefault(p => p.Id == selectedPerson);
        FamilyBox.SelectedItem = FamilyBox.Items.OfType<TimelineFamilyOption>().FirstOrDefault(f => f.Nome == selectedFamily);
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        if (!loading && PersonBox is not null && FamilyBox is not null)
        {
            Rebuild();
            if (IsLoaded)
            {
                FitZoom();
            }
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!loading && TimelineGrid is not null)
        {
            ApplyFilter();
        }
    }

    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!loading && Chart is not null)
        {
            DrawChart();
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => FitZoom();

    internal void Rebuild()
    {
        var familyMode = FamilyMode.IsChecked == true;
        PersonBox.IsEnabled = !familyMode;
        RelativesCheck.IsEnabled = !familyMode;
        FamilyBox.IsEnabled = familyMode;

        HashSet<int> ids;
        if (familyMode && FamilyBox.SelectedItem is TimelineFamilyOption family)
        {
            var names = family.DinastiaId is int id ? HistoricalMap.FamiliesOfDynasty(id, dynasties) : [family.Nome];
            ids = HistoricalTimeline.FamilyMembers(people, names);
            Title = $"Linea del tempo — {family.Nome}";
        }
        else if (!familyMode && PersonBox.SelectedItem is PersonChoice person)
        {
            ids = RelativesCheck.IsChecked == true ? HistoricalTimeline.Relatives(person.Id, people, unions) : [person.Id];
            Title = $"Linea del tempo — {people.FirstOrDefault(p => p.Id == person.Id)?.DisplayName}";
        }
        else
        {
            ids = [];
            Title = "Linea del tempo";
        }

        data = HistoricalTimeline.Build(people, unions, events, ids);
        ApplyFilter();
    }

    private bool IsShown(TimelineCategory category) => category switch
    {
        TimelineCategory.Vita => ShowLifeCheck.IsChecked == true,
        TimelineCategory.Carica => ShowOfficesCheck.IsChecked == true,
        TimelineCategory.Unione => ShowUnionsCheck.IsChecked == true,
        _ => ShowEventsCheck.IsChecked == true,
    };

    private void ApplyFilter()
    {
        var selected = TimelineGrid.SelectedItem as TimelineEntry;
        var visible = data.Voci.Where(v => IsShown(v.Categoria)).ToList();
        TimelineGrid.ItemsSource = visible;
        TimelineGrid.SelectedItem = visible.Contains(selected!) ? selected : null;
        CopyButton.IsEnabled = visible.Count > 0;

        var withoutYear = visible.Count(v => v.Anno is null);
        var parts = new List<string>
        {
            visible.Count == 1 ? "1 voce" : $"{visible.Count} voci",
            data.Corsie.Count == 1 ? "1 persona" : $"{data.Corsie.Count} persone",
        };
        if (data.Intervallo is { } range)
        {
            parts.Add($"{HistoricalYear.Format(range.Min)}–{HistoricalYear.Format(range.Max)}");
        }

        if (withoutYear > 0)
        {
            parts.Add($"{withoutYear} senza anno riconoscibile");
        }

        SummaryText.Text = data.Voci.Count == 0 ? "Nessuna data da mostrare: completa nascita, morte, cariche o eventi nelle schede." : string.Join(" · ", parts);
        DrawChart();
    }

    // Sceglie lo zoom per far stare tutto il periodo nella larghezza del grafico.
    private void FitZoom()
    {
        if (data.Intervallo is not { } range || ChartScroll.ActualWidth <= 0)
        {
            return;
        }

        var (min, max) = range;

        var available = ChartScroll.ActualWidth - LabelWidth - RightPadding - SystemParameters.VerticalScrollBarWidth;
        ZoomSlider.Value = Math.Clamp(available / (max - min + 2), ZoomSlider.Minimum, ZoomSlider.Maximum);
        DrawChart();
    }

    private Brush BrushOf(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private Brush CategoryBrush(TimelineCategory category) => category switch
    {
        TimelineCategory.Vita => BrushOf("GreenBrush", Brushes.SeaGreen),
        TimelineCategory.Carica => BrushOf("SignalBrush", Brushes.DarkOrange),
        TimelineCategory.Unione => BrushOf("InkSoftBrush", Brushes.SlateGray),
        _ => BrushOf("DangerBrush", Brushes.Firebrick),
    };

    internal void DrawChart()
    {
        Chart.Children.Clear();
        markers.Clear();
        var lanes = data.Corsie
            .Select(l => l with
            {
                Barre = [.. l.Barre.Where(b => IsShown(b.Categoria))],
                Voci = [.. l.Voci.Where(v => IsShown(v.Categoria))],
            })
            .Where(l => l.Barre.Count > 0 || l.Voci.Count > 0)
            .ToList();
        if (data.Intervallo is not { } period || lanes.Count == 0)
        {
            Chart.Width = Chart.Height = 0;
            ChartEmpty.Visibility = Visibility.Visible;
            return;
        }

        ChartEmpty.Visibility = Visibility.Collapsed;
        var min = period.Min - 1;
        var max = period.Max + 1;
        var scale = ZoomSlider.Value;
        double X(int year) => LabelWidth + (year - min) * scale;
        Chart.Width = X(max) + RightPadding;
        Chart.Height = AxisHeight + lanes.Count * LaneHeight + 6;

        var ink = BrushOf("InkBrush", Brushes.Black);
        var quiet = BrushOf("QuietBrush", Brushes.Gray);
        var line = BrushOf("LineBrush", Brushes.LightGray);

        int[] steps = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000];
        var step = steps.FirstOrDefault(s => s * scale >= 64, 5000);
        for (var tick = (int)Math.Ceiling(min / (double)step) * step; tick <= max; tick += step)
        {
            Chart.Children.Add(new Line { X1 = X(tick), X2 = X(tick), Y1 = AxisHeight - 4, Y2 = Chart.Height, Stroke = line, StrokeThickness = 1 });
            var label = new TextBlock { Text = HistoricalYear.Format(tick), FontSize = 11, Foreground = quiet };
            Canvas.SetLeft(label, X(tick) + 3);
            Canvas.SetTop(label, 4);
            Chart.Children.Add(label);
        }

        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            var y = AxisHeight + i * LaneHeight;
            Chart.Children.Add(new Line { X1 = 0, X2 = Chart.Width, Y1 = y + LaneHeight, Y2 = y + LaneHeight, Stroke = line, StrokeThickness = 0.5 });
            var name = new TextBlock
            {
                Text = lane.Persona,
                Width = LabelWidth - 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ink,
                ToolTip = lane.Persona,
            };
            Canvas.SetLeft(name, 6);
            Canvas.SetTop(name, y + 8);
            Chart.Children.Add(name);

            foreach (var bar in lane.Barre)
            {
                var life = bar.Categoria == TimelineCategory.Vita;
                var rect = new Rectangle
                {
                    Width = Math.Max(2, (bar.Fine - bar.Inizio) * scale),
                    Height = life ? 12 : 6,
                    Fill = CategoryBrush(bar.Categoria),
                    Opacity = life ? 0.35 : 0.75,
                    RadiusX = 2,
                    RadiusY = 2,
                    ToolTip = bar.Etichetta,
                };
                if (bar.FineAperta)
                {
                    rect.Stroke = CategoryBrush(bar.Categoria);
                    rect.StrokeDashArray = [2, 2];
                    rect.Opacity = 0.45;
                }

                Canvas.SetLeft(rect, X(bar.Inizio));
                Canvas.SetTop(rect, life ? y + 7 : y + 22);
                Chart.Children.Add(rect);
            }

            foreach (var entry in lane.Voci)
            {
                var dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = CategoryBrush(entry.Categoria),
                    Stroke = BrushOf("AppSurface", Brushes.White),
                    StrokeThickness = 1,
                    ToolTip = entry.Etichetta,
                    Cursor = Cursors.Hand,
                };
                dot.MouseLeftButtonUp += (_, _) => SelectEntry(entry);
                Canvas.SetLeft(dot, X(entry.Anno!.Value) - 5);
                Canvas.SetTop(dot, y + 8);
                Chart.Children.Add(dot);
                if (!markers.TryGetValue(entry, out var shapes))
                {
                    markers[entry] = shapes = [];
                }

                shapes.Add(dot);
            }
        }

        Highlight(TimelineGrid.SelectedItem as TimelineEntry);
    }

    private void SelectEntry(TimelineEntry entry)
    {
        TimelineGrid.SelectedItem = entry;
        TimelineGrid.ScrollIntoView(entry);
        TimelineGrid.Focus();
    }

    private void Highlight(TimelineEntry? selected)
    {
        var ink = BrushOf("InkBrush", Brushes.Black);
        var surface = BrushOf("AppSurface", Brushes.White);
        foreach (var (entry, shapes) in markers)
        {
            foreach (var shape in shapes)
            {
                var on = entry == selected;
                shape.Stroke = on ? ink : surface;
                shape.StrokeThickness = on ? 2.5 : 1;
                Panel.SetZIndex(shape, on ? 10 : 1);
            }
        }

        if (selected is not null && markers.TryGetValue(selected, out var visible) && visible.Count > 0)
        {
            var left = Canvas.GetLeft(visible[0]);
            if (left < ChartScroll.HorizontalOffset + LabelWidth || left > ChartScroll.HorizontalOffset + ChartScroll.ViewportWidth - RightPadding)
            {
                ChartScroll.ScrollToHorizontalOffset(Math.Max(0, left - LabelWidth - ChartScroll.ViewportWidth / 3));
            }
        }
    }

    private void TimelineGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var entry = TimelineGrid.SelectedItem as TimelineEntry;
        OpenButton.IsEnabled = entry is not null;
        Highlight(entry);
    }

    private void TimelineGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(TimelineGrid, source) is DataGridRow)
        {
            OpenSelected();
        }
    }

    private void TimelineGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TimelineGrid.SelectedItem is TimelineEntry)
        {
            e.Handled = true;
            OpenSelected();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (TimelineGrid.SelectedItem is not TimelineEntry entry)
        {
            return;
        }

        bool changed;
        if (entry.EventoId is int eventId)
        {
            var window = new EventsWindow(store, entry.PersonIds.FirstOrDefault(), eventId, onlyFocusPerson: false) { Owner = this };
            window.ShowDialog();
            changed = true;
        }
        else if (entry.PersonIds.Count > 0 && store.GetPerson(entry.PersonIds[0]) is { } person)
        {
            var editor = new PersonEditorWindow(store, existing: person) { Owner = this };
            editor.ShowDialog();
            changed = editor.Saved;
        }
        else
        {
            return;
        }

        if (!changed)
        {
            return;
        }

        Changed = true;
        loading = true;
        LoadData();
        loading = false;
        Rebuild();
        TimelineGrid.SelectedItem = TimelineGrid.Items.OfType<TimelineEntry>()
            .FirstOrDefault(v => v.EventoId == entry.EventoId && v.Tipo == entry.Tipo && v.PersonIds.SequenceEqual(entry.PersonIds));
        StatusText.Text = "Linea del tempo aggiornata.";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineGrid.ItemsSource is not IEnumerable<TimelineEntry> entries)
        {
            return;
        }

        try
        {
            Clipboard.SetText($"{Title}{Environment.NewLine}{HistoricalTimeline.ToText(entries)}");
            StatusText.Text = "Elenco copiato negli appunti.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = "Impossibile copiare negli appunti: riprova.";
        }
    }
}
