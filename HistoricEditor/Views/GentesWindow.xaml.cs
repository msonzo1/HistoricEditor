using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Nodo dell'albero delle gentes: una gens o un suo ramo, dell'archivio o letto da un import e non ancora salvato.
public sealed class GentesNode : INotifyPropertyChanged
{
    private bool isExpanded;
    private bool isSelected;

    public GentesNode(GensRomana gens, RamoGens? ramo = null, PendingGens? pending = null)
    {
        Gens = gens;
        Ramo = ramo;
        Pending = pending;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // La gens del nodo; per un ramo, la gens a cui appartiene.
    public GensRomana Gens { get; }

    public RamoGens? Ramo { get; }

    // Gens o ramo letti dall'import, da creare al salvataggio della scheda.
    public PendingGens? Pending { get; }

    public bool IsRamo => Ramo is not null;

    public bool IsPending => Pending is not null;

    public string Etichetta => IsRamo ? GensLabels.Branch(Gens, Ramo) : Gens.Etichetta;

    public string Nota => IsPending ? (IsRamo ? "nuovo, sarà creato al salvataggio" : "nuova, sarà creata al salvataggio")
        : !IsRamo && Gens.DaCompletare.Length > 0 ? $"da completare: {Gens.DaCompletare}"
        : string.Empty;

    public string AccessibleName => Nota.Length > 0 ? $"{Etichetta}, {Nota}" : Etichetta;

    public ObservableCollection<GentesNode> Rami { get; } = [];

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            isSelected = value;
            OnPropertyChanged();
        }
    }

    public IEnumerable<GentesNode> SelfAndDescendants() => Rami.SelectMany(r => r.SelfAndDescendants()).Prepend(this);

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Gentes e rami (2026-11-15): elenco, modifica e, aperta dalla scheda, scelta della gens o del ramo. Le gentes lette da un
// import e non ancora salvate compaiono come nuove e si correggono in memoria.
public partial class GentesWindow : Window
{
    private static readonly StringComparer Comparer = PeopleGrouping.FamilyComparer;

    private readonly PeopleStore store;
    private readonly bool pick;
    private readonly List<PendingGens> pending;
    private List<GentesNode> roots = [];
    private IReadOnlyList<GensRomana> gentes = [];

    // Nodo mostrato nel modulo; null con una gens o un ramo nuovi (editingRamo dice quale dei due).
    private GentesNode? current;
    private bool editingRamo;
    private bool loading;

    // Scelta per la scheda (solo aperta per scegliere).
    public GensChoice? Result { get; private set; }

    // Vero se sono state modificate gentes o rami (anche quelli in attesa): chi ha aperto la finestra aggiorna i testi.
    public bool Changed { get; private set; }

    public GentesWindow(PeopleStore store, GensChoice? select = null, IEnumerable<PendingGens>? pending = null, bool pick = false)
    {
        InitializeComponent();
        this.store = store;
        this.pick = pick;
        this.pending = [.. (pending ?? []).Where(p => !p.Removed)];
        if (pick)
        {
            Title = "Scegli la gens";
            SelectButton.Visibility = Visibility.Visible;
            CloseButton.Content = "Annulla";
        }

        Reload(node => Matches(node, select));
        if (current is null)
        {
            ShowNode(null, ramo: false);
        }

        Loaded += (_, _) => GensTree.Focus();
    }

    private bool Matches(GentesNode node, GensChoice? choice)
    {
        if (choice is null || choice.IsEmpty)
        {
            return false;
        }

        if (choice.Pending is { } p)
        {
            if (node.Pending == p)
            {
                // Della gens in attesa con un ramo si seleziona il ramo.
                return node.IsRamo == (p.Data.Ramo is not null);
            }

            var (found, foundRamo) = store.FindImportedGens(p.Data);
            return !node.IsPending && (foundRamo is not null ? node.Ramo?.Id == foundRamo.Id : !node.IsRamo && node.Gens.Id == found?.Id && p.Data.Ramo is null);
        }

        return !node.IsPending && (choice.RamoId is int ramoId ? node.Ramo?.Id == ramoId : !node.IsRamo && node.Gens.Id == choice.GensId);
    }

