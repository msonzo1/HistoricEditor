namespace HistoricEditor;

// Compone, per ogni lingua d'importazione, nome/suffisso/soprannome/titolo principale a partire dai dati Wikidata.
internal static class LocalizedNameBuilder
{
    public static List<LocalizedPersonName> Build(
        IEnumerable<(string Code, string Name)> languages,
        IReadOnlyDictionary<string, string> labels,
        IReadOnlyList<(string Text, string Language)> nicknames,
        IReadOnlyDictionary<string, string> mainTitleLabels)
    {
        var result = new List<LocalizedPersonName>();
        foreach (var (code, name) in languages)
        {
            var languageNicknames = nicknames.Where(n => n.Language == code).Select(n => n.Text).ToList();
            var allNicknames = languageNicknames.Concat(nicknames.Select(n => n.Text)).Distinct().ToList();
            var label = labels.GetValueOrDefault(code, string.Empty);
            var parsed = label.Length > 0 ? PersonNameParser.Split(label, allNicknames) : (Name: string.Empty, Suffix: string.Empty, Nickname: string.Empty);

            var entry = new LocalizedPersonName
            {
                Lingua = code,
                LinguaNome = name,
                Nome = parsed.Name,
                Suffisso = parsed.Suffix,
                Soprannome = parsed.Nickname.Length > 0 ? parsed.Nickname : languageNicknames.FirstOrDefault() ?? string.Empty,
                TitoloPrincipale = mainTitleLabels.GetValueOrDefault(code, string.Empty),
            };
            if (!entry.IsEmpty())
            {
                result.Add(entry);
            }
        }

        return result;
    }
}
