using System.Globalization;
using System.Text;

namespace HistoricEditor;

// Nome usato da un luogo in un periodo (es. «Costantinopoli», 330–1922). Gli anni negativi sono a.C.
public sealed class NomeStorico
{
    public string Nome { get; set; } = string.Empty;
    public int? Dal { get; set; }
    public int? Al { get; set; }
    public string Lingua { get; set; } = string.Empty;
    public string Fonte { get; set; } = string.Empty;

    public NomeStorico Clone() => new() { Nome = Nome, Dal = Dal, Al = Al, Lingua = Lingua, Fonte = Fonte };
}

public static class HistoricalPlaceNames
{
    // Nome in uso nell'anno indicato; null se non noto o uguale al nome attuale.
    public static string? NameAt(string currentName, IEnumerable<NomeStorico> names, int? year)
    {
        if (year is not int y)
        {
            return null;
        }

        var match = names
            .Where(n => n.Nome.Trim().Length > 0 && (n.Dal is not null || n.Al is not null))
            .Where(n => (n.Dal is null || n.Dal <= y) && (n.Al is null || y <= n.Al))
            // Negli anni di passaggio (es. 330) vale il nome che inizia allora.
            .OrderByDescending(n => n.Dal ?? int.MinValue)
            .ThenBy(n => n.Al ?? int.MaxValue)
            .FirstOrDefault();
        return match is null || SameName(match.Nome, currentName) ? null : match.Nome.Trim();
    }

    // «Costantinopoli (oggi Istanbul)» se nell'anno il luogo aveva un altro nome, altrimenti il nome attuale.
    public static string Describe(string currentName, IEnumerable<NomeStorico> names, int? year)
        => NameAt(currentName, names, year) is { } old ? $"{old} (oggi {currentName})" : currentName;

    public static string FormatPeriod(NomeStorico name) => (name.Dal, name.Al) switch
    {
        (int dal, int al) => $"{FormatYear(dal)} – {FormatYear(al)}",
        (int dal, null) => $"dal {FormatYear(dal)}",
        (null, int al) => $"fino al {FormatYear(al)}",
        _ => string.Empty,
    };

    public static string FormatYear(int year) => year < 0
        ? $"{(-year).ToString(CultureInfo.InvariantCulture)} a.C."
        : year.ToString(CultureInfo.InvariantCulture);

    // Accetta «330», «-667», «667 a.C.», «330 d.C.»; vuoto = anno non noto.
    public static bool TryParseYear(string? text, out int? year)
    {
        year = null;
        var value = (text ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace(".", string.Empty);
        if (value.Length == 0)
        {
            return true;
        }

        var sign = 1;
        if (value.EndsWith("ac", StringComparison.Ordinal))
        {
            sign = -1;
            value = value[..^2];
        }
        else if (value.EndsWith("dc", StringComparison.Ordinal))
        {
            value = value[..^2];
        }

        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            || parsed == 0 || (sign < 0 && parsed < 0) || Math.Abs(parsed) > 9999)
        {
            return false;
        }

        year = sign * parsed;
        return true;
    }

    // Unisce i nomi importati a quelli già presenti: per lo stesso nome si allarga il periodo, i nomi inseriti a mano restano.
    public static List<NomeStorico> Merge(IEnumerable<NomeStorico> existing, IEnumerable<NomeStorico> incoming, string currentName)
    {
        var result = existing.Select(n => n.Clone()).ToList();
        foreach (var name in incoming.Where(n => n.Nome.Trim().Length > 0 && (n.Dal is not null || n.Al is not null)))
        {
            if (SameName(name.Nome, currentName) && name.Al is null)
            {
                continue;
            }

            var same = result.FirstOrDefault(n => SameName(n.Nome, name.Nome));
            if (same is null)
            {
                result.Add(name.Clone());
                continue;
            }

            same.Dal = Min(same.Dal, name.Dal);
            same.Al = Max(same.Al, name.Al);
            if (same.Fonte.Length == 0)
            {
                same.Fonte = name.Fonte;
            }
        }

        return [.. result.OrderBy(n => n.Dal ?? int.MinValue).ThenBy(n => n.Al ?? int.MaxValue)];
    }

    // Uguali a meno di maiuscole e segni diacritici (Istanbul = İstanbul).
    public static bool SameName(string a, string b)
        => string.Equals(Fold(a), Fold(b), StringComparison.Ordinal);

    private static string Fold(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Replace('ı', 'i');
    }

    private static int? Min(int? a, int? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private static int? Max(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
