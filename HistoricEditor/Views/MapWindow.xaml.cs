using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Pen = Mapsui.Styles.Pen;

namespace HistoricEditor;

public partial class MapWindow : Window
{
    internal sealed record PersonOption(int? Id, string Etichetta)
    {
        public override string ToString() => Etichetta;
    }

    private sealed record FamilyOption(string Etichetta, IReadOnlySet<string>? Famiglie)
    {
        public override string ToString() => Etichetta;
    }

    private const string AllTypes = "Tutti i tipi";

    private static readonly (MapPointKind Kind, string Nome, Color Colore)[] KindColors =
    [
        (MapPointKind.Nascita, "Nascita", new Color(46, 139, 87)),
        (MapPointKind.Morte, "Morte", new Color(70, 70, 70)),
        (MapPointKind.Sepoltura, "Sepoltura", new Color(139, 90, 43)),
        (MapPointKind.Evento, "Evento", new Color(200, 40, 40)),
        (MapPointKind.Luogo, "Luogo", new Color(40, 100, 200)),
    ];

    private readonly PeopleStore store;
    private readonly MemoryLayer pointsLayer = new("Elementi storici") { Style = null };
    private readonly MemoryLayer selectionLayer = new("Selezione") { Style = null };
    private List<MapPoint> allPoints = [];
    private Dictionary<int, string> familyOfPerson = [];
    private List<MapLocation> locations = [];
    private bool loading;
    private bool fitPending = true;

    // Vero se dalla mappa sono state modificate schede o eventi.
    public bool Changed { get; private set; }

    public MapWindow(PeopleStore store, int? personId = null)
    {
        InitializeComponent();
        this.store = store;

        var map = new Mapsui.Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer("AlberiStorici/1.0 (github.com/msonzo1/HistoricEditor)"));
        map.Layers.Add(pointsLayer);
        map.Layers.Add(selectionLayer);
        map.Tapped += Map_Tapped;
        MapView.Map = map;

        BuildLegend();
        loading = true;
        LoadData();
        if (personId is int id)
        {
            PersonBox.SelectedItem = PersonBox.Items.OfType<PersonOption>().FirstOrDefault(p => p.Id == id) ?? PersonBox.Items[0];
        }

