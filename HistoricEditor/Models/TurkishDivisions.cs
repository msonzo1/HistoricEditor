namespace HistoricEditor;

// Province della Turchia e regioni geografiche (2026-11-16, da Germanico). Le 7 regioni (Mar Nero, Marmara, Egeo, Mar
// Mediterraneo, Anatolia Centrale, Orientale e Sud Orientale) non sono divisioni amministrative: su Wikidata le province
// non sono «situate» in una regione ma ne sono, a volte, «parte di» (13 su 81), quindi la regione si legge da qui. Elenco
// letto dalle infobox delle voci italiane delle province (Divisione amm grado 1), con i nomi usati lì; i codici Wikidata
// sono quelli delle voci. Le province hanno il nome breve dell'infobox («Hatay», non «provincia di Hatay»).
public static class TurkishDivisions
{
    public const string Country = "Turchia";

    public sealed record Province(string Nome, string WikidataId, string Regione);

    public static readonly IReadOnlyList<Province> Provinces =
    [
        // Anatolia Centrale
        new("Aksaray", "Q83073", "Anatolia Centrale"),
        new("Ankara", "Q2297724", "Anatolia Centrale"),
        new("Eskişehir", "Q483053", "Anatolia Centrale"),
        new("Karaman", "Q482975", "Anatolia Centrale"),
        new("Kayseri", "Q483472", "Anatolia Centrale"),
        new("Kirşehir", "Q134187", "Anatolia Centrale"),
        new("Konya", "Q81551", "Anatolia Centrale"),
        new("Kırıkkale", "Q484392", "Anatolia Centrale"),
        new("Nevşehir", "Q430693", "Anatolia Centrale"),
        new("Niğde", "Q155219", "Anatolia Centrale"),
        new("Sivas", "Q483100", "Anatolia Centrale"),
        new("Yozgat", "Q75445", "Anatolia Centrale"),
        new("Çankırı", "Q272662", "Anatolia Centrale"),

        // Anatolia Orientale
        new("Ardahan", "Q79840", "Anatolia Orientale"),
        new("Ağrı", "Q80051", "Anatolia Orientale"),
        new("Bingöl", "Q79760", "Anatolia Orientale"),
        new("Bitlis", "Q83239", "Anatolia Orientale"),
        new("Elâzığ", "Q483091", "Anatolia Orientale"),
        new("Erzincan", "Q483173", "Anatolia Orientale"),
        new("Erzurum", "Q376797", "Anatolia Orientale"),
        new("Hakkâri", "Q93209", "Anatolia Orientale"),
        new("Iğdır", "Q125506", "Anatolia Orientale"),
        new("Kars", "Q83077", "Anatolia Orientale"),
        new("Malatya", "Q131384", "Anatolia Orientale"),
        new("Muş", "Q131387", "Anatolia Orientale"),
        new("Tunceli", "Q620742", "Anatolia Orientale"),
        new("Van", "Q80550", "Anatolia Orientale"),

        // Anatolia Sud Orientale
        new("Adıyaman", "Q43924", "Anatolia Sud Orientale"),
        new("Batman", "Q80370", "Anatolia Sud Orientale"),
        new("Diyarbakır", "Q83081", "Anatolia Sud Orientale"),
        new("Gaziantep", "Q483154", "Anatolia Sud Orientale"),
        new("Kilis", "Q128978", "Anatolia Sud Orientale"),
        new("Mardin", "Q131293", "Anatolia Sud Orientale"),
        new("Siirt", "Q482825", "Anatolia Sud Orientale"),
        new("Şanlıurfa", "Q388469", "Anatolia Sud Orientale"),
        new("Şırnak", "Q647378", "Anatolia Sud Orientale"),

        // Egeo
        new("Afyonkarahisar", "Q45220", "Egeo"),
        new("Aydın", "Q79846", "Egeo"),
        new("Denizli", "Q82096", "Egeo"),
        new("Kütahya", "Q126874", "Egeo"),
        new("Manisa", "Q130553", "Egeo"),
        new("Muğla", "Q123934", "Egeo"),
        new("Smirne", "Q344490", "Egeo"),
        new("Uşak", "Q483078", "Egeo"),

        // Mar Mediterraneo
        new("Adalia", "Q40249", "Mar Mediterraneo"),
        new("Adana", "Q40549", "Mar Mediterraneo"),
        new("Burdur", "Q80088", "Mar Mediterraneo"),
        new("Hatay", "Q83274", "Mar Mediterraneo"),
        new("Isparta", "Q268043", "Mar Mediterraneo"),
        new("Kahramanmaraş", "Q482834", "Mar Mediterraneo"),
        new("Mersina", "Q132637", "Mar Mediterraneo"),
        new("Osmaniye", "Q281206", "Mar Mediterraneo"),

        // Mar Nero
        new("Amasya", "Q80036", "Mar Nero"),
        new("Artvin", "Q43745", "Mar Nero"),
        new("Bartın", "Q83342", "Mar Nero"),
        new("Bayburt", "Q483063", "Mar Nero"),
        new("Bolu", "Q82089", "Mar Nero"),
        new("Düzce", "Q432391", "Mar Nero"),
        new("Giresun", "Q482779", "Mar Nero"),
        new("Gümüşhane", "Q482788", "Mar Nero"),
        new("Karabük", "Q483168", "Mar Nero"),
        new("Kastamonu", "Q483191", "Mar Nero"),
        new("Ordu", "Q483180", "Mar Nero"),
        new("Rize", "Q483481", "Mar Nero"),
        new("Samsun", "Q483040", "Mar Nero"),
        new("Sinope", "Q134413", "Mar Nero"),
        new("Tokat", "Q483195", "Mar Nero"),
        new("Trebisonda", "Q388995", "Mar Nero"),
        new("Zonguldak", "Q219956", "Mar Nero"),
        new("Çorum", "Q272947", "Mar Nero"),

        // Marmara
        new("Balıkesir", "Q47117", "Marmara"),
        new("Bilecik", "Q46763", "Marmara"),
        new("Bursa", "Q43690", "Marmara"),
        new("Edirne", "Q83102", "Marmara"),
        new("Istanbul", "Q534799", "Marmara"),
        new("Kocaeli", "Q83965", "Marmara"),
        new("Kırklareli", "Q131597", "Marmara"),
        new("Sakarya", "Q83069", "Marmara"),
        new("Tekirdağ", "Q129387", "Marmara"),
        new("Yalova", "Q483083", "Marmara"),
        new("Çanakkale", "Q47813", "Marmara"),
    ];

