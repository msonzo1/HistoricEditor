using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace HistoricEditor;

// Nodo dell'albero delle persone: una famiglia (gruppo) o una singola persona.
public abstract class PeopleTreeNode : INotifyPropertyChanged
{
    private bool isExpanded;
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public abstract string AccessibleName { get; }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PersonEntry(HistoricalPerson person) : PeopleTreeNode
{
    public HistoricalPerson Person { get; } = person;

    public string Nome => Person.DisplayName;

    public string Date => PeopleGrouping.DescribeDates(Person.Birth, Person.Death);

    public string Sesso => PeopleGrouping.DescribeSex(Person.Sesso);

    public string Nascita => Person.Birth;

    public string LuogoNascita => Person.BirthPlace;

    public string Morte => Person.Death;

    public string LuogoMorte => Person.DeathPlace;

    public override string AccessibleName => $"{Nome}, {Date}";
}

public sealed class PersonGroup(string famiglia, IReadOnlyList<PersonEntry> persone, IReadOnlyList<PersonGroup>? rami = null) : PeopleTreeNode
{
    public string Famiglia { get; } = famiglia;

    public IReadOnlyList<PersonEntry> Persone { get; } = persone;

    // Rami (sottodinastie) mostrati prima delle persone della dinastia.
    public IReadOnlyList<PersonGroup> Rami { get; } = rami ?? [];

    public IReadOnlyList<PeopleTreeNode> Elementi => [.. Rami, .. Persone];

    // Persone della dinastia e di tutti i suoi rami.
    public int Totale => Persone.Count + Rami.Sum(r => r.Totale);

    public string Conteggio => Totale == 1 ? "1 PERSONA" : $"{Totale} PERSONE";

    public override string AccessibleName
    {
        get
        {
            var name = Famiglia == PeopleGrouping.NoFamily ? Famiglia : $"Famiglia {Famiglia}";
            var rami = Rami.Count == 0 ? string.Empty : $", {Rami.Count} {(Rami.Count == 1 ? "ramo" : "rami")}";
            return $"{name}, {Totale} {(Totale == 1 ? "persona" : "persone")}{rami}";
        }
    }

    public IEnumerable<PersonGroup> SelfAndDescendants()
        => Rami.SelectMany(r => r.SelfAndDescendants()).Prepend(this);
}

public static class PeopleGrouping
{
    public const string NoFamily = "Famiglia non indicata";

    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    // Raggruppa per famiglia (senza distinguere maiuscole); le famiglie registrate come rami
    // di una dinastia vengono annidate sotto di essa. Le persone senza famiglia vanno in fondo.
    public static List<PersonGroup> Build(IEnumerable<HistoricalPerson> people, IEnumerable<Dinastia>? dynasties = null)
    {
        var byFamily = people
            .GroupBy(p => (p.Family ?? string.Empty).Trim(), Comparer)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.DisplayName, Comparer).Select(p => new PersonEntry(p)).ToList(), Comparer);

        var dynastyList = (dynasties ?? []).Where(d => !string.IsNullOrWhiteSpace(d.Nome)).ToList();
        var byId = dynastyList.ToDictionary(d => d.Id);
        var childrenOf = dynastyList.ToLookup(d => d.ParentId is int p && byId.ContainsKey(p) && p != d.Id ? p : (int?)null);
        var used = new HashSet<string>(Comparer);

        PersonGroup? BuildDynasty(Dinastia dynasty, HashSet<int> path)
        {
            if (!path.Add(dynasty.Id))
            {
                return null;
            }

            var name = dynasty.Nome.Trim();
            var rami = childrenOf[dynasty.Id]
                .Select(child => BuildDynasty(child, path))
                .OfType<PersonGroup>()
                .OrderBy(g => g.Famiglia, Comparer)
                .ToList();
            path.Remove(dynasty.Id);

            List<PersonEntry> members = byFamily.TryGetValue(name, out var found) && used.Add(name) ? found : [];
            return members.Count == 0 && rami.Count == 0 ? null : new PersonGroup(name, members, rami);
        }

        var roots = childrenOf[null]
            .Select(d => BuildDynasty(d, []))
            .OfType<PersonGroup>()
            .ToList();

        roots.AddRange(byFamily
            .Where(kv => kv.Key.Length > 0 && !used.Contains(kv.Key))
            .Select(kv => new PersonGroup(kv.Key, kv.Value)));

        var result = roots.OrderBy(g => g.Famiglia, Comparer).ToList();
        if (byFamily.TryGetValue(string.Empty, out var noFamily))
        {
            result.Add(new PersonGroup(NoFamily, noFamily));
        }

        return result;
    }

    public static string DescribeDates(string? birth, string? death)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(birth))
        {
            parts.Add($"n. {birth.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(death))
        {
            parts.Add($"m. {death.Trim()}");
        }

        return parts.Count == 0 ? "Date non indicate" : string.Join(" · ", parts);
    }

    public static string DescribeSex(string? sex) => (sex ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "M" => "Maschio",
        "F" => "Femmina",
        _ => string.Empty,
    };
}
