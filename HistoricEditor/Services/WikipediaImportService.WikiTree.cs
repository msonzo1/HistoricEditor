using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Profilo pubblico WikiTree (persona). FatherId e MotherId sono gli Id numerici WikiTree dei genitori.
public sealed record WikiTreeProfile(
    int Id,
    string Key,
    string FirstName,
    string MiddleName,
    string LastNameAtBirth,
    string Suffix,
    string Gender,
    string BirthDate,
    string BirthStatus,
    string DeathDate,
    string DeathStatus,
    string BirthLocation,
    string DeathLocation,
    int? FatherId,
    int? MotherId)
{
    public string Url => WikiTreeLinks.ProfileUrl(Key);

    public string GivenName => string.Join(' ', new[] { FirstName, MiddleName }.Where(p => p.Length > 0));

    public string DisplayName => HistoricalPerson.ComposeName(GivenName.Length > 0 ? GivenName : Key, LastNameAtBirth, Suffix);
}

// Famiglia di un profilo: genitori, coniugi, figli e gli altri genitori dei figli che non risultano coniugi.
public sealed record WikiTreeFamily(
    WikiTreeProfile Person,
    IReadOnlyList<WikiTreeProfile> Parents,
    IReadOnlyList<WikiTreeProfile> Spouses,
    IReadOnlyList<WikiTreeProfile> Children,
    IReadOnlyList<WikiTreeProfile> OtherParents)
{
    public IEnumerable<WikiTreeProfile> Relatives => Parents.Concat(Spouses).Concat(Children).Concat(OtherParents);
}

public static class WikiTreeLinks
{
    public static string ProfileUrl(string key) => string.IsNullOrWhiteSpace(key)
        ? string.Empty
        : $"https://www.wikitree.com/wiki/{Uri.EscapeDataString(key.Trim().Replace(' ', '_'))}";

    // Identificativo WikiTree: cognome, trattino e numero (es. «Habsburg-105»); accetta anche l'URL del profilo.
    public static string Normalize(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        var match = Regex.Match(value, @"wikitree\.com/wiki/(?<key>[^?#/]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            value = Uri.UnescapeDataString(match.Groups["key"].Value);
        }

        return value.Replace(' ', '_');
    }

    public static bool IsValid(string? key) => Regex.IsMatch(key ?? string.Empty, @"^[^\s\-][^\s]*-\d+$");
}

public sealed partial class WikipediaImportService
{
    private const string WikiTreeApi = "https://api.wikitree.com/api.php";
    private const string WikidataSparql = "https://query.wikidata.org/sparql";
    private const string WikiTreeFields = "Id,Name,FirstName,MiddleName,LastNameAtBirth,Suffix,Gender,BirthDate,DeathDate,BirthLocation,DeathLocation,Father,Mother,DataStatus";

    // P2949 «WikiTree person ID»: valore preferito, altrimenti il primo non deprecato.
    internal static string ReadWikiTreeId(JsonElement claims)
    {
        var statements = claims.GetPropertyOrDefault("P2949");
        if (statements.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var values = statements.EnumerateArray()
            .Where(s => GetString(s, "rank") != "deprecated")
            .OrderBy(s => GetString(s, "rank") == "preferred" ? 0 : 1)
            .Select(s => s.GetPropertyOrDefault("mainsnak").GetPropertyOrDefault("datavalue").GetPropertyOrDefault("value"))
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!.Trim());
        return values.FirstOrDefault(v => v.Length > 0) ?? string.Empty;
    }

    // Legge da WikiTree genitori, coniugi e figli del profilo; poi gli altri genitori dei figli.
    public async Task<WikiTreeFamily?> ReadWikiTreeFamilyAsync(string wikiTreeId, CancellationToken cancellationToken = default)
    {
        var key = WikiTreeLinks.Normalize(wikiTreeId);
        if (!WikiTreeLinks.IsValid(key))
        {
            return null;
        }

        var url = $"{WikiTreeApi}?action=getRelatives&keys={Uri.EscapeDataString(key)}&getParents=1&getSpouses=1&getChildren=1"
            + $"&fields={WikiTreeFields}&appId=AlberiStorici";
        using var document = await GetJsonAsync(url, cancellationToken);
        var family = ParseWikiTreeRelatives(document.RootElement);
        if (family is null)
        {
            return null;
        }

        var known = family.Relatives.Select(p => p.Id).Append(family.Person.Id).ToHashSet();
        var otherIds = family.Children
            .SelectMany(c => new[] { c.FatherId, c.MotherId })
            .OfType<int>()
            .Where(id => !known.Contains(id))
            .Distinct()
            .ToList();
        if (otherIds.Count == 0)
        {
            return family;
        }

        var peopleUrl = $"{WikiTreeApi}?action=getPeople&keys={string.Join(',', otherIds)}&fields={WikiTreeFields}&appId=AlberiStorici";
        using var people = await GetJsonAsync(peopleUrl, cancellationToken);
        return family with { OtherParents = ParseWikiTreePeople(people.RootElement) };
    }