    private static readonly Dictionary<string, Province> ById = Provinces.ToDictionary(p => p.WikidataId, StringComparer.Ordinal);

    // Regione della provincia indicata dal codice Wikidata (per GeoSchemas.ImpliedLevel).
    public static IReadOnlyDictionary<string, string> RegionByProvinceId { get; } = Provinces.ToDictionary(p => p.WikidataId, p => p.Regione, StringComparer.Ordinal);

    // La provincia dal codice Wikidata o dal nome («provincia di Hatay», «Hatay»), senza badare alle maiuscole.
    public static Province? Find(string nome, string? wikidataId = null)
    {
        if (!string.IsNullOrEmpty(wikidataId) && ById.TryGetValue(wikidataId, out var byId))
        {
            return byId;
        }

        var shortName = System.Text.RegularExpressions.Regex.Replace(nome.Trim(), @"^provincia\s+di\s+", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return Provinces.FirstOrDefault(p => string.Equals(p.Nome, shortName, StringComparison.OrdinalIgnoreCase));
    }

    // Livelli di un luogo turco importato: la provincia prende il nome breve; una città che Wikidata mette direttamente
    // nella provincia (Antiochia, «distretto e città») è il distretto, senza un comune con lo stesso nome sotto. Vero se
    // ha cambiato qualcosa.
    public static bool Normalize(List<GeoLevel> levels, IEnumerable<PlaceDivision> divisions)
    {
        if (levels.Count == 0 || !string.Equals(levels[0].Nome.Trim(), Country, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var changed = false;
        var ids = divisions
            .Where(d => d.Name.Trim().Length > 0 && d.WikidataId.Length > 0)
            .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().WikidataId, StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels.Where(l => l.Tipo == "Provincia" && !l.Assente))
        {
            if (Find(level.Nome, ids.GetValueOrDefault(level.Nome.Trim())) is { } province && level.Nome != province.Nome)
            {
                level.Nome = province.Nome;
                changed = true;
            }
        }

        var district = levels.FindIndex(l => l.Tipo == "Distretto");
        if (district > 0 && district == levels.Count - 2 && levels[^1].Tipo == "Comune" && !levels[^1].Assente && levels[^1].Nome.Trim().Length > 0
            && (levels[district].Assente || levels[district].Nome.Trim().Length == 0))
        {
            var town = levels[^1];
            levels[district] = new GeoLevel("Distretto", town.Nome) { Nomi = town.Nomi, Latitudine = town.Latitudine, Longitudine = town.Longitudine };
            levels.RemoveAt(levels.Count - 1);
            changed = true;
        }

        return changed;
    }
}
