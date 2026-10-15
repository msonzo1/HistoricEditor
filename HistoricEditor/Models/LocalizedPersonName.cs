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
}
