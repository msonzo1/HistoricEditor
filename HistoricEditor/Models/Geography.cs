namespace HistoricEditor;

// Nome di un luogo in una determinata lingua (it/en/de/fr/es/nl), dalle etichette Wikidata.
public sealed class PlaceName
{
    public string Language { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class Luogo
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int? ParentId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Luogo? Parent { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public List<Luogo> Figli { get; set; } = [];
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }

    // Nomi del luogo nelle altre lingue (etichette Wikidata).
    public List<PlaceName> NomiAltreLingue { get; set; } = [];

    // Il livello successivo dello schema non esiste sotto questo luogo (es. Comunità di Madrid senza Provincia).
    public bool SenzaLivelloInferiore { get; set; }

    // Luoghi storici non più esistenti.
    public bool Storico { get; set; }
    public string Periodo { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    // Nomi usati nel tempo (es. Bisanzio, Costantinopoli per Istanbul).
    public List<NomeStorico> NomiStorici { get; set; } = [];
    public string WikidataId { get; set; } = string.Empty;
}

// Luogo puntuale (edificio, monastero, reggia…) sempre contenuto in un luogo amministrativo.
public sealed class LuogoSpecifico
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int LuogoId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Luogo? Luogo { get; set; }
    public string Indirizzo { get; set; } = string.Empty;
    public string Condizione { get; set; } = CondizioniLuogo.Esistente;
    public string DataDistruzione { get; set; } = string.Empty;
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }
    public string Note { get; set; } = string.Empty;
    public string WikidataId { get; set; } = string.Empty;
}

public static class CondizioniLuogo
{
    public const string Esistente = "Esistente";
    public const string InRovina = "In rovina";
    public const string Distrutto = "Distrutto";

    public static readonly string[] Tutte = [Esistente, InRovina, Distrutto];
}

