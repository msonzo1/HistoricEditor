using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HistoricEditor;

namespace HistoricEditor.Tests;

// Import reali da Wikipedia/Wikidata con le risposte salvate su file, per non riscaricarle a ogni test.
// Le risposte di ogni personaggio stanno in Fixtures/Import/<titolo della pagina>/, un file per richiesta
// ({"url": ..., "response": ...}, nome = hash dell'URL).
//
// Per registrare un nuovo personaggio, o le richieste nuove dopo una modifica all'import, eseguire i test con
// la variabile d'ambiente HISTORIC_RECORD_IMPORT=1: si scaricano solo le risposte mancanti e, a import
// riuscito, si cancellano quelle non più usate. Per riscaricare tutto da capo, cancellare la cartella.
// Senza la variabile i test non usano mai la rete: una risposta mancante fa fallire il test con l'URL.
internal static class ImportFixtures
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<WikipediaImportResult>>> Imports = new(StringComparer.Ordinal);

    public static bool Recording => Environment.GetEnvironmentVariable("HISTORIC_RECORD_IMPORT") is "1" or "true";

    // Import della pagina it.wikipedia indicata; eseguito una volta sola per tutti i test che lo chiedono.
    public static Task<WikipediaImportResult> ImportAsync(string pageTitle)
        => Imports.GetOrAdd(pageTitle, title => new Lazy<Task<WikipediaImportResult>>(() => RunImportAsync(title))).Value;

    private static async Task<WikipediaImportResult> RunImportAsync(string pageTitle)
    {
        var store = new FileResponses(Path.Combine(FixturesRoot(), FolderName(pageTitle)), Recording);
        WikipediaImportService.RecordedResponses.Value = store;
        try
        {
            var result = await new WikipediaImportService().ImportAsync(new WikipediaSearchResult { Title = pageTitle });
            store.PruneUnused();
            return result;
        }
        finally
        {
            WikipediaImportService.RecordedResponses.Value = null;
        }
    }

    // Confronta il riepilogo completo dell'import con quello approvato in Fixtures/Import/<titolo>.atteso.txt.
    // Se differisce scrive <titolo>.ottenuto.txt accanto e fallisce elencando le righe cambiate. Con
    // HISTORIC_UPDATE_SNAPSHOTS=1 il riepilogo ottenuto diventa quello approvato (da rivedere con git diff).
    public static void AssertMatchesSnapshot(string pageTitle, WikipediaImportResult result)
    {
        var expectedPath = Path.Combine(FixturesRoot(), FolderName(pageTitle) + ".atteso.txt");
        var actualPath = Path.Combine(FixturesRoot(), FolderName(pageTitle) + ".ottenuto.txt");
        var actual = ImportSnapshot.Describe(result);
        var update = Environment.GetEnvironmentVariable("HISTORIC_UPDATE_SNAPSHOTS") is "1" or "true";
        if (update || !File.Exists(expectedPath))
        {
            File.WriteAllText(expectedPath, actual, new UTF8Encoding(false));
            File.Delete(actualPath);
            return;
        }

        var expected = File.ReadAllText(expectedPath).ReplaceLineEndings("\n");
        if (expected == actual)
        {
            File.Delete(actualPath);
            return;
        }

        File.WriteAllText(actualPath, actual, new UTF8Encoding(false));
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var differences = expectedLines.Except(actualLines).Select(l => "- " + l)
            .Concat(actualLines.Except(expectedLines).Select(l => "+ " + l))
            .Take(40);
        throw new Xunit.Sdk.XunitException(
            $"L'import di «{pageTitle}» è cambiato rispetto a {Path.GetFileName(expectedPath)} (- prima, + ora):\n"
            + string.Join('\n', differences)
            + $"\nSe il cambiamento è voluto, eseguire i test con HISTORIC_UPDATE_SNAPSHOTS=1 (confronto completo in {Path.GetFileName(actualPath)}).");
    }

    private static string FixturesRoot([CallerFilePath] string sourceFile = "")
        => Path.Combine(Path.GetDirectoryName(sourceFile)!, "Fixtures", "Import");

    private static string FolderName(string pageTitle)
        => string.Concat(pageTitle.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private sealed class FileResponses(string folder, bool recording) : WikipediaImportService.IRecordedResponses
    {
        private readonly ConcurrentDictionary<string, byte> used = new(StringComparer.OrdinalIgnoreCase);

        public string? Find(string url)
        {
            var path = PathOf(url);
            used[Path.GetFileName(path)] = 0;
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.GetProperty("response").GetRawText();
            }

            return recording
                ? null
                : throw new MissingImportFixtureException(
                    $"Risposta non registrata in {folder}: {url}. Eseguire i test con HISTORIC_RECORD_IMPORT=1 per scaricarla.");
        }

        public void Save(string url, string json)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(PathOf(url), $"{{\"url\":{JsonSerializer.Serialize(url)},\"response\":{json}}}", new UTF8Encoding(false));
        }

        // Dopo un import riuscito in registrazione: via le risposte di richieste che l'import non fa più.
        public void PruneUnused()
        {
            if (!recording || !Directory.Exists(folder))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(folder, "*.json").Where(f => !used.ContainsKey(Path.GetFileName(f))))
            {
                File.Delete(file);
            }
        }

        private string PathOf(string url)
            => Path.Combine(folder, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16] + ".json");
    }
}

// Non è tra gli errori «accessori» che l'import ignora: una risposta mancante fa sempre fallire il test.
internal sealed class MissingImportFixtureException(string message) : Exception(message);
