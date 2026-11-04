using System.Windows;

namespace HistoricEditor;

// Controllo delle schede già presenti in archivio, per non creare doppioni dello stesso personaggio.
public partial class PersonEditorWindow
{
    // Schede che l'utente ha già dichiarato diverse da questa: non vengono riproposte.
    private readonly HashSet<int> acknowledgedDuplicates = [];
    private bool closingToOpenExisting;

    // Scheda già presente che l'utente ha scelto di aprire al posto di questa.
    public int? OpenExistingId { get; private set; }

    private List<PersonDuplicate> FindDuplicates(HistoricalPerson candidate, bool onlyCertain)
    {
        try
        {
            return [.. store.FindPossibleDuplicates(candidate, editingPerson?.Id ?? 0)
                .Where(d => (!onlyCertain || d.Certa) && !acknowledgedDuplicates.Contains(d.Id))];
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return [];
        }
    }

    private static string DescribeDuplicates(IEnumerable<PersonDuplicate> duplicates)
        => string.Join(Environment.NewLine, duplicates.Take(5).Select(d => $"• {d.Descrizione}"));

    // Prima del salvataggio: per le schede nuove si cercano anche gli omonimi, per quelle esistenti solo i doppioni certi.
    private bool ConfirmNoDuplicates(HistoricalPerson person)
    {
        var duplicates = FindDuplicates(person, onlyCertain: editingPerson is not null);
        if (duplicates.Count == 0)
        {
            return true;
        }

        var answer = MessageBox.Show(
            this,
            $"In archivio ci sono già schede che potrebbero essere lo stesso personaggio:{Environment.NewLine}{Environment.NewLine}{DescribeDuplicates(duplicates)}{Environment.NewLine}{Environment.NewLine}Salvare comunque questa scheda?",
            "Possibile doppione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            Feedback.Info(EditorMessage, "Salvataggio annullato: la scheda potrebbe essere un doppione.");
            return false;
        }

        acknowledgedDuplicates.UnionWith(duplicates.Select(d => d.Id));
        return true;
    }

    // Dopo l'import: se la stessa voce o lo stesso elemento Wikidata è già in archivio si propone di aprire quella scheda.
    // Restituisce false se l'import non va applicato.
    private bool ConfirmImportNotDuplicate(WikipediaImportResult import)
    {
        var candidate = new HistoricalPerson
        {
            Name = import.Name,
            Surname = import.Surname,
            Suffix = import.Suffix,
            WikidataId = import.WikidataId,
            WikipediaTitle = import.ArticleTitle,
        };
        var duplicates = FindDuplicates(candidate, onlyCertain: true);
        if (duplicates.Count == 0)
        {
            return true;
        }

        var answer = MessageBox.Show(
            this,
            $"Questo personaggio è già in archivio:{Environment.NewLine}{Environment.NewLine}{DescribeDuplicates(duplicates)}{Environment.NewLine}{Environment.NewLine}Sì: apri la scheda esistente.{Environment.NewLine}No: importa comunque in una nuova scheda.{Environment.NewLine}Annulla: non importare.",
            "Personaggio già presente",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);
        switch (answer)
        {
            case MessageBoxResult.Yes:
                OpenExistingId = duplicates[0].Id;
                closingToOpenExisting = true;
                DialogResult = Saved;
                return false;
            case MessageBoxResult.No:
                acknowledgedDuplicates.UnionWith(duplicates.Select(d => d.Id));
                return true;
            default:
                Feedback.Info(WikipediaStatus, "Importazione annullata: il personaggio è già in archivio.");
                return false;
        }
    }

    // Avviso immediato quando il risultato scelto corrisponde a una voce già importata.
    private string ExistingArticleNotice(string title)
    {
        var duplicate = FindDuplicates(new HistoricalPerson { WikipediaTitle = title }, onlyCertain: true).FirstOrDefault();
        return duplicate is null ? string.Empty : $" Attenzione: «{title}» è già in archivio ({duplicate.Etichetta}).";
    }
}
