using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoricEditor;

// Ricerca e import di opere dalle API aperte dei musei (Met, Art Institute of Chicago, Cleveland) e da Europeana.
// Gli artisti senza Q-id vengono cercati su Wikidata per nome e anni, così da poterli abbinare alle schede.
public sealed partial class MuseumImportService(WikipediaImportService wikipedia, string? europeanaKey = null)
{
    public const int ImportSteps = 4;

    // Chiave dimostrativa pubblica di Europeana, usata se l'utente non ne ha registrata una propria.
    public const string EuropeanaDemoKey = "api2demo";

    private const string MetApi = "https://collectionapi.metmuseum.org/public/collection/v1";
    private const string AicApi = "https://api.artic.edu/api/v1";
    private const string ClevelandApi = "https://openaccess-api.clevelandart.org/api";
    private const string EuropeanaApi = "https://api.europeana.eu/record/v2/search.json";
    private const string MetQid = "Q160236";
    private const string AicQid = "Q239303";
    private const string ClevelandQid = "Q657415";
    private const int ResultLimit = 12;
    private const long MaximumImageBytes = 60L * 1024 * 1024;
    private const string AicFields = "id,title,artist_title,artist_display,date_display,medium_display,dimensions,artwork_type_title,image_id,is_public_domain,credit_line,place_of_origin,description";

    private static readonly HttpClient Client = CreateClient();

    private string EuropeanaKey => string.IsNullOrWhiteSpace(europeanaKey) ? EuropeanaDemoKey : europeanaKey.Trim();

