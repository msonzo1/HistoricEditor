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

    // Persone distinte della dinastia e di tutti i suoi rami (chi ha più dinastie conta una volta).
    public int Totale => SelfAndDescendants().SelectMany(g => g.Persone).Select(p => p.Person.Id).Distinct().Count();

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

    public static StringComparer FamilyComparer => Comparer;

    // Nomi delle famiglie indicate in una scheda: più dinastie sono separate da «;».
    public static List<string> FamilyNames(string? family)
        => [.. (family ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(Comparer)];

    public static string JoinFamilyNames(IEnumerable<string> names) => string.Join("; ", names);

    // Raggruppa per dinastia: le persone collegate (links) vanno sotto ciascuna delle loro dinastie,
    // quelle non collegate secondo i nomi scritti nel campo «Famiglia» (senza distinguere maiuscole).
    // Le famiglie registrate come rami di una dinastia vengono annidate sotto di essa; chi è sia nella dinastia sia in un suo ramo
    // compare solo nel ramo. Le persone senza famiglia vanno in fondo.
    public static List<PersonGroup> Build(IEnumerable<HistoricalPerson> people, IEnumerable<Dinastia>? dynasties = null, IEnumerable<PersonaDinastia>? links = null)
    {
        var dynastyList = (dynasties ?? []).Where(d => !string.IsNullOrWhiteSpace(d.Nome)).ToList();
        var byId = dynastyList.ToDictionary(d => d.Id);
        var linksByPerson = (links ?? []).Where(l => byId.ContainsKey(l.DinastiaId)).ToLookup(l => l.PersonaId, l => l.DinastiaId);

        var byDynasty = new Dictionary<int, List<HistoricalPerson>>();
        var byFamily = new Dictionary<string, List<HistoricalPerson>>(Comparer);
        foreach (var person in people)
        {
            var linked = linksByPerson[person.Id].Distinct().ToList();
            if (linked.Count > 0)
            {
                foreach (var id in linked)
                {
                    Members(byDynasty, id).Add(person);
                }

                // Le famiglie scritte nella scheda ma non (ancora) registrate restano raggruppate per nome.
                var linkedNames = new HashSet<string>(linked.Select(id => byId[id].Nome.Trim()), Comparer);
                foreach (var name in FamilyNames(person.Family).Where(n => !linkedNames.Contains(n)))
                {
                    Members(byFamily, name).Add(person);
                }

                continue;
            }

            var names = GroupKeys(person);
            foreach (var name in names)
            {
                Members(byFamily, name).Add(person);
            }
        }

        var childrenOf = dynastyList.ToLookup(d => d.ParentId is int p && byId.ContainsKey(p) && p != d.Id ? p : (int?)null);
        var used = new HashSet<string>(Comparer);

        List<PersonEntry> Entries(IEnumerable<HistoricalPerson> list)
            => [.. list.DistinctBy(p => p.Id).OrderBy(p => p.DisplayName, Comparer).Select(p => new PersonEntry(p))];

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

            var members = new List<HistoricalPerson>();
            if (byDynasty.TryGetValue(dynasty.Id, out var linked))
            {
                members.AddRange(linked);
            }

            if (used.Add(name) && byFamily.TryGetValue(name, out var named))
            {
                members.AddRange(named);
            }

            // Chi appartiene anche a un ramo compare solo lì: altrimenti, aprendo la dinastia, sembrerebbe un doppione.
            var inBranches = rami.SelectMany(r => r.SelfAndDescendants()).SelectMany(g => g.Persone).Select(p => p.Person.Id).ToHashSet();
            members.RemoveAll(p => inBranches.Contains(p.Id));

            return members.Count == 0 && rami.Count == 0 ? null : new PersonGroup(name, Entries(members), rami);
        }

        var roots = childrenOf[null]
            .Select(d => BuildDynasty(d, []))
            .OfType<PersonGroup>()
            .ToList();

        roots.AddRange(byFamily
            .Where(kv => kv.Key.Length > 0 && !used.Contains(kv.Key))
            .Select(kv => new PersonGroup(kv.Key, Entries(kv.Value))));

        var result = roots.OrderBy(g => g.Famiglia, Comparer).ToList();
        if (byFamily.TryGetValue(string.Empty, out var noFamily))
        {
            result.Add(new PersonGroup(NoFamily, Entries(noFamily)));
        }

        return result;
    }

    private static List<HistoricalPerson> Members<TKey>(Dictionary<TKey, List<HistoricalPerson>> groups, TKey key)
        where TKey : notnull
    {
        if (!groups.TryGetValue(key, out var list))
        {
            list = [];
            groups[key] = list;
        }

        return list;
    }

    // Senza famiglia (dinastia) i personaggi moderni si raggruppano per cognome.
    private static List<string> GroupKeys(HistoricalPerson person)
    {
        var names = FamilyNames(person.Family);
        return names.Count > 0 ? names : [(person.Surname ?? string.Empty).Trim()];
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
