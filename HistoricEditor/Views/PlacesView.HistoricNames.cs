using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace HistoricEditor;

// Riga modificabile dei nomi storici: gli anni sono testo («667 a.C.») e vengono letti al salvataggio.
public sealed class HistoricNameRow
{
    public string Nome { get; set; } = string.Empty;
    public string Dal { get; set; } = string.Empty;
    public string Al { get; set; } = string.Empty;
    public string Fonte { get; set; } = string.Empty;
    public string Lingua { get; set; } = string.Empty;

    public static HistoricNameRow From(NomeStorico name) => new()
    {
        Nome = name.Nome,
        Dal = name.Dal is int dal ? HistoricalPlaceNames.FormatYear(dal) : string.Empty,
        Al = name.Al is int al ? HistoricalPlaceNames.FormatYear(al) : string.Empty,
        Fonte = name.Fonte,
        Lingua = name.Lingua,
    };
}

public partial class PlacesView
{
    private readonly ObservableCollection<HistoricNameRow> historicNames = [];
    private readonly WikipediaImportService wikipediaService = new();
    private CancellationTokenSource? wikidataCancellation;

    private void LoadHistoricNames(IEnumerable<NomeStorico> names)
    {
        historicNames.Clear();
        foreach (var name in names)
        {
            historicNames.Add(HistoricNameRow.From(name));
        }

        HistoricNamesGrid.ItemsSource = historicNames;
    }

    // Null se un anno non è valido (il messaggio indica la riga).
    private List<NomeStorico>? ReadHistoricNames()
    {
        HistoricNamesGrid.CommitEdit();
        HistoricNamesGrid.CommitEdit();
        var result = new List<NomeStorico>();
        foreach (var row in historicNames.Where(r => r.Nome.Trim().Length > 0))
        {
            if (!HistoricalPlaceNames.TryParseYear(row.Dal, out var dal) || !HistoricalPlaceNames.TryParseYear(row.Al, out var al))
            {
                Feedback.Error(ManagerMessage, $"Anno non valido per «{row.Nome.Trim()}»: scrivi ad esempio 330, 667 a.C. o -667.");
                return null;
            }

            if (dal is int from && al is int to && from > to)
            {
                Feedback.Error(ManagerMessage, $"Per «{row.Nome.Trim()}» l’anno iniziale viene dopo quello finale.");
                return null;
            }

            result.Add(new NomeStorico { Nome = row.Nome.Trim(), Dal = dal, Al = al, Fonte = row.Fonte.Trim(), Lingua = row.Lingua });
        }

        return result;
    }

    private void AddHistoricName_Click(object sender, RoutedEventArgs e)
    {
        var row = new HistoricNameRow { Fonte = "Inserito a mano" };
        historicNames.Add(row);
        HistoricNamesGrid.SelectedItem = row;
        HistoricNamesGrid.ScrollIntoView(row);
        HistoricNamesGrid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(row, HistoricNamesGrid.Columns[0]);
        HistoricNamesGrid.Focus();
        HistoricNamesGrid.BeginEdit();
    }

    private void RemoveHistoricName_Click(object sender, RoutedEventArgs e)
    {
        if (HistoricNamesGrid.SelectedItem is HistoricNameRow row)
        {
            HistoricNamesGrid.CancelEdit();
            historicNames.Remove(row);
        }
    }

    private async void WikidataNames_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || PlacesTree.SelectedNode is not { Source: not null } selected || store.GetPlace(selected.Id) is not { } luogo)
        {
            return;
        }

        wikidataCancellation?.Cancel();
        var cancellation = wikidataCancellation = new CancellationTokenSource();
        WikidataNamesButton.IsEnabled = false;
        Feedback.Info(ManagerMessage, $"Ricerca su Wikidata dei nomi storici di «{luogo.Nome}»…");
        try
        {
            var found = await wikipediaService.ReadPlaceHistoricalNamesAsync(luogo.WikidataId, luogo.Nome, cancellation.Token);
            if (cancellation.IsCancellationRequested || PlacesTree.SelectedNode?.Id != selected.Id)
            {
                return;
            }

            if (found is null)
            {
                Feedback.Error(ManagerMessage, $"Nessun luogo «{luogo.Nome}» trovato su Wikidata.");
                return;
            }

            if (found.Names.Count == 0)
            {
                Feedback.Info(ManagerMessage, $"Wikidata non indica altri nomi per «{found.Label}».");
                return;
            }

            // Se il luogo non era già collegato, si chiede conferma che la voce trovata sia quella giusta.
            if (luogo.WikidataId.Length == 0 && Owner is { } owner)
            {
                var description = found.Description.Length > 0 ? $" ({found.Description})" : string.Empty;
                var list = string.Join("\n", found.Names.Select(n => $"• {n.Nome} {HistoricalPlaceNames.FormatPeriod(n)}"));
                var answer = MessageBox.Show(owner, $"Trovato su Wikidata: «{found.Label}»{description}.\n\nNomi storici:\n{list}\n\nAggiungerli al luogo?",
                    "Nomi storici da Wikidata", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    Feedback.Info(ManagerMessage, "Nessun nome aggiunto.");
                    return;
                }
            }

            // Si uniscono ai nomi già nella griglia (anche se non ancora salvati), poi si salva solo questo elenco.
            var current = ReadHistoricNames() ?? luogo.NomiStorici;
            var merged = HistoricalPlaceNames.Merge(current, found.Names, luogo.Nome);
            store.SetPlaceHistoricNames(luogo.Id, merged);
            if (luogo.WikidataId.Length == 0)
            {
                store.MergePlaceHistoricNames(luogo.Id, [], found.WikidataId);
            }

            LoadHistoricNames(merged);
            PlacesChanged?.Invoke(this, EventArgs.Empty);
            Feedback.Success(ManagerMessage, $"Nomi storici aggiornati da Wikidata ({found.Names.Count} trovati).");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            Feedback.Error(ManagerMessage, "Impossibile leggere i nomi storici da Wikidata. Controlla la connessione.");
        }
        finally
        {
            if (wikidataCancellation == cancellation)
            {
                WikidataNamesButton.IsEnabled = true;
            }
        }
    }

    private void Attachments_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || PlacesTree.SelectedNode is not { Source: not null } selected)
        {
            return;
        }

        var window = new AttachmentsWindow(store, AmbitiAllegato.Luogo, selected.Id, selected.Nome) { Owner = Owner };
        window.ShowDialog();
        if (window.Changed)
        {
            AttachmentsView.Load(store, AmbitiAllegato.Luogo, selected.Id);
            PlacesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