    private void Reload(Func<GentesNode, bool>? select)
    {
        gentes = store.GetGentes();
        var expanded = roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.IsExpanded).Select(n => n.Etichetta).ToHashSet(Comparer);
        var nodes = new List<GentesNode>();
        foreach (var gens in gentes)
        {
            var node = new GentesNode(gens);
            foreach (var ramo in gens.Rami.OrderBy(r => GensLabels.Branch(gens, r), Comparer))
            {
                ramo.Gens = gens;
                node.Rami.Add(new GentesNode(gens, ramo));
            }

            nodes.Add(node);
        }

        // Gentes e rami letti dall'import che non sono ancora nell'archivio.
        foreach (var p in pending.Where(p => !p.Removed))
        {
            var (found, foundRamo) = store.FindImportedGens(p.Data);
            var (gens, ramo) = p.Data.ToEntities();
            if (found is null)
            {
                var node = nodes.FirstOrDefault(n => n.IsPending && Comparer.Equals(n.Gens.Nome, gens.Nome));
                if (node is null)
                {
                    node = new GentesNode(gens, pending: p);
                    nodes.Add(node);
                }

                if (ramo is not null)
                {
                    ramo.Gens = node.Gens;
                    node.Rami.Add(new GentesNode(node.Gens, ramo, p));
                    node.IsExpanded = true;
                }
            }
            else if (ramo is not null && foundRamo is null && nodes.FirstOrDefault(n => !n.IsPending && n.Gens.Id == found.Id) is { } owner)
            {
                ramo.Gens = owner.Gens;
                owner.Rami.Add(new GentesNode(owner.Gens, ramo, p));
                owner.IsExpanded = true;
            }
        }

        roots = [.. nodes.OrderBy(n => n.Etichetta, Comparer)];
        foreach (var node in roots.SelectMany(r => r.SelfAndDescendants()).Where(n => expanded.Contains(n.Etichetta)))
        {
            node.IsExpanded = true;
        }

