using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace HistoricEditor;

public partial class PlaceTreeControl : UserControl
{
    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private PeopleStore? store;
    private IReadOnlyList<Luogo> places = [];
    private IReadOnlyList<LuogoSpecifico> specifics = [];
    private bool suppressFilter;

    public PlaceTreeControl()
    {
        InitializeComponent();
        searchTimer.Tick += (_, _) =>
        {
            searchTimer.Stop();
            Rebuild(null, false);
        };
    }

    public bool IncludeSpecific { get; set; } = true;

    public PlaceNode? SelectedNode => Tree.SelectedItem as PlaceNode;

    public event EventHandler? SelectedNodeChanged;

    public event EventHandler? NodeActivated;

    public void Initialize(PeopleStore store, int? selectId = null, bool selectSpecific = false, bool showHistoric = false)
    {
        this.store = store;
        SetMode(showHistoric);
        Reload(selectId, selectSpecific);
    }

    public void Reload(int? selectId = null, bool selectSpecific = false)
    {
        if (store is null)
        {
            return;
        }

        places = store.GetAllPlaces();
        specifics = IncludeSpecific ? store.GetSpecificPlaces() : [];

        // Passa alla vista (attuali/storici) che contiene il luogo da selezionare.
        if (selectId is int id)
        {
            bool? historic = selectSpecific
                ? specifics.FirstOrDefault(s => s.Id == id) is { } specifico ? PlaceNode.IsHistoricSpecific(specifico) : null
                : places.FirstOrDefault(l => l.Id == id)?.Storico;
            if (historic is bool value)
            {
                SetMode(value);
            }
        }

        Rebuild(selectId, selectSpecific);
    }

    public void FocusSearch() => SearchBox.Focus();

    private bool ShowHistoric => HistoricRadio.IsChecked == true;

    private void SetMode(bool historic)
    {
        suppressFilter = true;
        HistoricRadio.IsChecked = historic;
        CurrentRadio.IsChecked = !historic;
        suppressFilter = false;
    }

    private void Rebuild(int? selectId, bool selectSpecific)
    {
        if (store is null)
        {
            return;
        }

        if (selectId is null && SelectedNode is { } current)
        {
            selectId = current.Id;
            selectSpecific = current.IsSpecific;
        }

        var query = SearchBox.Text.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        bool Matches(PlaceNode node) => node.IsHistoric == ShowHistoric
            && (query.Length == 0
                || compare.IndexOf(node.Nome, query, SearchOptions) >= 0
                || compare.IndexOf(node.Tipo, query, SearchOptions) >= 0);

        var classic = places.ToDictionary(l => l.Id, PlaceNode.Classic);
        foreach (var node in classic.Values)
        {
            if (node.Source!.ParentId is int parentId && classic.TryGetValue(parentId, out var parent))
            {
                node.Parent = parent;
            }
        }

        var specificNodes = new Dictionary<int, PlaceNode>();
        foreach (var specifico in specifics)
        {
            if (classic.TryGetValue(specifico.LuogoId, out var parent))
            {
                var node = PlaceNode.Specific(specifico);
                node.Parent = parent;
                specificNodes[specifico.Id] = node;
            }
        }

        // Ogni risultato porta con sé i suoi contenitori, così resta visibile il contesto.
        var included = new HashSet<PlaceNode>();
        var matches = 0;
        foreach (var node in classic.Values.Concat(specificNodes.Values).Where(Matches))
        {
            matches++;
            included.Add(node);
            for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                included.Add(ancestor);
                if (query.Length > 0)
                {
                    ancestor.IsExpanded = true;
                }
            }
        }

        var roots = new ObservableCollection<PlaceNode>();
        foreach (var node in included.OrderBy(n => n.IsSpecific).ThenBy(n => n.Nome, StringComparer.CurrentCultureIgnoreCase))
        {
            if (node.Parent is { } parent)
            {
                parent.Figli.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        Tree.ItemsSource = roots;
        CountText.Text = query.Length > 0
            ? $"{matches} {(matches == 1 ? "risultato" : "risultati")}"
            : $"{matches} {(matches == 1 ? "luogo" : "luoghi")}";

        var lookup = selectSpecific ? specificNodes : classic;
        if (selectId is int id && lookup.TryGetValue(id, out var target) && included.Contains(target))
        {
            for (var ancestor = target.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                ancestor.IsExpanded = true;
            }

            target.IsSelected = true;
        }

        SelectedNodeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        searchTimer.Stop();
        searchTimer.Start();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!suppressFilter)
        {
            Rebuild(null, false);
        }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => SelectedNodeChanged?.Invoke(this, EventArgs.Empty);

    private void TreeItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // L'evento risale ai nodi genitori: lo gestisce solo l'elemento selezionato.
        if (sender is TreeViewItem { IsSelected: true })
        {
            e.Handled = true;
            NodeActivated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TreeItem_Selected(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && sender is TreeViewItem item)
        {
            item.BringIntoView();
        }
    }
}

public sealed class PlaceNode : INotifyPropertyChanged
{
    private bool isExpanded;
    private bool isSelected;

    private PlaceNode(int id, string nome, string tipo, bool isHistoric, Luogo? source)
    {
        Id = id;
        Nome = nome;
        Tipo = tipo;
        IsHistoric = isHistoric;
        Source = source;
    }

    public static PlaceNode Classic(Luogo luogo) => new(luogo.Id, luogo.Nome, luogo.Tipo, luogo.Storico, luogo);

    public static PlaceNode Specific(LuogoSpecifico specifico)
        => new(specifico.Id, specifico.Nome, specifico.Tipo.Length > 0 ? specifico.Tipo : "luogo specifico", IsHistoricSpecific(specifico), null);

    public static bool IsHistoricSpecific(LuogoSpecifico specifico) => specifico.Condizione == CondizioniLuogo.Distrutto;

    // Valorizzato solo per i luoghi amministrativi o storici.
    public Luogo? Source { get; }
    public bool IsSpecific => Source is null;
    public bool IsHistoric { get; }
    public int Id { get; }
    public string Nome { get; }
    public string Tipo { get; }

    // Glifi Segoe MDL2: segnaposto per i luoghi specifici, orologio per quelli storici.
    public string Icona => IsSpecific ? "\uE707" : IsHistoric ? "\uE81C" : string.Empty;

    public string AccessibleName => $"{Nome}, {Tipo}";

    public string Path
    {
        get
        {
            var parts = new List<string>();
            for (var current = this; current is not null; current = current.Parent)
            {
                parts.Insert(0, current.Nome);
            }

            return string.Join(" › ", parts);
        }
    }

    public PlaceNode? Parent { get; set; }
    public ObservableCollection<PlaceNode> Figli { get; } = [];

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