// Schemi dei livelli amministrativi per Paese (dal livello radice al più fine).
public static class GeoSchemas
{
    private static readonly Dictionary<string, string[]> ByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Italia"] = ["Stato", "Regione", "Provincia", "Comune"],
        ["Francia"] = ["Stato", "Regione", "Dipartimento", "Arrondissement", "Comune"],
        ["Germania"] = ["Stato", "Land", "Circondario", "Comune"],
        ["Spagna"] = ["Stato", "Comunità autonoma", "Provincia", "Comune"],
        ["Portogallo"] = ["Stato", "Regione", "Distretto", "Comune"],
        ["Belgio"] = ["Stato", "Regione", "Provincia", "Arrondissement", "Comune"],
        ["Paesi Bassi"] = ["Stato", "Provincia", "Comune"],
        ["Austria"] = ["Stato", "Land", "Distretto", "Comune"],
        ["Regno Unito"] = ["Stato", "Nazione", "Contea", "Distretto", "Comune"],
        ["Irlanda"] = ["Stato", "Provincia", "Contea", "Comune"],
        ["Estonia"] = ["Stato", "Contea", "Municipalità", "Comune"],
        ["Lettonia"] = ["Stato", "Municipalità", "Comune"],
        ["Lituania"] = ["Stato", "Contea", "Municipalità", "Comune"],
        ["Danimarca"] = ["Stato", "Regione", "Municipalità", "Comune"],
        ["Norvegia"] = ["Stato", "Regione", "Contea", "Municipalità", "Comune"],
        ["Svezia"] = ["Stato", "Regione", "Contea", "Municipalità", "Comune"],
        ["Finlandia"] = ["Stato", "Regione", "Comune"],
        ["Svizzera"] = ["Stato", "Cantone", "Distretto", "Comune"],
        ["Liechtenstein"] = ["Stato", "Comune"],
        ["Lussemburgo"] = ["Stato", "Cantone", "Comune"],
        ["Monaco"] = ["Stato", "Comune"],
        ["Andorra"] = ["Stato", "Parrocchia", "Comune"],
        ["San Marino"] = ["Stato", "Comune"],
        ["Città del Vaticano"] = ["Stato", "Comune"],
        ["Malta"] = ["Stato", "Regione", "Comune"],
        ["Islanda"] = ["Stato", "Regione", "Municipalità", "Comune"],
        ["Polonia"] = ["Stato", "Voivodato", "Distretto", "Gmina", "Comune"],
        ["Repubblica Ceca"] = ["Stato", "Regione", "Distretto", "Comune"],
        ["Slovacchia"] = ["Stato", "Regione", "Distretto", "Comune"],
        ["Ungheria"] = ["Stato", "Provincia", "Distretto", "Comune"],
        ["Slovenia"] = ["Stato", "Municipalità", "Comune"],
        ["Croazia"] = ["Stato", "Contea", "Comune"],
        ["Bosnia ed Erzegovina"] = ["Stato", "Entità", "Cantone", "Comune"],
        ["Serbia"] = ["Stato", "Distretto", "Comune"],
        ["Montenegro"] = ["Stato", "Municipalità", "Comune"],
        ["Kosovo"] = ["Stato", "Distretto", "Municipalità", "Comune"],
        ["Albania"] = ["Stato", "Prefettura", "Municipalità", "Comune"],
        ["Macedonia del Nord"] = ["Stato", "Municipalità", "Comune"],
        ["Grecia"] = ["Stato", "Periferia", "Unità periferica", "Municipalità", "Comune"],
        ["Bulgaria"] = ["Stato", "Provincia", "Municipalità", "Comune"],
        ["Romania"] = ["Stato", "Distretto", "Comune"],
        ["Moldavia"] = ["Stato", "Distretto", "Comune"],
        ["Cipro"] = ["Stato", "Distretto", "Comune"],
        ["Turchia"] = ["Stato", "Provincia", "Comune"],
        ["Ucraina"] = ["Stato", "Oblast", "Distretto", "Comunità", "Comune"],
        ["Bielorussia"] = ["Stato", "Regione", "Comune"],
        ["Russia"] = ["Stato", "Soggetto federale", "Comune"],
    };

    // Paesi con catene P131 di profondità variabile (città statutarie austriache, regioni/contee/distretti
    // diversi tra Inghilterra, Scozia, Galles e Irlanda del Nord): i livelli intermedi vengono compattati.
    private static readonly HashSet<string> FlexibleCountries = new(StringComparer.OrdinalIgnoreCase) { "Austria", "Regno Unito" };

    public static bool HasFlexibleDepth(string country)
        => !string.IsNullOrWhiteSpace(country) && FlexibleCountries.Contains(country.Trim());

    public static readonly string[] Fallback = ["Stato", "Divisione", "Comune"];

    // Suddivisioni note prive del livello successivo, usate per riconoscerle durante l'import.
    private static readonly (string Country, string Tipo, string[] Keywords)[] NoLowerLevelHints =
    [
        ("Spagna", "Comunità autonoma", ["Madrid", "Asturie", "Asturias", "Cantabria", "Rioja", "Navarra", "Baleari", "Balears", "Baleares", "Murcia", "Ceuta", "Melilla"]),
        ("Belgio", "Regione", ["Bruxelles"]),
        // La Valle d'Aosta non ha province.
        ("Italia", "Regione", ["Valle d'Aosta", "Vallée d'Aoste"]),
        // Parigi e la Metropoli di Lione sono collettività senza arrondissement nella catena Wikidata.
        ("Francia", "Dipartimento", ["Parigi", "Paris", "Metropoli di Lione", "Métropole de Lyon"]),
        // Le regioni autonome portoghesi non sono suddivise in distretti.
        ("Portogallo", "Regione", ["Azzorre", "Azores", "Açores", "Madera", "Madeira"]),
        // Cantoni svizzeri non suddivisi in distretti.
        ("Svizzera", "Cantone", ["Basilea Città", "Basel-Stadt", "Ginevra", "Genève", "Geneva", "Zugo", "Zug", "Canton Uri", "Cantone di Uri", "Kanton Uri", "Canton of Uri", "Obvaldo", "Obwalden", "Nidvaldo", "Nidwalden", "Glarona", "Glarus", "Appenzello Esterno", "Appenzell Ausserrhoden", "Neuchâtel", "Sciaffusa", "Schaffhausen"]),
        // La Republika Srpska non è divisa in cantoni.
        ("Bosnia ed Erzegovina", "Entità", ["Republika Srpska", "Repubblica Serba", "Brčko"]),
    ];

    // Paesi i cui livelli si leggono dall'infobox {{Divisione amministrativa}} della pagina it.wikipedia del comune.
    private static readonly HashSet<string> InfoboxCountries = new(StringComparer.OrdinalIgnoreCase) { "Portogallo" };

    public static bool UsesInfoboxDivisions(string country)
        => !string.IsNullOrWhiteSpace(country) && InfoboxCountries.Contains(country.Trim());

    // Suddivisioni Wikidata che non corrispondono a un livello dello schema (es. Francia metropolitana, Q212429).
    private static readonly HashSet<string> TransparentDivisions = new(StringComparer.Ordinal) { "Q212429" };

    public static bool IsTransparentDivision(string wikidataId)
        => !string.IsNullOrEmpty(wikidataId) && TransparentDivisions.Contains(wikidataId);

    public static IReadOnlyList<string> KnownCountries => [.. ByCountry.Keys];

    // Tipi Wikidata (P31) che identificano ciascun livello dello schema, per Paese.
    // TODO: Francia d'oltremare (Polinesia, Nuova Caledonia, Saint-Pierre-et-Miquelon…).
    private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> LevelTypesByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Italia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Regione, regione a statuto speciale.
            ["Regione"] = new(StringComparer.Ordinal) { "Q16110", "Q1710033" },
            // Provincia (anche autonoma), città metropolitana, libero consorzio siciliano, provincia soppressa.
            ["Provincia"] = new(StringComparer.Ordinal) { "Q15089", "Q15110", "Q21190155", "Q3924474" },
            // Comune, comune sparso.
            ["Comune"] = new(StringComparer.Ordinal) { "Q747074", "Q954172" },
        },
        ["Francia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Regione"] = new(StringComparer.Ordinal) { "Q36784" },
            // Dipartimento, dipartimento d'oltremare, collettività a statuto particolare (Parigi, Metropoli di Lione).
            ["Dipartimento"] = new(StringComparer.Ordinal) { "Q6465", "Q202216", "Q22923920" },
            ["Arrondissement"] = new(StringComparer.Ordinal) { "Q194203" },
            // Comune, comune a statuto particolare (Lione, Marsiglia).
            ["Comune"] = new(StringComparer.Ordinal) { "Q484170", "Q22927616" },
        },
        // Il Regierungsbezirk non ha un livello nello schema: non avendo un tipo previsto viene saltato.
        ["Germania"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Land"] = new(StringComparer.Ordinal) { "Q1221156" },
            // Circondario (Landkreis) e città extracircondariale (kreisfreie Stadt), con le sottoclassi per Land
            // usate da Wikidata. Berlino e Amburgo sono insieme Land, città extracircondariale e comune.
            ["Circondario"] = new(StringComparer.Ordinal)
            {
                "Q106658", "Q17302772", "Q20738676", "Q20738811", "Q20738945", "Q61708063", "Q61793460", "Q61818979",
                "Q61856889", "Q61980413", "Q85332736", "Q85482556", "Q85483234", "Q85493040",
                "Q22865", "Q2327515", "Q61708053", "Q61708099", "Q61747322", "Q61793478", "Q61856863", "Q61980648",
                "Q85631896", "Q85632032", "Q85635301", "Q85635630", "Q85635638", "Q85635929", "Q85636199", "Q114401982",
            },
            // Comune, città, comune unitario, Ortsgemeinde, comune mercato, grande/media città circondariale, città a statuto speciale.
            ["Comune"] = new(StringComparer.Ordinal)
            {
                "Q262166", "Q42744322", "Q15974307", "Q41778911", "Q100341898", "Q116457956", "Q1650922",
                "Q448801", "Q1548518", "Q1548525", "Q54935786", "Q3678424",
            },
        },
        // Le comarche e le aree metropolitane tra comune e provincia non hanno un tipo previsto e vengono saltate.
        ["Spagna"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Comunità autonoma, nazionalità storica, comunità forale (Navarra), città autonoma (Ceuta, Melilla).
            ["Comunità autonoma"] = new(StringComparer.Ordinal) { "Q10742", "Q2736168", "Q3297051", "Q16532593" },
            // Provincia, provincia della Catalogna.
            ["Provincia"] = new(StringComparer.Ordinal) { "Q162620", "Q4379648" },
            // Comune e sottoclassi per comunità autonoma (consiglio delle Asturie, cendea navarrese…); città autonoma.
            ["Comune"] = new(StringComparer.Ordinal)
            {
                "Q2074737", "Q2276925", "Q3284867", "Q5055981", "Q20103881", "Q33146843", "Q61763947", "Q123095923",
                "Q123680441", "Q123754112", "Q125576665", "Q125576927", "Q125576962", "Q125577012", "Q125577043",
                "Q125577059", "Q125577083", "Q125577114", "Q125577150", "Q16532593",
            },
        },
        // Ripiego quando manca l'infobox: le regioni continentali (Nord, Centro…) non sono nella catena P131,
        // quindi solo Azzorre e Madera risultano complete. Ex province e sottoregioni vengono saltate.
        ["Portogallo"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Regione autonoma.
            ["Regione"] = new(StringComparer.Ordinal) { "Q2914565" },
            ["Distretto"] = new(StringComparer.Ordinal) { "Q3032141" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q13217644" },
        },
        // Le Comunità (francese, fiamminga, germanofona) non sono un livello dello schema e vengono saltate.
        ["Belgio"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Regione"] = new(StringComparer.Ordinal) { "Q83057" },
            // Provincia, provincia della Vallonia, provincia delle Fiandre.
            ["Provincia"] = new(StringComparer.Ordinal) { "Q83116", "Q8218604", "Q15051068" },
            ["Arrondissement"] = new(StringComparer.Ordinal) { "Q91028" },
            // Comune, comune con facilitazioni linguistiche, comune di confine linguistico, comune con titolo di città.
            ["Comune"] = new(StringComparer.Ordinal) { "Q493522", "Q737681", "Q4314343", "Q15273785" },
        },
        // Le città contenute nel comune omonimo (es. Amsterdam) usano il comune come foglia.
        ["Paesi Bassi"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Provincia"] = new(StringComparer.Ordinal) { "Q134390" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q2039348" },
        },
        // Regioni inglesi e regioni statistiche non hanno un livello nello schema e vengono saltate.
        // Senza tipi riconoscibili si ripiega sulla mappatura a profondità variabile.
        ["Regno Unito"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Nazione costitutiva (Inghilterra, Scozia, Galles, Irlanda del Nord).
            ["Nazione"] = new(StringComparer.Ordinal) { "Q3336843" },
            // Contea cerimoniale, non metropolitana, metropolitana; council area scozzese, principal area gallese,
            // distretto dell'Irlanda del Nord (questi ultimi tre senza distretti).
            ["Contea"] = new(StringComparer.Ordinal) { "Q180673", "Q769603", "Q769628", "Q15060255", "Q15979307", "Q1049540" },
            // Distretto non metropolitano, metropolitan borough, London borough, unitary authority.
            ["Distretto"] = new(StringComparer.Ordinal) { "Q1187580", "Q1002812", "Q211690", "Q1136601" },
            // Città, città del Regno Unito, town, market town, villaggio, insediamento, civil parish.
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q110390579", "Q3957", "Q18511725", "Q532", "Q486972", "Q1115575" },
        },
        // Le regioni statistiche NUTS 3 vengono saltate.
        ["Irlanda"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Provincia storica (Leinster, Munster, Connacht, Ulster).
            ["Provincia"] = new(StringComparer.Ordinal) { "Q202156" },
            // Contea, città con funzioni di contea (Dublin City, Cork City…).
            ["Contea"] = new(StringComparer.Ordinal) { "Q179872" },
            // Città, città amministrativa, town, villaggio, insediamento.
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q13455645", "Q3957", "Q532", "Q486972" },
        },
        ["Estonia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Contea (maakond).
            ["Contea"] = new(StringComparer.Ordinal) { "Q189672" },
            // Comune urbano (linn), comune rurale (vald), comune dell'Estonia.
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q21583365", "Q28122896", "Q612229" },
            // Città dell'Estonia, città, town, villaggio, insediamento.
            ["Comune"] = new(StringComparer.Ordinal) { "Q11881845", "Q515", "Q3957", "Q532", "Q486972" },
        },
        // Dal 2021 i comuni (novadi) e le città statali dipendono direttamente dallo Stato; le parrocchie (pagasti) vengono saltate.
        ["Lettonia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Comune (novads), città statale (anche foglia, es. Riga).
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q3345345", "Q109329953" },
            // Città statale, città sotto la giurisdizione di un comune, città, town, villaggi della Lettonia, insediamento.
            ["Comune"] = new(StringComparer.Ordinal)
            {
                "Q109329953", "Q15584664", "Q515", "Q3957", "Q532", "Q22580836", "Q16362394", "Q16355778", "Q16363214", "Q486972",
            },
        },
        // Le seniūnijos vengono saltate.
        ["Lituania"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Contea (apskritis).
            ["Contea"] = new(StringComparer.Ordinal) { "Q273789" },
            // Comune della Lituania, comune urbano, comune distrettuale.
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q1363145", "Q17166803", "Q17301072" },
            // Città, town, villaggio, insediamento.
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q3957", "Q532", "Q486972" },
        },
        ["Danimarca"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Regione della Danimarca.
            ["Regione"] = new(StringComparer.Ordinal) { "Q62326" },
            // Comune (kommune).
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q2177636" },
            // Città, grande città, town, villaggio, insediamento.
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q1549591", "Q3957", "Q532", "Q486972" },
        },
        ["Norvegia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Contea (fylke); Oslo è sia contea sia comune.
            ["Contea"] = new(StringComparer.Ordinal) { "Q192299" },
            // Comune (kommune).
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q755707", "Q99691499" },
            // Area urbana della Norvegia, città, grande città, town, villaggio, insediamento.
            ["Comune"] = new(StringComparer.Ordinal) { "Q15092344", "Q515", "Q1549591", "Q3957", "Q532", "Q486972" },
        },
        ["Svezia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Contea (län).
            ["Contea"] = new(StringComparer.Ordinal) { "Q200547" },
            // Comune (kommun), compresa la Region Gotland.
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q127448" },
            // Area urbana della Svezia (tätort), città, grande città, town, villaggio, insediamento.
            ["Comune"] = new(StringComparer.Ordinal) { "Q12813115", "Q515", "Q1549591", "Q3957", "Q532", "Q486972" },
        },
        // I comuni finlandesi coincidono con la città (Turku, Porvoo); le subregioni vengono saltate.
        ["Finlandia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Regione (maakunta).
            ["Regione"] = new(StringComparer.Ordinal) { "Q193512" },
            // Comune della Finlandia, città della Finlandia.
            ["Comune"] = new(StringComparer.Ordinal) { "Q856076", "Q11870638" },
        },
        // Le città statutarie sono insieme distretto e comune; Vienna è Land, distretto e comune.
        ["Austria"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Land"] = new(StringComparer.Ordinal) { "Q261543" },
            // Distretto, città statutaria.
            ["Distretto"] = new(StringComparer.Ordinal) { "Q871419", "Q262882" },
            // Comune, città statutaria, comune rurale, comune mercato, città con diritti cittadini.
            ["Comune"] = new(StringComparer.Ordinal) { "Q667509", "Q262882", "Q1802801", "Q562061", "Q13539802" },
        },
        ["Svizzera"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Cantone, semicantone.
            ["Cantone"] = new(StringComparer.Ordinal) { "Q23058", "Q692170" },
            // Distretto della Svizzera e sottoclassi cantonali.
            ["Distretto"] = new(StringComparer.Ordinal)
            {
                "Q662914", "Q628993", "Q2864107", "Q2864207", "Q3032116", "Q3032119", "Q3032122", "Q3032124", "Q3032126", "Q3032131",
                "Q3032132", "Q3032133", "Q3032137", "Q3032139", "Q3091916", "Q15644465", "Q17276572", "Q17278014", "Q23013985", "Q60299921", "Q140589112",
            },
            // Comune, città della Svizzera.
            ["Comune"] = new(StringComparer.Ordinal) { "Q70208", "Q54935504" },
        },
        ["Liechtenstein"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Comune"] = new(StringComparer.Ordinal) { "Q203300" },
        },
        ["Lussemburgo"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cantone"] = new(StringComparer.Ordinal) { "Q1146429" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q2919801" },
        },
        ["Andorra"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Parrocchia"] = new(StringComparer.Ordinal) { "Q24279" },
            // Insediamento di Andorra, città, villaggio.
            ["Comune"] = new(StringComparer.Ordinal) { "Q41108733", "Q515", "Q532" },
        },
        // I castelli (castelli) sono i comuni sammarinesi.
        ["San Marino"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Comune"] = new(StringComparer.Ordinal) { "Q14292916" },
        },
        ["Malta"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Regione"] = new(StringComparer.Ordinal) { "Q7309296" },
            // Consiglio locale.
            ["Comune"] = new(StringComparer.Ordinal) { "Q719592" },
        },
        ["Islanda"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Regione"] = new(StringComparer.Ordinal) { "Q842100" },
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q955655" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q3957", "Q532", "Q486972" },
        },
        // Le città con diritti di distretto (Cracovia) sono insieme distretto, gmina e comune.
        ["Polonia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Voivodato"] = new(StringComparer.Ordinal) { "Q150093" },
            // Distretto (powiat), città con diritti di distretto.
            ["Distretto"] = new(StringComparer.Ordinal) { "Q247073", "Q925381" },
            // Gmina urbana, urbano-rurale, rurale; città con diritti di distretto.
            ["Gmina"] = new(StringComparer.Ordinal) { "Q2616791", "Q3491915", "Q3504085", "Q925381" },
            // Città, grande città, town, villaggio della Polonia, insediamento; gmina urbana e città con diritti di distretto.
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q1549591", "Q3957", "Q3558970", "Q532", "Q486972", "Q2616791", "Q925381" },
        },
        // Praga è regione, distretto e comune. I distretti amministrativi ORP vengono saltati.
        ["Repubblica Ceca"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Regione"] = new(StringComparer.Ordinal) { "Q38911" },
            ["Distretto"] = new(StringComparer.Ordinal) { "Q548611" },
            // Comune ceco e sottoclassi (città, città statutaria, comuni con poteri estesi/ufficio autorizzato).
            ["Comune"] = new(StringComparer.Ordinal) { "Q5153359", "Q15978299", "Q257978", "Q7819319", "Q7841907", "Q16155707" },
        },
        ["Slovacchia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Regione"] = new(StringComparer.Ordinal) { "Q15057583" },
            ["Distretto"] = new(StringComparer.Ordinal) { "Q2264478" },
            // Comune della Slovacchia, città, town.
            ["Comune"] = new(StringComparer.Ordinal) { "Q6784672", "Q515", "Q3957" },
        },
        // Budapest non appartiene a nessuna provincia.
        ["Ungheria"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Provincia (vármegye).
            ["Provincia"] = new(StringComparer.Ordinal) { "Q188604" },
            ["Distretto"] = new(StringComparer.Ordinal) { "Q1162835" },
            // Comune, città dell'Ungheria, città con diritti di provincia, megyei város.
            ["Comune"] = new(StringComparer.Ordinal) { "Q2590631", "Q13218690", "Q681277", "Q1175519" },
        },
        ["Slovenia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Comune, comune urbano.
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q328584", "Q6960199" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q1549591", "Q3957", "Q532", "Q486972" },
        },
        // Zagabria è città e contea insieme e dipende direttamente dallo Stato.
        ["Croazia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Contea"] = new(StringComparer.Ordinal) { "Q57053" },
            // Città della Croazia, comune croato, villaggio, insediamento.
            ["Comune"] = new(StringComparer.Ordinal) { "Q15105893", "Q57058", "Q532", "Q486972" },
        },
        ["Bosnia ed Erzegovina"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Federazione di Bosnia ed Erzegovina, Republika Srpska.
            ["Entità"] = new(StringComparer.Ordinal) { "Q140937749" },
            ["Cantone"] = new(StringComparer.Ordinal) { "Q18279" },
            // Città della Bosnia ed Erzegovina, comune della Republika Srpska, città, town.
            ["Comune"] = new(StringComparer.Ordinal) { "Q102104752", "Q57315116", "Q515", "Q3957" },
        },
        // Le province autonome (Voivodina) e le municipalità urbane vengono saltate.
        ["Serbia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Distretto"] = new(StringComparer.Ordinal) { "Q648681" },
            // Città serba, comune della Serbia, città, town, villaggio.
            ["Comune"] = new(StringComparer.Ordinal) { "Q37800986", "Q783930", "Q515", "Q3957", "Q532" },
        },
        ["Montenegro"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q838549" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q3957", "Q532" },
        },
        ["Kosovo"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Distretto"] = new(StringComparer.Ordinal) { "Q2915243" },
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q2989682" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q1549591", "Q3957", "Q532" },
        },
        ["Albania"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Prefettura"] = new(StringComparer.Ordinal) { "Q104251" },
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q768307" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q3957", "Q532", "Q486972" },
        },
        ["Macedonia del Nord"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q646793" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q3957", "Q532" },
        },
        // Le amministrazioni decentrate e le unità municipali vengono saltate.
        ["Grecia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Periferia"] = new(StringComparer.Ordinal) { "Q207299" },
            ["Unità periferica"] = new(StringComparer.Ordinal) { "Q1234255" },
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q1349648" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q515", "Q1549591", "Q3957", "Q532", "Q486972" },
        },
        ["Bulgaria"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Provincia (oblast).
            ["Provincia"] = new(StringComparer.Ordinal) { "Q209824" },
            ["Municipalità"] = new(StringComparer.Ordinal) { "Q1906268" },
            // Città della Bulgaria, città, town, villaggio.
            ["Comune"] = new(StringComparer.Ordinal) { "Q89487741", "Q515", "Q3957", "Q532" },
        },
        ["Romania"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Distretto (județ).
            ["Distretto"] = new(StringComparer.Ordinal) { "Q1776764" },
            // Municipiu, città, town, villaggio.
            ["Comune"] = new(StringComparer.Ordinal) { "Q640364", "Q515", "Q3957", "Q532" },
        },
        ["Moldavia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Distretto (raion).
            ["Distretto"] = new(StringComparer.Ordinal) { "Q15068450" },
            ["Comune"] = new(StringComparer.Ordinal) { "Q1963041", "Q515", "Q3957", "Q532" },
        },
        ["Cipro"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Distretto"] = new(StringComparer.Ordinal) { "Q59136" },
            // Città di Cipro, città, town, villaggio.
            ["Comune"] = new(StringComparer.Ordinal) { "Q29556224", "Q515", "Q3957", "Q532" },
        },
        // I distretti (ilçe) non sono nella catena P131 delle città: vengono saltati.
        ["Turchia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Provincia"] = new(StringComparer.Ordinal) { "Q48336" },
            // Comune metropolitano, città, grande città, town, villaggio.
            ["Comune"] = new(StringComparer.Ordinal) { "Q2716259", "Q6024226", "Q515", "Q1549591", "Q3957", "Q532" },
        },
        ["Ucraina"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Oblast"] = new(StringComparer.Ordinal) { "Q3348196" },
            // Distretto (rajon).
            ["Distretto"] = new(StringComparer.Ordinal) { "Q1267632", "Q111262701" },
            // Comunità territoriale (hromada).
            ["Comunità"] = new(StringComparer.Ordinal) { "Q104841013", "Q4291381", "Q113637359" },
            // Città dell'Ucraina e sottoclassi, città, town, villaggio.
            ["Comune"] = new(StringComparer.Ordinal) { "Q12131624", "Q5123999", "Q5124045", "Q12131640", "Q515", "Q3957", "Q532" },
        },
        // I distretti (rajon) vengono saltati.
        ["Bielorussia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Regione (voblasc').
            ["Regione"] = new(StringComparer.Ordinal) { "Q209325" },
            // Città di subordinazione regionale, città o paese, città, town, villaggio.
            ["Comune"] = new(StringComparer.Ordinal) { "Q79323854", "Q7930989", "Q515", "Q1549591", "Q3957", "Q532" },
        },
        // Mosca, San Pietroburgo e Sebastopoli sono insieme soggetto federale e città. Distretti e okrug vengono saltati.
        ["Russia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Soggetto federale e sottoclassi (repubblica, oblast', città federale, kraj, okrug autonomo, oblast' autonoma).
            ["Soggetto federale"] = new(StringComparer.Ordinal) { "Q43263", "Q41162", "Q183342", "Q184122", "Q309166", "Q831740", "Q835714" },
            // Città o paese, città, grande città, town, villaggio, città federale.
            ["Comune"] = new(StringComparer.Ordinal) { "Q7930989", "Q515", "Q1549591", "Q3957", "Q532", "Q183342" },
        },
    };

    // Livelli assenti dalla catena P131 di Wikidata, dedotti dall'ID dell'entità del livello inferiore.
    // Le regioni della Norvegia (landsdeler) sono collegate alle contee solo tramite "parte di" (P361).
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> ImpliedLevelsByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Norvegia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Regione"] = new(StringComparer.Ordinal)
            {
                ["Q50632"] = "Nord-Norge", // Finnmark
                ["Q50631"] = "Nord-Norge", // Troms
                ["Q50630"] = "Nord-Norge", // Nordland
                ["Q127676"] = "Trøndelag", // Trøndelag
                ["Q50627"] = "Vestlandet", // Møre og Romsdal
                ["Q56407177"] = "Vestlandet", // Vestland
                ["Q50624"] = "Vestlandet", // Rogaland
                ["Q2729021"] = "Sørlandet", // Agder
                ["Q5245991"] = "Østlandet", // Oslo
                ["Q50615"] = "Østlandet", // Akershus
                ["Q56404886"] = "Østlandet", // Innlandet
                ["Q50618"] = "Østlandet", // Buskerud
                ["Q50614"] = "Østlandet", // Østfold
                ["Q2254"] = "Østlandet", // Telemark
                ["Q50619"] = "Østlandet", // Vestfold
            },
        },
        // Terre (landsdelar); i confini storici non coincidono del tutto con le contee, si usa la terra prevalente.
        ["Svezia"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Regione"] = new(StringComparer.Ordinal)
            {
                ["Q102377"] = "Götaland", // Blekinge
                ["Q103738"] = "Götaland", // Gotland
                ["Q103691"] = "Götaland", // Halland
                ["Q103672"] = "Götaland", // Jönköping
                ["Q103707"] = "Götaland", // Kalmar
                ["Q104746"] = "Götaland", // Kronoberg
                ["Q103659"] = "Götaland", // Skåne
                ["Q103093"] = "Götaland", // Västra Götaland
                ["Q104940"] = "Götaland", // Östergötland
                ["Q103732"] = "Svealand", // Dalarna
                ["Q104231"] = "Svealand", // Stoccolma
                ["Q106915"] = "Svealand", // Södermanland
                ["Q104926"] = "Svealand", // Uppsala
                ["Q106789"] = "Svealand", // Värmland
                ["Q105075"] = "Svealand", // Västmanland
                ["Q104257"] = "Svealand", // Örebro
                ["Q103699"] = "Norrland", // Gävleborg
                ["Q103679"] = "Norrland", // Jämtland
                ["Q103686"] = "Norrland", // Norrbotten
                ["Q104877"] = "Norrland", // Västerbotten
                ["Q104891"] = "Norrland", // Västernorrland
            },
        },
    };

    public static string? ImpliedLevel(string country, string level, string? lowerWikidataId)
        => !string.IsNullOrEmpty(lowerWikidataId)
            && ImpliedLevelsByCountry.TryGetValue(country.Trim(), out var byLevel)
            && byLevel.TryGetValue(level, out var byId)
            && byId.TryGetValue(lowerWikidataId, out var name)
                ? name
                : null;

    public static IReadOnlyDictionary<string, HashSet<string>>? LevelTypes(string country)
        => !string.IsNullOrWhiteSpace(country) && LevelTypesByCountry.TryGetValue(country.Trim(), out var types) ? types : null;

    // Tipi Wikidata di suddivisioni a livello unico, prive del livello successivo dello schema
    // (es. council area scozzesi e principal area gallesi non hanno distretti).
    private static readonly Dictionary<string, HashSet<string>> NoLowerLevelTypesByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Regno Unito"] = new(StringComparer.Ordinal) { "Q15060255", "Q15979307", "Q1049540" },
    };

    public static bool TypesHintNoLowerLevel(string country, IEnumerable<string> typeIds)
        => !string.IsNullOrWhiteSpace(country)
            && NoLowerLevelTypesByCountry.TryGetValue(country.Trim(), out var types)
            && typeIds.Any(types.Contains);

    public static bool IsKnown(string country)
        => !string.IsNullOrWhiteSpace(country) && ByCountry.ContainsKey(country.Trim());

    public static string[] For(string country)
        => !string.IsNullOrWhiteSpace(country) && ByCountry.TryGetValue(country.Trim(), out var levels)
            ? levels
            : Fallback;

    public static bool HintsNoLowerLevel(string country, string tipo, string nome)
    {
        var value = nome?.Trim() ?? string.Empty;
        return value.Length > 0 && NoLowerLevelHints.Any(hint =>
            string.Equals(hint.Country, country?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(hint.Tipo, tipo, StringComparison.OrdinalIgnoreCase)
            && hint.Keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class GeoLevel
{
    public string Tipo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;

    // Nomi del livello nelle altre lingue (etichette Wikidata), salvati sul Luogo.
    public List<PlaceName> Nomi { get; set; } = [];

    // Coordinate del livello (P625 da Wikidata), salvate sul Luogo quando disponibili.
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }

    // Il livello non esiste per questo luogo (es. Provincia sotto la Comunità di Madrid).
    public bool Assente { get; set; }

    public GeoLevel()
    {
    }

    public GeoLevel(string tipo, string nome)
    {
        Tipo = tipo;
        Nome = nome;
    }
}

public static class CoordinateText
{
    public static bool TryParse(string text, out double? value)
    {
        value = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (double.TryParse(trimmed.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    public static string Format(double? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}
