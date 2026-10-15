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
            .Select(p => (p.Family ?? string.Empty).Trim())
            .Where(f => f.Length > 0)
            .Distinct(Comparer)
            .OrderBy(f => f, Comparer)
            .ToList();
        Reload(null);
        if (!string.IsNullOrWhiteSpace(initialName))
        {
            var item = ((IEnumerable<DynastyItem>)DynastyList.ItemsSource).FirstOrDefault(i => Comparer.Equals(i.Dinastia!.Nome, initialName));
            if (item is not null)
            {
                DynastyList.SelectedItem = item;
                DynastyList.ScrollIntoView(item);
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
        var items = all
            .Select(d => new DynastyItem(d, DescribePath(d, byId)))
            .OrderBy(i => i.Percorso, Comparer)
            .ToList();
        DynastyList.ItemsSource = items;
        DynastyList.SelectedItem = items.FirstOrDefault(i => i.Dinastia!.Id == selectId);
        if (DynastyList.SelectedItem is null)
        {
            ShowDynasty(null);
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

    private void DynastyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ShowDynasty((DynastyList.SelectedItem as DynastyItem)?.Dinastia);

    private void ShowDynasty(Dinastia? dynasty)
    {
        currentId = dynasty?.Id ?? 0;
        NameBox.Text = dynasty?.Nome ?? string.Empty;
        WikidataBox.Text = dynasty?.WikidataId ?? string.Empty;
        NoteBox.Text = dynasty?.Note ?? string.Empty;
        DeleteButton.IsEnabled = dynasty is not null;

        // Una dinastia non può essere ramo di sé stessa: la si esclude dall'elenco dei possibili superiori.
        var parents = new List<DynastyItem> { new(null, "(nessuna: dinastia principale)") };
        parents.AddRange(((IEnumerable<DynastyItem>)DynastyList.ItemsSource).Where(i => i.Dinastia!.Id != currentId));
        ParentBox.ItemsSource = parents;
        ParentBox.SelectedItem = parents.FirstOrDefault(i => i.Dinastia?.Id == dynasty?.ParentId) ?? parents[0];
        EditorMessage.Text = string.Empty;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        DynastyList.SelectedItem = null;
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
            var oldName = (DynastyList.SelectedItem as DynastyItem)?.Dinastia?.Nome;
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
