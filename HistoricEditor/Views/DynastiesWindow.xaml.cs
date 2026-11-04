using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class DynastiesWindow : Window
{
    private sealed record DynastyItem(Dinastia? Dinastia, string Percorso);

    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    private readonly PeopleStore store;
    private int currentId;
    private List<DynastyNode> roots = [];
    private List<DynastyItem> items = [];

    private DynastyNode? SelectedNode => DynastyList.SelectedItem as DynastyNode;

    private readonly Dictionary<string, string> renames = new(StringComparer.CurrentCultureIgnoreCase);

    // Nome attuale di una dinastia eventualmente rinominata in questa finestra.
    public string ResolveRename(string name)
    {
        var visited = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        while (renames.TryGetValue(name, out var renamed) && visited.Add(name))
        {
            name = renamed;
        }

        return name;
    }

    // Se initialName è indicato, seleziona la dinastia con quel nome o ne prepara una nuova.
    public DynastiesWindow(PeopleStore store, string? initialName = null)
    {
        InitializeComponent();
        this.store = store;
        NameBox.ItemsSource = store.LoadAll()
            .SelectMany(p => PeopleGrouping.FamilyNames(p.Family))
            .Distinct(Comparer)
            .OrderBy(f => f, Comparer)
            .ToList();
        Reload(null);
        if (!string.IsNullOrWhiteSpace(initialName))
        {
            var node = roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => Comparer.Equals(n.Nome, initialName));
            if (node is not null)
            {
                Select(node.Dinastia.Id);
            }
            else
            {
                NameBox.Text = initialName;
            }
        }
    }

    private void Reload(int? selectId)
    {
        var all = store.GetDynasties();
        var byId = all.ToDictionary(d => d.Id);
        items = all
            .Select(d => new DynastyItem(d, DescribePath(d, byId)))
            .OrderBy(i => i.Percorso, Comparer)
            .ToList();

        // Mantiene aperti i rami già espansi.
        var expanded = roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.IsExpanded).Select(n => n.Dinastia.Id).ToHashSet();
        roots = DynastyTree.Build(all);
        foreach (var node in roots.SelectMany(r => r.SelfAndDescendants()).Where(n => expanded.Contains(n.Dinastia.Id)))
        {
            node.IsExpanded = true;
        }

        DynastyList.ItemsSource = roots;
        if (selectId is null || !Select(selectId.Value))
        {
            ShowDynasty(null);
        }
    }

    // Seleziona la dinastia espandendo i suoi superiori.
    private bool Select(int id)
    {
        foreach (var root in roots)
        {
            if (Expand(root, id))
            {
                return true;
            }
        }

        return false;

        static bool Expand(DynastyNode node, int id)
        {
            if (node.Dinastia.Id == id)
            {
                node.IsSelected = true;
                return true;
            }

            if (node.Rami.Any(r => Expand(r, id)))
            {
                node.IsExpanded = true;
                return true;
            }

            return false;
        }
    }

    // Percorso completo, es. «Asburgo › Asburgo di Spagna».
    private static string DescribePath(Dinastia dynasty, IReadOnlyDictionary<int, Dinastia> byId)
    {
        var names = new List<string> { dynasty.Nome };
        var visited = new HashSet<int> { dynasty.Id };
        var current = dynasty;
        while (current.ParentId is int p && visited.Add(p) && byId.TryGetValue(p, out var parent))
        {
            names.Insert(0, parent.Nome);
            current = parent;
        }

        return string.Join(" › ", names);
    }

    private void DynastyList_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => ShowDynasty(SelectedNode?.Dinastia);

    private void ShowDynasty(Dinastia? dynasty)
    {
        currentId = dynasty?.Id ?? 0;
        NameBox.Text = dynasty?.Nome ?? string.Empty;
        WikidataBox.Text = dynasty?.WikidataId ?? string.Empty;
        NoteBox.Text = dynasty?.Note ?? string.Empty;
        DeleteButton.IsEnabled = dynasty is not null;
        AttachmentsButton.IsEnabled = dynasty is not null;

        // Una dinastia non può essere ramo di sé stessa: la si esclude dall'elenco dei possibili superiori.
        var parents = new List<DynastyItem> { new(null, "(nessuna: dinastia principale)") };
        parents.AddRange(items.Where(i => i.Dinastia!.Id != currentId));
        ParentBox.ItemsSource = parents;
        ParentBox.SelectedItem = parents.FirstOrDefault(i => i.Dinastia?.Id == dynasty?.ParentId) ?? parents[0];
        EditorMessage.Text = string.Empty;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNode is { } selected)
        {
            selected.IsSelected = false;
        }

        ShowDynasty(null);
        NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var data = new Dinastia
        {
            Id = currentId,
            Nome = NameBox.Text,
            ParentId = (ParentBox.SelectedItem as DynastyItem)?.Dinastia?.Id,
            WikidataId = WikidataBox.Text,
            Note = NoteBox.Text,
        };

        try
        {
            var oldName = currentId == 0 ? null : SelectedNode?.Dinastia.Nome;
            var id = store.SaveDynasty(data);
            if (oldName is not null && !string.Equals(oldName, data.Nome.Trim(), StringComparison.Ordinal))
            {
                renames[oldName] = data.Nome.Trim();
            }

            Reload(id);
            EditorMessage.Text = "Dinastia salvata.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            EditorMessage.Text = ex is InvalidOperationException ? ex.Message : "Impossibile salvare la dinastia.";
        }
    }

    private void Attachments_Click(object sender, RoutedEventArgs e)
    {
        if (currentId != 0)
        {
            new AttachmentsWindow(store, AmbitiAllegato.Dinastia, currentId, NameBox.Text.Trim()) { Owner = this }.ShowDialog();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (currentId == 0)
        {
            return;
        }

        var answer = MessageBox.Show(this, $"Eliminare la dinastia «{NameBox.Text}»? I suoi rami passeranno alla dinastia superiore; le schede delle persone non vengono modificate.", "Elimina dinastia", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            store.DeleteDynasty(currentId);
            Reload(null);
            EditorMessage.Text = "Dinastia eliminata.";
        }
        catch (DbUpdateException)
        {
            EditorMessage.Text = "Impossibile eliminare la dinastia.";
        }
    }
}
