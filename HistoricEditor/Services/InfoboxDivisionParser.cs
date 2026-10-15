using System.Text.RegularExpressions;

namespace HistoricEditor;

// Legge dal template {{Divisione amministrativa}} di it.wikipedia le divisioni come appaiono sulla pagina.
internal static class InfoboxDivisionParser
{
    public const string Absent = "no";

    public static InfoboxDivisions? Parse(string wikitext)
    {
        if (string.IsNullOrEmpty(wikitext) || wikitext.IndexOf("{{Divisione amministrativa", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        var grades = new List<string>();
        for (var grade = 1; grade <= 3; grade++)
        {
            var value = ReadField(wikitext, $"Divisione amm grado {grade}");
            if (value is null)
            {
                break;
            }

            grades.Add(value);
        }

        var level = int.TryParse(ReadField(wikitext, "Grado amministrativo"), out var parsed) ? parsed : 0;
        return grades.Count == 0 ? null : new InfoboxDivisions(level, grades);
    }

    private static string? ReadField(string wikitext, string field)
    {
        var match = Regex.Match(wikitext, $@"^\s*\|\s*{Regex.Escape(field)}\s*=(?<v>[^\n]*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? Clean(match.Groups["v"].Value) : null;
    }

    private static string Clean(string value)
    {
        value = Regex.Replace(value, @"<ref[^>]*/>|<ref[^>]*>.*?</ref>|<!--.*?-->", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", "$1");
        value = Regex.Replace(value, @"\{\{[^}]*\}\}|'{2,}", string.Empty);
        return value.Trim();
    }
}

public sealed record InfoboxDivisions(int AdministrativeGrade, List<string> Grades);