    // Wikidata collega i profili WikiTree con P2949: restituisce identificativo WikiTree → Q-id.
    public async Task<Dictionary<string, string>> FindWikidataIdsForWikiTreeAsync(IEnumerable<string> wikiTreeIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keys = wikiTreeIds.Select(WikiTreeLinks.Normalize).Where(WikiTreeLinks.IsValid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var batch in keys.Chunk(50))
        {
            var values = string.Join(' ', batch.Select(k => $"\"{k.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""));
            var query = $"SELECT ?item ?wt WHERE {{ VALUES ?wt {{ {values} }} ?item wdt:P2949 ?wt . }}";
            using var document = await GetJsonAsync($"{WikidataSparql}?format=json&query={Uri.EscapeDataString(query)}", cancellationToken);
            foreach (var (key, qid) in ParseWikiTreeSparql(document.RootElement))
            {
                result.TryAdd(key, qid);
            }
        }

        return result;
    }

    internal static IEnumerable<(string Key, string Qid)> ParseWikiTreeSparql(JsonElement root)
    {
        var bindings = root.GetPropertyOrDefault("results").GetPropertyOrDefault("bindings");
        if (bindings.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var row in bindings.EnumerateArray())
        {
            var item = GetString(row.GetPropertyOrDefault("item"), "value");
            var key = GetString(row.GetPropertyOrDefault("wt"), "value");
            var qid = item[(item.LastIndexOf('/') + 1)..];
            if (key.Length > 0 && IsEntityId(qid))
            {
                yield return (key, qid);
            }
        }
    }

    internal static WikiTreeFamily? ParseWikiTreeRelatives(JsonElement root)
    {
        var item = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
            ? root[0].GetPropertyOrDefault("items")
            : root.GetPropertyOrDefault("items");
        if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() == 0)
        {
            return null;
        }

        var person = item[0].GetPropertyOrDefault("person");
        if (ParseWikiTreeProfile(person) is not { } profile)
        {
            return null;
        }

        return new WikiTreeFamily(
            profile,
            ParseProfileMap(person.GetPropertyOrDefault("Parents")),
            ParseProfileMap(person.GetPropertyOrDefault("Spouses")),
            ParseProfileMap(person.GetPropertyOrDefault("Children")),
            []);
    }

    // getPeople: [{ "people": { "Id": {...} } }] oppure { "people": ... }.
    internal static IReadOnlyList<WikiTreeProfile> ParseWikiTreePeople(JsonElement root)
    {
        var container = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0] : root;
        return ParseProfileMap(container.GetPropertyOrDefault("people"));
    }

    private static List<WikiTreeProfile> ParseProfileMap(JsonElement map)
    {
        var values = map.ValueKind switch
        {
            JsonValueKind.Object => map.EnumerateObject().Select(p => p.Value),
            JsonValueKind.Array => map.EnumerateArray(),
            _ => [],
        };
        return [.. values.Select(ParseWikiTreeProfile).OfType<WikiTreeProfile>()];
    }

    internal static WikiTreeProfile? ParseWikiTreeProfile(JsonElement person)
    {
        var key = GetString(person, "Name").Trim();
        if (person.ValueKind != JsonValueKind.Object || key.Length == 0 || ReadInt(person, "Id") is not int id)
        {
            return null;
        }

        var status = person.GetPropertyOrDefault("DataStatus");
        return new WikiTreeProfile(
            id,
            key,
            GetString(person, "FirstName").Trim(),
            GetString(person, "MiddleName").Trim(),
            GetString(person, "LastNameAtBirth").Trim(),
            GetString(person, "Suffix").Trim(),
            GetString(person, "Gender").Trim(),
            GetString(person, "BirthDate").Trim(),
            GetString(status, "BirthDate").Trim(),
            GetString(person, "DeathDate").Trim(),
            GetString(status, "DeathDate").Trim(),
            GetString(person, "BirthLocation").Trim(),
            GetString(person, "DeathLocation").Trim(),
            ReadInt(person, "Father") is int f && f > 0 ? f : null,
            ReadInt(person, "Mother") is int m && m > 0 ? m : null);
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        var value = element.GetPropertyOrDefault(name);
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };
    }

    // Date WikiTree «AAAA-MM-GG» (zeri per le parti ignote) nel formato della scheda, con «circa», «prima del», «dopo il».
    internal static string FormatWikiTreeDate(string date, string status)
    {
        var match = Regex.Match(date ?? string.Empty, @"^(?<y>\d{1,4})-(?<m>\d{2})-(?<d>\d{2})$");
        if (!match.Success || !int.TryParse(match.Groups["y"].Value, out var year) || year == 0)
        {
            return string.Empty;
        }

        var month = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        if (month is >= 1 and <= 12)
        {
            if (day is >= 1 and <= 31)
            {
                text.Append(CultureInfo.InvariantCulture, $"{day:00}/");
            }

            text.Append(CultureInfo.InvariantCulture, $"{month:00}/");
        }

        text.Append(FormatYear(year, beforeChrist: false));
        var qualifier = status switch
        {
            "guess" => DataEvento.Circa,
            "before" => DataEvento.Prima,
            "after" => DataEvento.Dopo,
            _ => DataEvento.Esatta,
        };
        return DataEvento.Format(qualifier, text.ToString(), null);
    }
}
