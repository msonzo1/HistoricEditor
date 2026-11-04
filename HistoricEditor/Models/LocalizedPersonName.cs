namespace HistoricEditor;

// Nome, suffisso, soprannome e titolo principale letti da Wikidata in una delle lingue d'importazione.
public sealed class LocalizedPersonName
{
    public string Lingua { get; set; } = string.Empty;
    public string LinguaNome { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Suffisso { get; set; } = string.Empty;
    public string Soprannome { get; set; } = string.Empty;
    public string TitoloPrincipale { get; set; } = string.Empty;

    public LocalizedPersonName Clone() => (LocalizedPersonName)MemberwiseClone();

    public bool IsEmpty() => new[] { Nome, Suffisso, Soprannome, TitoloPrincipale }.All(string.IsNullOrWhiteSpace);

    // Lingue aggiungibili a mano nella scheda (le stesse lette dall'import, italiano escluso perché è la scheda stessa).
    public static readonly IReadOnlyList<(string Code, string Name)> EditableLanguages =
    [
        ("en", "inglese"),
        ("de", "tedesco"),
        ("fr", "francese"),
        ("es", "spagnolo"),
        ("nl", "olandese"),
    ];

    public static IReadOnlyList<(string Code, string Name)> MissingLanguages(IEnumerable<LocalizedPersonName> existing)
    {
        var codes = existing.Select(n => n.Lingua).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. EditableLanguages.Where(l => !codes.Contains(l.Code))];
    }

    // Valori ripuliti per il salvataggio: spazi tolti e lingue rimaste vuote scartate.
    public static List<LocalizedPersonName> Normalize(IEnumerable<LocalizedPersonName> names) =>
    [
        .. names
            .Select(n => new LocalizedPersonName
            {
                Lingua = n.Lingua.Trim(),
                LinguaNome = n.LinguaNome.Trim(),
                Nome = n.Nome.Trim(),
                Suffisso = n.Suffisso.Trim(),
                Soprannome = n.Soprannome.Trim(),
                TitoloPrincipale = n.TitoloPrincipale.Trim(),
            })
            .Where(n => !n.IsEmpty()),
    ];
}