    public async Task<IReadOnlyList<ArtworkSearchResult>> SearchAsync(ArtworkSource source, string query, CancellationToken cancellationToken = default)
    {
        var text = Uri.EscapeDataString(query.Trim());
        switch (source)
        {
            case ArtworkSource.Metropolitan:
            {
                List<int> ids;
                using (var document = await GetJsonAsync($"{MetApi}.1/search?q={text}&limit={ResultLimit}", cancellationToken))
                {
                    ids = document.RootElement.GetPropertyOrDefault("objectIDs") is { ValueKind: JsonValueKind.Array } array
                        ? [.. array.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Number).Select(i => i.GetInt32()).Take(ResultLimit)]
                        : [];
                }

                var objects = await Task.WhenAll(ids.Select(async id =>
                {
                    try
                    {
                        using var document = await GetJsonAsync($"{MetApi}/objects/{id}", cancellationToken);
                        return ParseMetResult(document.RootElement);
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }
                }));
                return [.. objects.OfType<ArtworkSearchResult>()];
            }

            case ArtworkSource.ArtInstitute:
            {
                using var document = await GetJsonAsync($"{AicApi}/artworks/search?q={text}&limit={ResultLimit}&fields=id,title,artist_title,date_display", cancellationToken);
                return [.. Array(document.RootElement, "data").Select(d => new ArtworkSearchResult(ArtworkSource.ArtInstitute,
                    Number(d, "id"), Str(d, "title"), Join(Str(d, "artist_title"), Str(d, "date_display"))))];
            }

            case ArtworkSource.Cleveland:
            {
                using var document = await GetJsonAsync($"{ClevelandApi}/artworks/?q={text}&limit={ResultLimit}&fields=id,title,creators,creation_date,type", cancellationToken);
                return [.. Array(document.RootElement, "data").Select(d => new ArtworkSearchResult(ArtworkSource.Cleveland,
                    Number(d, "id"), Str(d, "title"),
                    Join(Array(d, "creators").Select(c => ArtworkText.ParseArtistDisplay(Str(c, "description")).Nome).FirstOrDefault() ?? string.Empty, Str(d, "creation_date"))))];
            }

            case ArtworkSource.Europeana:
            {
                using var document = await GetJsonAsync($"{EuropeanaApi}?wskey={Uri.EscapeDataString(EuropeanaKey)}&query={text}&rows={ResultLimit}&profile=rich&qf=TYPE:IMAGE", cancellationToken);
                return [.. Array(document.RootElement, "items").Select(ParseEuropeanaResult)];
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    public async Task<ArtworkImport> ImportAsync(ArtworkSearchResult result, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var reporter = new ImportProgressReporter(progress, importSteps: ImportSteps);
        reporter.Step(1, "Lettura della scheda del museo…");
        ArtworkImport import;
        string museumQid;
        switch (result.Fonte)
        {
            case ArtworkSource.Metropolitan:
                using (var document = await GetJsonAsync($"{MetApi}/objects/{Uri.EscapeDataString(result.Id)}", cancellationToken))
                {
                    import = ParseMetObject(document.RootElement);
                }

                museumQid = MetQid;
                break;
            case ArtworkSource.ArtInstitute:
                using (var document = await GetJsonAsync($"{AicApi}/artworks/{Uri.EscapeDataString(result.Id)}?fields={AicFields}", cancellationToken))
                {
                    import = ParseAicArtwork(document.RootElement);
                }

                museumQid = AicQid;
                break;
            case ArtworkSource.Cleveland:
                using (var document = await GetJsonAsync($"{ClevelandApi}/artworks/{Uri.EscapeDataString(result.Id)}", cancellationToken))
                {
                    import = ParseClevelandArtwork(document.RootElement.GetPropertyOrDefault("data"));
                }

                museumQid = ClevelandQid;
                break;
            case ArtworkSource.Europeana:
                var query = Uri.EscapeDataString($"europeana_id:\"{result.Id}\"");
                using (var document = await GetJsonAsync($"{EuropeanaApi}?wskey={Uri.EscapeDataString(EuropeanaKey)}&query={query}&rows=1&profile=rich", cancellationToken))
                {
                    import = Array(document.RootElement, "items").Select(ParseEuropeanaItem).FirstOrDefault()
                        ?? throw new InvalidOperationException("Opera non trovata su Europeana.");
                }

                museumQid = string.Empty;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }

        reporter.Step(2, "Ricerca degli artisti su Wikidata…");
        var people = new List<ArtworkImportPerson>();
        foreach (var person in import.Persone)
        {
            var qid = person.WikidataId;
            if (qid.Length == 0)
            {
                try
                {
                    qid = await wikipedia.FindHumanAsync(person.Nome, person.AnnoNascita, person.AnnoMorte, cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
                {
                    // Senza Wikidata l'artista si abbina solo per nome.
                }
            }

            people.Add(person with { WikidataId = qid });
        }

        reporter.Step(3, "Lettura della collocazione…");
        HistoricalPlaceRecord? place = null;
        if (museumQid.Length > 0)
        {
            try
            {
                place = await wikipedia.ReadPlaceByIdAsync(museumQid, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                // Resta il nome del museo nel campo Collocazione del messaggio.
            }
        }

        reporter.Step(4, "Composizione della scheda…");
        return new ArtworkImport
        {
            Titolo = import.Titolo,
            Tipo = import.Tipo,
            Data = import.Data,
            Tecnica = import.Tecnica,
            Dimensioni = import.Dimensioni,
            Note = import.Note,
            WikidataId = import.WikidataId,
            Fonti = import.Fonti,
            Persone = people,
            Luogo = place,
            Collocazione = import.Collocazione,
            ImmagineUrl = import.ImmagineUrl,
        };
    }

    // Scarica l'immagine in un file temporaneo e ne salva una copia compressa nella cartella dell'archivio.
    public static async Task<string> DownloadImageAsync(string url, string directory, CancellationToken cancellationToken = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"AlberiStorici-{Guid.NewGuid():N}.img");
        try
        {
            using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumImageBytes)
                {
                    throw new InvalidOperationException("L’immagine online è troppo grande per essere scaricata.");
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(temp);
                await input.CopyToAsync(output, cancellationToken);
            }

            return await Task.Run(() => ArtworkImages.Import(temp, directory), cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Il file temporaneo verrà eliminato da Windows.
            }
        }
    }

    internal static ArtworkSearchResult? ParseMetResult(JsonElement o)
    {
        var id = Number(o, "objectID");
        var title = Str(o, "title");
        return id.Length == 0 || title.Length == 0
            ? null
            : new ArtworkSearchResult(ArtworkSource.Metropolitan, id, title, Join(Join(Str(o, "artistPrefix"), Str(o, "artistDisplayName"), " "), Str(o, "objectDate")));
    }

    internal static ArtworkImport ParseMetObject(JsonElement o)
    {
        var people = new List<ArtworkImportPerson>();
        var mainName = Str(o, "artistDisplayName");
        if (mainName.Length > 0 && ArtworkText.MapArtistRole(Str(o, "artistPrefix")) is { } mainRole)
        {
            people.Add(new ArtworkImportPerson(WikidataIdFromUrl(Str(o, "artistWikidata_URL")), mainName, mainRole)
            {
                AnnoNascita = Year(Str(o, "artistBeginDate")),
                AnnoMorte = Year(Str(o, "artistEndDate")),
            });
        }

        foreach (var c in Array(o, "constituents"))
        {
            var name = Str(c, "name");
            if (name.Length == 0 || string.Equals(name, mainName, StringComparison.OrdinalIgnoreCase) || ArtworkText.MapArtistRole(Str(c, "role")) is not { } role)
            {
                continue;
            }

            people.Add(new ArtworkImportPerson(WikidataIdFromUrl(Str(c, "constituentWikidata_URL")), name, role));
        }

        var publicDomain = o.GetPropertyOrDefault("isPublicDomain").ValueKind == JsonValueKind.True;
        var image = publicDomain ? FirstNonEmpty(Str(o, "primaryImage"), Str(o, "primaryImageSmall")) : string.Empty;
        var sources = new List<string> { Str(o, "objectURL") };
        if (image.Length > 0)
        {
            sources.Add("Immagine: The Metropolitan Museum of Art, Open Access (pubblico dominio, CC0)");
        }

        return new ArtworkImport
        {
            Titolo = Str(o, "title"),
            Tipo = ArtworkText.TranslateType(FirstNonEmpty(Str(o, "objectName"), Str(o, "classification"))),
            Data = ArtworkText.TranslateDate(Str(o, "objectDate")),
            Tecnica = ArtworkText.TranslateMedium(Str(o, "medium")),
            Dimensioni = ArtworkText.ExtractMetricDimensions(Str(o, "dimensions")),
            Note = Join(Str(o, "creditLine"), Str(o, "culture"), ". "),
            WikidataId = WikidataIdFromUrl(Str(o, "objectWikidata_URL")),
            Fonti = [.. sources.Where(s => s.Length > 0)],
            Persone = people,
            Collocazione = "The Metropolitan Museum of Art, New York",
            ImmagineUrl = image,
        };
    }

    // Risposta di /artworks/{id}: i dati in «data», l'indirizzo IIIF delle immagini in «config».
    internal static ArtworkImport ParseAicArtwork(JsonElement root)
    {
        var d = root.GetPropertyOrDefault("data");
        var iiif = FirstNonEmpty(Str(root.GetPropertyOrDefault("config"), "iiif_url"), "https://www.artic.edu/iiif/2");
        var people = new List<ArtworkImportPerson>();
        var display = Str(d, "artist_display");
        var (parsedName, birth, death) = ArtworkText.ParseArtistDisplay(display);
        var artist = FirstNonEmpty(Str(d, "artist_title"), parsedName);
        if (artist.Length > 0)
        {
            people.Add(new ArtworkImportPerson(string.Empty, artist, display.StartsWith("Attributed", StringComparison.OrdinalIgnoreCase) ? OperaRuoli.Attribuita : OperaRuoli.Autore)
            {
                AnnoNascita = birth,
                AnnoMorte = death,
            });
        }

        var imageId = Str(d, "image_id");
        var publicDomain = d.GetPropertyOrDefault("is_public_domain").ValueKind == JsonValueKind.True;
        var image = publicDomain && imageId.Length > 0 ? $"{iiif}/{imageId}/full/843,/0/default.jpg" : string.Empty;
        var sources = new List<string> { $"https://www.artic.edu/artworks/{Number(d, "id")}" };
        if (image.Length > 0)
        {
            sources.Add("Immagine: Art Institute of Chicago (pubblico dominio, CC0)");
        }

        return new ArtworkImport
        {
            Titolo = Str(d, "title"),
            Tipo = ArtworkText.TranslateType(Str(d, "artwork_type_title")),
            Data = ArtworkText.TranslateDate(Str(d, "date_display")),
            Tecnica = ArtworkText.TranslateMedium(Str(d, "medium_display")),
            Dimensioni = ArtworkText.ExtractMetricDimensions(Str(d, "dimensions")),
            Note = Str(d, "credit_line"),
            Fonti = sources,
            Persone = people,
            Collocazione = "Art Institute of Chicago",
            ImmagineUrl = image,
        };
    }

    internal static ArtworkImport ParseClevelandArtwork(JsonElement d)
    {
        var people = new List<ArtworkImportPerson>();
        foreach (var c in Array(d, "creators"))
        {
            var (name, birth, death) = ArtworkText.ParseArtistDisplay(Str(c, "description"));
            var role = ArtworkText.MapArtistRole(FirstNonEmpty(Str(c, "qualifier"), Str(c, "role")));
            if (name.Length > 0 && role is not null)
            {
                people.Add(new ArtworkImportPerson(string.Empty, name, role)
                {
                    AnnoNascita = Year(Str(c, "birth_year")) ?? birth,
                    AnnoMorte = Year(Str(c, "death_year")) ?? death,
                });
            }
        }

        var images = d.GetPropertyOrDefault("images");
        var cc0 = string.Equals(Str(d, "share_license_status"), "CC0", StringComparison.OrdinalIgnoreCase);
        var image = cc0 ? FirstNonEmpty(Str(images.GetPropertyOrDefault("print"), "url"), Str(images.GetPropertyOrDefault("web"), "url")) : string.Empty;
        var sources = new List<string> { Str(d, "url") };
        if (image.Length > 0)
        {
            sources.Add("Immagine: Cleveland Museum of Art, Open Access (CC0)");
        }

        return new ArtworkImport
        {
            Titolo = Str(d, "title"),
            Tipo = ArtworkText.TranslateType(Str(d, "type")),
            Data = ArtworkText.TranslateDate(Str(d, "creation_date")),
            Tecnica = ArtworkText.TranslateMedium(Str(d, "technique")),
            Dimensioni = ArtworkText.ExtractMetricDimensions(Str(d, "measurements")),
            Note = Str(d, "credit_line"),
            Fonti = [.. sources.Where(s => s.Length > 0)],
            Persone = people,
            Collocazione = "Cleveland Museum of Art",
            ImmagineUrl = image,
        };
    }

    internal static ArtworkSearchResult ParseEuropeanaResult(JsonElement item)
    {
        var import = ParseEuropeanaItem(item);
        var detail = Join(Join(string.Join(", ", import.Persone.Select(p => p.Nome)), import.Data), import.Collocazione);
        return new ArtworkSearchResult(ArtworkSource.Europeana, Str(item, "id"), import.Titolo.Length > 0 ? import.Titolo : Str(item, "id"), detail);
    }

    internal static ArtworkImport ParseEuropeanaItem(JsonElement item)
    {
        var creators = Strings(item, "dcCreator").Where(c => !c.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToList();
        if (creators.Count == 0)
        {
            creators = [.. Array(item, "edmAgentLabel").Select(l => Str(l, "def")).Where(l => l.Length > 0)];
        }

        var people = creators
            .Select(FlipName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new ArtworkImportPerson(string.Empty, name, OperaRuoli.Autore))
            .ToList();
        var types = item.GetPropertyOrDefault("dcTypeLangAware");
        var tipo = LangAware(types, "it") is { Length: > 0 } italian
            ? char.ToUpper(italian[0], CultureInfo.CurrentCulture) + italian[1..]
            : ArtworkText.TranslateType(LangAware(types, "en"));
        var titles = item.GetPropertyOrDefault("dcTitleLangAware");
        var title = FirstNonEmpty(LangAware(titles, "it"), LangAware(titles, "en"), Strings(item, "title").FirstOrDefault() ?? string.Empty);
        var description = FirstNonEmpty(LangAware(item.GetPropertyOrDefault("dcDescriptionLangAware"), "it"), Strings(item, "dcDescription").FirstOrDefault() ?? string.Empty);
        var image = FirstNonEmpty(Strings(item, "edmIsShownBy").FirstOrDefault() ?? string.Empty, Strings(item, "edmPreview").FirstOrDefault() ?? string.Empty);
        var page = Regex.Replace(Str(item, "guid"), @"\?.*$", string.Empty);
        var rights = Strings(item, "rights").FirstOrDefault() ?? string.Empty;
        var sources = new List<string> { page, Strings(item, "edmIsShownAt").FirstOrDefault() ?? string.Empty };
        if (image.Length > 0 && rights.Length > 0)
        {
            sources.Add($"Licenza dell’immagine: {rights}");
        }

        return new ArtworkImport
        {
            Titolo = title,
            Tipo = tipo,
            Data = ArtworkText.TranslateDate(Strings(item, "year").FirstOrDefault()),
            Note = description.Length > 600 ? WikipediaImportService.SummarizeExtract(description) : description,
            Fonti = [.. sources.Where(s => s.Length > 0).Distinct()],
            Persone = people,
            Collocazione = Join(Strings(item, "dataProvider").FirstOrDefault() ?? string.Empty, Strings(item, "country").FirstOrDefault() ?? string.Empty),
            ImmagineUrl = image,
        };
    }

    // «Botticelli, Sandro» → «Sandro Botticelli»; i nomi con più virgole restano invariati.
    internal static string FlipName(string name)
    {
        var parts = name.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && !parts[1].Any(char.IsDigit) ? $"{parts[1]} {parts[0]}" : name.Trim();
    }

    internal static string WikidataIdFromUrl(string url)
    {
        var match = Regex.Match(url ?? string.Empty, @"(Q\d+)\s*$");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static int? Year(string text)
        => int.TryParse(Regex.Match(text ?? string.Empty, @"-?\d{1,4}").Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var year) && year != 0 ? year : null;

    private static string LangAware(JsonElement element, string language)
        => element.GetPropertyOrDefault(language) is { ValueKind: JsonValueKind.Array } values
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? string.Empty).FirstOrDefault(v => v.Length > 0 && !v.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? string.Empty
            : string.Empty;

    private static IEnumerable<JsonElement> Array(JsonElement element, string name)
        => element.GetPropertyOrDefault(name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    private static IEnumerable<string> Strings(JsonElement element, string name)
        => Array(element, name).Where(v => v.ValueKind == JsonValueKind.String).Select(v => (v.GetString() ?? string.Empty).Trim()).Where(v => v.Length > 0);

    private static string Str(JsonElement element, string name)
        => element.GetPropertyOrDefault(name) is { ValueKind: JsonValueKind.String } value ? (value.GetString() ?? string.Empty).Trim() : string.Empty;

    private static string Number(JsonElement element, string name)
        => element.GetPropertyOrDefault(name) is { ValueKind: JsonValueKind.Number } value ? value.GetRawText() : Str(element, name);

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => v.Length > 0) ?? string.Empty;

    private static string Join(string first, string second, string separator = " · ")
        => first.Length > 0 && second.Length > 0 ? $"{first}{separator}{second}" : first + second;

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AlberiStorici/1.0 (github.com/msonzo1/HistoricEditor)");
        // L'Art Institute chiede di identificarsi anche con questa intestazione.
        client.DefaultRequestHeaders.TryAddWithoutValidation("AIC-User-Agent", "AlberiStorici (github.com/msonzo1/HistoricEditor)");
        return client;
    }
}
