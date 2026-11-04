using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace HistoricEditor;

// Nodo dell'albero delle dinastie: la dinastia e i suoi rami.
public sealed class DynastyNode(Dinastia dinastia, IReadOnlyList<DynastyNode> rami) : INotifyPropertyChanged
{
    private bool isExpanded;
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Dinastia Dinastia { get; } = dinastia;

    public string Nome => Dinastia.Nome;

    public IReadOnlyList<DynastyNode> Rami { get; } = rami;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded != value)
            {
                isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected != value)
            {
                isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public IEnumerable<DynastyNode> SelfAndDescendants()
        => Rami.SelectMany(r => r.SelfAndDescendants()).Prepend(this);

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class DynastyTree
{
    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    // Costruisce l'albero ordinando per nome ogni livello; le dinastie con superiore mancante o ciclico diventano radici.
    public static List<DynastyNode> Build(IEnumerable<Dinastia> dynasties)
    {
        var all = dynasties.ToList();
        var ids = all.Select(d => d.Id).ToHashSet();
        var childrenOf = all
            .Where(d => d.ParentId is int p && p != d.Id && ids.Contains(p))
            .ToLookup(d => d.ParentId!.Value);
        var placed = new HashSet<int>();

        DynastyNode Create(Dinastia d)
        {
            placed.Add(d.Id);
            var rami = childrenOf[d.Id]
                .Where(c => !placed.Contains(c.Id))
                .OrderBy(c => c.Nome, Comparer)
                .Select(Create)
                .ToList();
            return new DynastyNode(d, rami);
        }

        var roots = all
            .Where(d => d.ParentId is not int p || p == d.Id || !ids.Contains(p))
            .OrderBy(d => d.Nome, Comparer)
            .Select(Create)
            .ToList();

        // Dinastie rimaste fuori per un ciclo nei superiori.
        foreach (var orphan in all.Where(d => !placed.Contains(d.Id)).OrderBy(d => d.Nome, Comparer).ToList())
        {
            if (!placed.Contains(orphan.Id))
            {
                roots.Add(Create(orphan));
            }
        }

        return [.. roots.OrderBy(r => r.Nome, Comparer)];
    }
}