        GensTree.ItemsSource = roots;
        RamoGensBox.ItemsSource = gentes;
        var target = select is null ? null : roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(select);
        if (target is not null)
        {
            foreach (var root in roots.Where(r => r.Rami.Contains(target)))
            {
                root.IsExpanded = true;
            }

            target.IsSelected = true;
            ShowNode(target, target.IsRamo);
        }
    }

    private GentesNode? SelectedNode => GensTree.SelectedItem as GentesNode;

    private void GensTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (SelectedNode is { } node)
        {
            ShowNode(node, node.IsRamo);
        }
    }

    private void TreeItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (pick && sender is TreeViewItem { IsSelected: true })
        {
            e.Handled = true;
            Select_Click(sender, e);
        }
    }

    // Modulo della gens o del ramo (node null: nuovi).
    private void ShowNode(GentesNode? node, bool ramo)
    {
        loading = true;
        current = node;
        editingRamo = ramo;
        GensPanel.Visibility = ramo ? Visibility.Collapsed : Visibility.Visible;
        RamoPanel.Visibility = ramo ? Visibility.Visible : Visibility.Collapsed;
        var gens = node?.Gens;
        GensNameBox.Text = ramo ? string.Empty : gens?.Nome ?? string.Empty;
        NomenBox.Text = ramo ? string.Empty : gens?.Nomen ?? string.Empty;
        GensPluralBox.Text = ramo ? string.Empty : gens?.Plurale ?? string.Empty;
        GensItalianBox.Text = ramo ? string.Empty : gens?.NomeItaliano ?? string.Empty;
        GensItalianPluralBox.Text = ramo ? string.Empty : gens?.PluraleItaliano ?? string.Empty;
        RamoPluralBox.Text = node?.Ramo?.Plurale ?? string.Empty;
        CognomenBox.Text = node?.Ramo?.Cognomen ?? string.Empty;
        RamoItalianBox.Text = node?.Ramo?.NomeItaliano ?? string.Empty;
        WikidataBox.Text = (ramo ? node?.Ramo?.WikidataId : gens?.WikidataId) ?? string.Empty;
        NoteBox.Text = (ramo ? node?.Ramo?.Note : gens?.Note) ?? string.Empty;
        if (ramo)
        {
            RamoGensBox.SelectedItem = gentes.FirstOrDefault(g => g.Id == (node?.Gens.Id ?? SelectedGensId()));
        }

        // Gens e rami in attesa si correggono in memoria: non hanno ancora note, e il ramo non cambia gens.
        var isPending = node?.IsPending == true;
        NoteBox.IsEnabled = !isPending;
        RamoGensBox.IsEnabled = !isPending;
        RamoGensBox.Visibility = isPending ? Visibility.Collapsed : Visibility.Visible;
        RamoGensText.Visibility = isPending ? Visibility.Visible : Visibility.Collapsed;
        RamoGensText.Text = isPending ? node!.Gens.Etichetta : string.Empty;
        DeleteButton.IsEnabled = node is not null;
        NewRamoButton.IsEnabled = node is { IsPending: false };
        var count = node is null || isPending ? 0 : ramo ? store.CountPeopleUsingRamo(node.Ramo!.Id) : store.CountPeopleUsingGens(node.Gens.Id);
        UsageText.Text = isPending
            ? (ramo ? "Ramo letto dall'import: sarà creato al salvataggio della scheda. Le correzioni restano in memoria fino ad allora." : "Gens letta dall'import: sarà creata al salvataggio della scheda. Le correzioni restano in memoria fino ad allora.")
            : count > 0 ? $"Collegata a {count} {(count == 1 ? "personaggio" : "personaggi")}: le modifiche valgono per tutti." : string.Empty;
        UsageText.Visibility = UsageText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        EditorMessage.Text = string.Empty;
        loading = false;
        UpdatePreview();
    }

    private int? SelectedGensId() => SelectedNode is { IsPending: false } node ? node.Gens.Id : null;

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (!loading)
        {
            UpdatePreview();
        }
    }

    // Come si leggerà: «Gens Giulia (Gens Iulia)», «Giulii Cesari (Iulii Caesares)».
    private void UpdatePreview()
    {
        if (PreviewText is null)
        {
            return;
        }

        string text;
        if (editingRamo)
        {
            var gens = current?.IsPending == true ? current.Gens : RamoGensBox.SelectedItem as GensRomana;
            var ramo = FormRamo(gens?.Id ?? 0);
            ramo.Gens = gens;
            text = GensLabels.PathWithLatin(null, ramo);
        }
        else
        {
            text = GensLabels.PathWithLatin(FormGens(), null);
        }

        PreviewText.Text = text.Length > 0 ? $"Si leggerà: {text}" : string.Empty;
    }

    private GensRomana FormGens() => new()
    {
        Id = current?.IsPending == false ? current.Gens.Id : 0,
        Nome = GensNameBox.Text.Trim(),
        Nomen = NomenBox.Text.Trim(),
        Plurale = GensPluralBox.Text.Trim(),
        NomeItaliano = GensItalianBox.Text.Trim(),
        PluraleItaliano = GensItalianPluralBox.Text.Trim(),
        WikidataId = WikidataBox.Text.Trim(),
        Note = NoteBox.Text,
    };

    private RamoGens FormRamo(int gensId) => new()
    {
        Id = current?.IsPending == false ? current.Ramo?.Id ?? 0 : 0,
        GensId = gensId,
        Plurale = RamoPluralBox.Text.Trim(),
        Cognomen = CognomenBox.Text.Trim(),
        NomeItaliano = RamoItalianBox.Text.Trim(),
        WikidataId = WikidataBox.Text.Trim(),
        Note = NoteBox.Text,
    };

    private void NewGens_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNode is { } selected)
        {
            selected.IsSelected = false;
        }

        ShowNode(null, ramo: false);
        GensNameBox.Focus();
    }

    private void NewRamo_Click(object sender, RoutedEventArgs e)
    {
        var gensId = SelectedGensId();
        if (SelectedNode is { } selected)
        {
            selected.IsSelected = false;
        }

        ShowNode(null, ramo: true);
        RamoGensBox.SelectedItem = gentes.FirstOrDefault(g => g.Id == gensId);
        RamoPluralBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (current?.Pending is { } p)
            {
                SavePending(p);
                return;
            }

            if (editingRamo)
            {
                if (RamoGensBox.SelectedItem is not GensRomana gens)
                {
                    EditorMessage.Text = "Scegliere la gens del ramo.";
                    return;
                }

                var id = store.SaveRamo(FormRamo(gens.Id));
                Changed = true;
                Reload(n => n.Ramo?.Id == id && !n.IsPending);
                EditorMessage.Text = "Ramo salvato.";
            }
            else
            {
                var id = store.SaveGens(FormGens());
                Changed = true;
                Reload(n => !n.IsRamo && !n.IsPending && n.Gens.Id == id);
                EditorMessage.Text = "Gens salvata.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            EditorMessage.Text = ex is InvalidOperationException ? ex.Message : "Impossibile salvare.";
        }
    }

    // Correzione di una gens o di un ramo in attesa: resta in memoria e si salva con la scheda.
    private void SavePending(PendingGens p)
    {
        if (editingRamo)
        {
            if (RamoPluralBox.Text.Trim().Length == 0)
            {
                EditorMessage.Text = "Indicare il nome plurale del ramo (es. «Caesares»).";
                return;
            }

            p.Data = p.Data with
            {
                Ramo = new ImportedGensBranch(WikidataBox.Text.Trim(), DynastyNames.Capitalize(RamoPluralBox.Text), DynastyNames.Capitalize(RamoItalianBox.Text), DynastyNames.Capitalize(CognomenBox.Text)),
            };
        }
        else
        {
            if (GensNameBox.Text.Trim().Length == 0)
            {
                EditorMessage.Text = "Indicare il nome latino della gens (es. «Iulia»).";
                return;
            }

            // Tutte le schede in attesa con la stessa gens nuova ricevono la correzione.
            var oldName = current!.Gens.Nome;
            foreach (var other in pending.Where(o => !o.Removed && Comparer.Equals(o.Data.Nome, oldName)))
            {
                other.Data = other.Data with
                {
                    Nome = DynastyNames.Capitalize(GensNameBox.Text),
                    Nomen = DynastyNames.Capitalize(NomenBox.Text),
                    Plurale = DynastyNames.Capitalize(GensPluralBox.Text),
                    NomeItaliano = DynastyNames.Capitalize(GensItalianBox.Text),
                    PluraleItaliano = DynastyNames.Capitalize(GensItalianPluralBox.Text),
                    WikidataId = WikidataBox.Text.Trim(),
                };
            }
        }

        Changed = true;
        var ramo = editingRamo;
        Reload(n => n.Pending == p && n.IsRamo == ramo);
        EditorMessage.Text = "Correzione tenuta in memoria: sarà salvata con la scheda.";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (current is not { } node)
        {
            return;
        }

        if (node.Pending is { } p)
        {
            // Un ramo in attesa si toglie lasciando la gens; una gens in attesa si toglie dalla scheda.
            if (node.IsRamo)
            {
                p.Data = p.Data with { Ramo = null };
            }
            else
            {
                foreach (var other in pending.Where(o => Comparer.Equals(o.Data.Nome, node.Gens.Nome)))
                {
                    other.Removed = true;
                }
            }

            Changed = true;
            Reload(null);
            ShowNode(null, ramo: false);
            EditorMessage.Text = node.IsRamo ? "Ramo tolto: la scheda resta nella gens." : "Gens tolta dalla scheda.";
            return;
        }

        var count = node.IsRamo ? store.CountPeopleUsingRamo(node.Ramo!.Id) : store.CountPeopleUsingGens(node.Gens.Id);
        var what = node.IsRamo ? $"il ramo «{node.Etichetta}»" : $"la {node.Etichetta} e i suoi rami";
        var people = count == 0 ? string.Empty : $" {count} {(count == 1 ? "personaggio resterà" : "personaggi resteranno")} senza.";
        if (MessageBox.Show(this, $"Eliminare {what}?{people}", "Elimina", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (node.IsRamo)
            {
                store.DeleteRamo(node.Ramo!.Id);
            }
            else
            {
                store.DeleteGens(node.Gens.Id);
            }

            Changed = true;
            Reload(null);
            ShowNode(null, ramo: false);
            EditorMessage.Text = node.IsRamo ? "Ramo eliminato." : "Gens eliminata.";
        }
        catch (DbUpdateException)
        {
            EditorMessage.Text = "Impossibile eliminare.";
        }
    }

    // Scelta per la scheda: la gens o il ramo selezionati.
    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNode is not { } node)
        {
            EditorMessage.Text = "Selezionare una gens o un ramo.";
            return;
        }

        Result = node.Pending is { } p
            ? node.IsRamo || p.Data.Ramo is null ? new GensChoice(null, null, p) : new GensChoice(null, null, new PendingGens(p.Data with { Ramo = null }))
            : node.IsRamo ? new GensChoice(node.Gens.Id, node.Ramo!.Id, null) : new GensChoice(node.Gens.Id, null, null);
        DialogResult = true;
    }
}