        loading = false;
        ApplyFilter();
        Loaded += (_, _) =>
        {
            FitAll();
            LocationList.Focus();
        };
    }

    private void BuildLegend()
    {
        foreach (var (_, nome, colore) in KindColors)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)colore.R, (byte)colore.G, (byte)colore.B)),
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 1.5,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock { Text = nome, FontSize = 12 });
            Legend.Children.Add(row);
        }

        Legend.Children.Add(new TextBlock { Text = "Il numero indica più elementi nello stesso punto.", FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("QuietBrush"), Margin = new Thickness(0, 4, 0, 0), MaxWidth = 200, TextWrapping = TextWrapping.Wrap });
    }

    private void LoadData()
    {
        var people = store.LoadAll();
        var events = store.GetEvents();
        allPoints = HistoricalMap.BuildPoints(people, events, store.GetAllPlaces(), store.GetSpecificPlaces());
        familyOfPerson = people.ToDictionary(p => p.Id, p => (p.Family ?? string.Empty).Trim());

        var selectedPerson = (PersonBox.SelectedItem as PersonOption)?.Id;
        var personOptions = new List<PersonOption> { new(null, "Tutti i personaggi") };
        personOptions.AddRange(store.GetPersonSummaries()
            .OrderBy(p => p.Etichetta, HistoricalMap.FamilyComparer)
            .Select(p => new PersonOption(p.Id, p.Etichetta)));
        PersonBox.ItemsSource = personOptions;
        PersonBox.SelectedItem = personOptions.FirstOrDefault(p => p.Id == selectedPerson) ?? personOptions[0];

        var selectedFamily = (DynastyBox.SelectedItem as FamilyOption)?.Etichetta;
        DynastyBox.ItemsSource = BuildFamilyOptions(store.GetDynasties(), people);
        DynastyBox.SelectedItem = DynastyBox.Items.OfType<FamilyOption>().FirstOrDefault(f => f.Etichetta == selectedFamily) ?? DynastyBox.Items[0];

        var selectedType = EventTypeBox.SelectedItem as string;
        var types = new List<string> { AllTypes };
        types.AddRange(events.Select(e => e.Tipo.Trim()).Where(t => t.Length > 0).Distinct(HistoricalMap.FamilyComparer).OrderBy(t => t, HistoricalMap.FamilyComparer));
        EventTypeBox.ItemsSource = types;
        EventTypeBox.SelectedItem = types.FirstOrDefault(t => t == selectedType) ?? AllTypes;
    }

    // Dinastie registrate (con i rami, indentati) seguite dalle altre famiglie presenti nelle schede.
    private static List<FamilyOption> BuildFamilyOptions(IReadOnlyList<Dinastia> dynasties, IEnumerable<HistoricalPerson> people)
    {
        var options = new List<FamilyOption> { new("Tutte le dinastie", null) };
        var known = new HashSet<string>(HistoricalMap.FamilyComparer);
        var byParent = dynasties.ToLookup(d => d.ParentId is int p && dynasties.Any(x => x.Id == p) && p != d.Id ? p : (int?)null);

        void Add(Dinastia dynasty, int depth, HashSet<int> path)
        {
            if (!path.Add(dynasty.Id))
            {
                return;
            }

            known.Add(dynasty.Nome.Trim());
            var label = new string(' ', depth * 4) + dynasty.Nome.Trim();
            options.Add(new FamilyOption(label, HistoricalMap.FamiliesOfDynasty(dynasty.Id, dynasties)));
            foreach (var child in byParent[dynasty.Id].OrderBy(d => d.Nome, HistoricalMap.FamilyComparer))
            {
                Add(child, depth + 1, path);
            }

            path.Remove(dynasty.Id);
        }

        foreach (var root in byParent[null].OrderBy(d => d.Nome, HistoricalMap.FamilyComparer))
        {
            Add(root, 0, []);
        }

        foreach (var family in people.SelectMany(p => PeopleGrouping.FamilyNames(p.Family))
                     .Where(f => !known.Contains(f))
                     .Distinct(HistoricalMap.FamilyComparer)
                     .OrderBy(f => f, HistoricalMap.FamilyComparer))
        {
            options.Add(new FamilyOption(family, new HashSet<string>([family], HistoricalMap.FamilyComparer)));
        }

        return options;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!loading && IsInitialized)
        {
            fitPending = true;
            ApplyFilter();
        }
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        loading = true;
        foreach (var check in new[] { BirthsCheck, DeathsCheck, BurialsCheck, EventsCheck, PlacesCheck })
        {
            check.IsChecked = true;
        }

        FromYearBox.Text = string.Empty;
        ToYearBox.Text = string.Empty;
        PersonBox.SelectedIndex = 0;
        DynastyBox.SelectedIndex = 0;
        EventTypeBox.SelectedIndex = 0;
        loading = false;
        fitPending = true;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var errors = new List<string>();
        int? ReadYear(TextBox box, string name)
        {
            var text = box.Text.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            var year = HistoricalYear.Parse(text);
            if (year is null)
            {
                errors.Add($"«{text}» non è un anno riconoscibile ({name}).");
            }

            return year;
        }

        var filter = new MapFilter
        {
            Nascite = BirthsCheck.IsChecked == true,
            Morti = DeathsCheck.IsChecked == true,
            Sepolture = BurialsCheck.IsChecked == true,
            Eventi = EventsCheck.IsChecked == true,
            Luoghi = PlacesCheck.IsChecked == true,
            DaAnno = ReadYear(FromYearBox, "dall’anno"),
            AAnno = ReadYear(ToYearBox, "all’anno"),
            PersonId = (PersonBox.SelectedItem as PersonOption)?.Id,
            Famiglie = (DynastyBox.SelectedItem as FamilyOption)?.Famiglie,
            TipoEvento = EventTypeBox.SelectedItem is string type && type != AllTypes ? type : string.Empty,
        };

        var visible = HistoricalMap.Filter(allPoints, filter, familyOfPerson);
        var selectedLocation = LocationList.SelectedItem as MapLocation;
        locations = HistoricalMap.GroupByLocation(visible);
        LocationList.ItemsSource = locations;
        UpdateFeatures();

        var summary = $"{visible.Count} elementi in {locations.Count} posizioni.";
        if (filter.DaAnno is int from && filter.AAnno is int to && to < from)
        {
            errors.Add("L’anno finale precede quello iniziale.");
        }

        SummaryText.Text = errors.Count > 0 ? string.Join(" ", errors) + " " + summary : summary;
        SummaryText.Foreground = (System.Windows.Media.Brush)FindResource(errors.Count > 0 ? "SignalBrush" : "QuietBrush");

        var reselect = selectedLocation is null ? null : locations.FirstOrDefault(l => SamePosition(l, selectedLocation));
        if (reselect is not null)
        {
            LocationList.SelectedItem = reselect;
        }
        else
        {
            ShowLocation(null);
        }

        if (fitPending && IsLoaded)
        {
            FitAll();
        }
    }

    private static bool SamePosition(MapLocation a, MapLocation b)
        => Math.Abs(a.Latitudine - b.Latitudine) < 1e-4 && Math.Abs(a.Longitudine - b.Longitudine) < 1e-4;

    private static MPoint Project(double lat, double lon)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        return new MPoint(x, y);
    }

    private static Color ColorOf(MapPointKind kind) => KindColors.First(k => k.Kind == kind).Colore;

    private void UpdateFeatures()
    {
        pointsLayer.Features = [.. locations.Select(location =>
        {
            var feature = new PointFeature(Project(location.Latitudine, location.Longitudine)) { Data = location };
            var count = location.Punti.Count;
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = count == 1 ? 0.45 : Math.Min(0.6 + (count * 0.03), 0.95),
                Fill = new Brush(ColorOf(location.KindPrevalente)),
                Outline = new Pen(Color.White, 2),
            });
            if (count > 1)
            {
                feature.Styles.Add(new LabelStyle
                {
                    Text = count.ToString(CultureInfo.CurrentCulture),
                    ForeColor = Color.White,
                    BackColor = null,
                    Font = new Font { Size = 11, Bold = true },
                    HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                    VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
                    CollisionDetection = false,
                });
            }

            return (IFeature)feature;
        })];
        pointsLayer.FeaturesWereModified();
        MapView.Map.RefreshGraphics();
    }

    private void HighlightSelection(MapLocation? location)
    {
        selectionLayer.Features = location is null
            ? []
            : [new PointFeature(Project(location.Latitudine, location.Longitudine))
            {
                Styles =
                {
                    new SymbolStyle
                    {
                        SymbolType = SymbolType.Ellipse,
                        SymbolScale = 1.2,
                        Fill = new Brush(Color.Transparent),
                        Outline = new Pen(new Color(255, 200, 0), 4),
                    },
                },
            }];
        selectionLayer.FeaturesWereModified();
        MapView.Map.RefreshGraphics();
    }

    private void FitAll_Click(object sender, RoutedEventArgs e) => FitAll();

    private void FitAll()
    {
        fitPending = false;
        if (locations.Count == 0)
        {
            return;
        }

        var projected = locations.Select(l => Project(l.Latitudine, l.Longitudine)).ToList();
        if (projected.Count == 1)
        {
            MapView.Map.Navigator.CenterOnAndZoomTo(projected[0], 150, 300);
            return;
        }

        var box = new MRect(projected.Min(p => p.X), projected.Min(p => p.Y), projected.Max(p => p.X), projected.Max(p => p.Y));
        // Margine attorno ai segnaposto; minimo ~20 km perché punti vicini non portino a uno zoom eccessivo.
        var margin = Math.Max(Math.Max(box.Width, box.Height) * 0.15, 20000);
        MapView.Map.Navigator.ZoomToBox(box.Grow(margin), MBoxFit.Fit, 300);
    }

    private void LocationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocationList.SelectedItem is MapLocation location)
        {
            ShowLocation(location);
            MapView.Map.Navigator.CenterOn(Project(location.Latitudine, location.Longitudine), 300);
        }
    }

    private void ShowLocation(MapLocation? location)
    {
        PointList.ItemsSource = location?.Punti;
        if (location is not null)
        {
            PointList.SelectedIndex = 0;
        }

        HighlightSelection(location);
        UpdateOpenButton();
    }

    private void Map_Tapped(object? sender, MapEventArgs e)
    {
        var info = e.GetMapInfo([pointsLayer]);
        if (info?.Feature is PointFeature { Data: MapLocation tapped })
        {
            var location = locations.FirstOrDefault(l => SamePosition(l, tapped));
            if (location is not null)
            {
                LocationList.SelectedItem = location;
                LocationList.ScrollIntoView(location);
            }
        }
    }

    private void PointList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOpenButton();

    private void UpdateOpenButton()
        => OpenButton.IsEnabled = PointList.SelectedItem is MapPoint { Kind: not MapPointKind.Luogo };

    private void PointList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void PointList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OpenSelected();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (PointList.SelectedItem is not MapPoint point)
        {
            return;
        }

        if (point.EventId is int eventId)
        {
            var window = new EventsWindow(store, null, selectEventId: eventId, onlyFocusPerson: false) { Owner = this };
            window.ShowDialog();
            Changed = true;
        }
        else if (point.PersonId is int personId && store.GetPerson(personId) is { } person)
        {
            var editor = new PersonEditorWindow(store, existing: person) { Owner = this };
            editor.ShowDialog();
            Changed |= editor.Saved;
        }
        else
        {
            return;
        }

        // Le modifiche possono aver cambiato luoghi, date o partecipanti: si ricalcola tutto mantenendo i filtri.
        loading = true;
        LoadData();
        loading = false;
        ApplyFilter();
    }

    private void Attribution_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText.Text = "Impossibile aprire il collegamento.";
        }

        e.Handled = true;
    }
}
