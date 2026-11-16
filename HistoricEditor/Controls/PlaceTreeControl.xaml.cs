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
    private IReadOnlyList<PendingStep> pendingSteps = [];
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

    // Luogo importato non ancora nell'archivio (2026-11-15): le parti che mancano compaiono come luoghi nuovi, al loro
    // posto nell'albero, così il luogo si vede e si corregge prima del salvataggio.
    public PendingPlace? Pending { get; set; }

    // Il nodo del luogo in attesa (l'ultima parte della sua catena), dopo l'ultima ricostruzione dell'albero.
    public PlaceNode? PendingTarget { get; private set; }

    public event EventHandler? SelectedNodeChanged;

    public event EventHandler? NodeActivated;

    public void Initialize(PeopleStore store, int? selectId = null, bool selectSpecific = false, bool showHistoric = false, bool selectPending = false)
    {
        this.store = store;
        SetMode(showHistoric);
        Reload(selectId, selectSpecific, selectPending);
    }

    public void Reload(int? selectId = null, bool selectSpecific = false, bool selectPending = false)
    {
        if (store is null)
        {
            return;
        }

        places = store.GetAllPlaces();
        specifics = IncludeSpecific ? store.GetSpecificPlaces() : [];
        pendingSteps = Pending is null ? [] : ImportedPlaceResolver.Steps(store, Pending).Where(s => IncludeSpecific || s.Kind != PendingStepKind.Specific).ToList();

        // Il luogo in attesa da selezionare: si apre la vista che lo contiene, poi la selezione passa dal nodo.
        if (selectPending && pendingSteps.Count > 0)
        {
            var last = pendingSteps[^1];
            SetMode(last.Kind switch
            {
                PendingStepKind.Historic => true,
                PendingStepKind.Specific => pendingSteps.Any(p => p.Kind == PendingStepKind.Historic)
                    || (last.ExistingId is int s ? specifics.FirstOrDefault(x => x.Id == s) is { } x && (PlaceNode.IsHistoricSpecific(x) || InsideHistoric(x.LuogoId)) : last.Distrutto),
                _ => false,
            });
            Rebuild(null, false, selectPending: true);
            return;
        }

        // Passa alla vista (attuali/storici) che contiene il luogo da selezionare.
        if (selectId is int id)
        {
            bool? historic = selectSpecific
                ? specifics.FirstOrDefault(s => s.Id == id) is { } specifico ? PlaceNode.IsHistoricSpecific(specifico) || InsideHistoric(specifico.LuogoId) : null
                : places.FirstOrDefault(l => l.Id == id)?.Storico;
            if (historic is bool value)
            {
                SetMode(value);
            }
        }

        Rebuild(selectId, selectSpecific);
    }

    public void FocusSearch() => SearchBox.Focus();

    // Vero se il luogo o uno dei suoi contenitori è un luogo storico.
    private bool InsideHistoric(int luogoId)
    {
        var byId = places.ToDictionary(l => l.Id);
        var visited = new HashSet<int>();
        for (int? id = luogoId; id is int current && visited.Add(current) && byId.TryGetValue(current, out var luogo); id = luogo.ParentId)
        {
            if (luogo.Storico)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShowHistoric => HistoricRadio.IsChecked == true;

    private void SetMode(bool historic)
    {
        suppressFilter = true;
        HistoricRadio.IsChecked = historic;
        CurrentRadio.IsChecked = !historic;
        suppressFilter = false;
    }

    private void Rebuild(int? selectId, bool selectSpecific, bool selectPending = false)
    {
        if (store is null)
        {
            return;
        }

        if (selectId is null && !selectPending && SelectedNode is { } current)
        {
            if (current.IsPending)
            {
                selectPending = true;
            }
            else
            {
                selectId = current.Id;
                selectSpecific = current.IsSpecific;
            }
        }

        var query = SearchBox.Text.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        // Un luogo specifico dentro un luogo storico (le rovine di una città antica) sta nella vista dei luoghi storici:
        // altrimenti tra quelli attuali compariva anche il luogo storico che lo contiene (2026-11-16, da Antiochia di Siria).
        static bool InHistoricView(PlaceNode node)
        {
            if (node.IsHistoric)
            {
                return true;
            }

            if (!node.IsSpecific)
            {
                return false;
            }

            for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor.IsHistoric)
                {
                    return true;
                }
            }

            return false;
        }

        bool Matches(PlaceNode node) => InHistoricView(node) == ShowHistoric
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

        // Le parti del luogo in attesa che non sono ancora nell'archivio, appese all'ultima che c'è già.
        var pendingNodes = new List<PlaceNode>();
        PlaceNode? chain = null;
        PendingTarget = null;
        foreach (var step in pendingSteps)
        {
            PlaceNode? node;
            if (step.ExistingId is int existingId)
            {
                node = step.Kind == PendingStepKind.Specific ? specificNodes.GetValueOrDefault(existingId) : classic.GetValueOrDefault(existingId);
                if (node is null)
                {
                    break;
                }
            }
            else
            {
                node = PlaceNode.ForPending(-1 - pendingNodes.Count, step);
                node.Parent = chain;
                pendingNodes.Add(node);
            }

            chain = node;
            PendingTarget = node;
        }

        // Ogni risultato porta con sé i suoi contenitori, così resta visibile il contesto.
        var included = new HashSet<PlaceNode>();
        var matches = 0;
        foreach (var node in classic.Values.Concat(specificNodes.Values).Concat(pendingNodes).Where(Matches))
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
        PlaceNode? target = null;
        if (selectPending)
        {
            target = PendingTarget;
        }
        else if (selectId is int id)
        {
            lookup.TryGetValue(id, out target);
        }

        if (target is not null && included.Contains(target))
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

    private PlaceNode(int id, string nome, string tipo, bool isHistoric, Luogo? source, bool isSpecific, bool isPending = false)
    {
        Id = id;
        Nome = nome;
        Tipo = tipo;
        IsHistoric = isHistoric;
        Source = source;
        IsSpecific = isSpecific;
        IsPending = isPending;
    }

    public static PlaceNode Classic(Luogo luogo) => new(luogo.Id, luogo.Nome, luogo.Tipo, luogo.Storico, luogo, false);

    public static PlaceNode Specific(LuogoSpecifico specifico)
        => new(specifico.Id, specifico.Nome, specifico.Tipo.Length > 0 ? specifico.Tipo : "luogo specifico", IsHistoricSpecific(specifico), null, true);

    // Parte di un luogo importato non ancora nell'archivio: id negativo, mai uguale a quello di un luogo salvato.
    internal static PlaceNode ForPending(int id, PendingStep step)
    {
        var tipo = step.Tipo.Length > 0 ? step.Tipo : step.Kind == PendingStepKind.Specific ? "luogo specifico" : "luogo storico";
        return new(id, step.Nome, $"{tipo} · nuovo, sarà creato al salvataggio", step.Kind == PendingStepKind.Historic || step.Distrutto, null,
            step.Kind == PendingStepKind.Specific, isPending: true);
    }

    public static bool IsHistoricSpecific(LuogoSpecifico specifico) => specifico.Condizione == CondizioniLuogo.Distrutto;

    // Valorizzato solo per i luoghi amministrativi o storici.
    public Luogo? Source { get; }
    public bool IsSpecific { get; }

    // Parte di un luogo importato che si creerà al salvataggio della scheda, dell'evento o dell'opera.
    public bool IsPending { get; }
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
