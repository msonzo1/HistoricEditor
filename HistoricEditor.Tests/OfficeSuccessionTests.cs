using System.IO;
using HistoricEditor;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HistoricEditor.Tests;

public class OfficeSuccessionTests
{
    private static HistoricalPerson Person(int id, string name, string wikidataId, params PersonTitle[] titles)
        => new() { Id = id, Name = name, WikidataId = wikidataId, Titles = [.. titles] };

    private static PersonTitle Re(string start, string end, string predecessor = "", string predecessorId = "", string successor = "", string successorId = "")
        => new()
        {
            Titolo = "Re di Francia",
            WikidataId = "Q18384454",
            Inizio = start,
            Fine = end,
            Predecessore = predecessor,
            PredecessoreWikidataId = predecessorId,
            Successore = successor,
            SuccessoreWikidataId = successorId,
        };

    [Fact]
    public void Build_OrdersHoldersAndLinksPredecessorsAndSuccessors()
    {
        var people = new[]
        {
            Person(3, "Enrico II", "Q3", Re("31 marzo 1547", "10 luglio 1559", "Francesco I", "Q2")),
            Person(1, "Luigi XII", "Q1", Re("1498", "1 gennaio 1515", successor: "Francesco I", successorId: "Q2")),
            // Titolo scritto a mano, senza codice: si unisce alla carica con lo stesso nome.
            Person(2, "Francesco I", "Q2", new PersonTitle { Titolo = "re di  Francia", Numerazione = "I", Inizio = "1515", Fine = "1547" }),
            Person(4, "Carlo V", "Q4", new PersonTitle { Titolo = "Imperatore", Inizio = "1519", Fine = "1556" }),
        };

        var offices = OfficeSuccession.Build(people);

        Assert.Equal(["Imperatore", "Re di Francia"], offices.Select(o => o.Nome));
        var france = offices[1];
        Assert.Equal("Q18384454", france.WikidataId);
        Assert.Equal(["Luigi XII", "Francesco I", "Enrico II"], france.Righe.Select(r => r.Mandato.Persona));
        var francesco = france.Righe[1];
        Assert.Equal("Francesco I (I)", francesco.Titolare);
        Assert.Equal("Luigi XII", francesco.Predecessore);
        Assert.Equal("Enrico II", francesco.Successore);
        Assert.All(france.Righe, r => Assert.Empty(r.Note));
        Assert.Empty(france.Mancanti);
        Assert.Equal("Re di Francia (3 titolari, 1498–1559)", france.Etichetta);
        Assert.Equal("—", france.Righe[0].Predecessore);
    }

    [Fact]
    public void Build_ReportsGapsOverlapsMismatchesAndMissingHolders()
    {
        var people = new[]
        {
            Person(1, "Carlo VIII", "Q10", Re("1483", "1498", "Luigi XI", "Q9", "Luigi XII", "Q11")),
            Person(2, "Francesco I", "Q2", Re("1510", "1547")),
            Person(3, "Enrico II", "Q3", Re("1545", "1559")),
            Person(4, "Senza date", "", Re("", "")),
            // In archivio ma senza la carica.
            Person(5, "Luigi XII", "Q11"),
        };

        var france = Assert.Single(OfficeSuccession.Build(people));

        var rows = france.Righe;
        Assert.Equal("Senza date", rows[^1].Mandato.Persona);
        Assert.Contains("Date mancanti: posizione incerta", rows[^1].Note);
        Assert.Contains("Successore indicato: Luigi XII", rows[0].Note);
        Assert.Equal("Luigi XI (non in cronologia)", rows[0].Predecessore);
        Assert.Contains("Carica vacante tra il 1498 e il 1510", rows[1].Note);
        Assert.Contains("Periodo sovrapposto a Francesco I (1545–1547)", rows[2].Note);

        Assert.Equal(2, france.Mancanti.Count);
        Assert.Null(france.Mancanti.Single(m => m.WikidataId == "Q9").PersonId);
        Assert.Equal(5, france.Mancanti.Single(m => m.WikidataId == "Q11").PersonId);
        Assert.EndsWith("in archivio, senza questa carica", france.Mancanti.Single(m => m.WikidataId == "Q11").Etichetta);
    }

    [Fact]
    public void Build_HandlesYearsBeforeChrist()
    {
        var people = new[]
        {
            Person(1, "Augusto", "", new PersonTitle { Titolo = "Imperatore romano", Inizio = "27 a.C.", Fine = "19 agosto 14 d.C." }),
            Person(2, "Tiberio", "", new PersonTitle { Titolo = "Imperatore romano", Inizio = "14 d.C.", Fine = "37 d.C." }),
        };

        var office = Assert.Single(OfficeSuccession.Build(people));

        Assert.Equal(["Augusto", "Tiberio"], office.Righe.Select(r => r.Mandato.Persona));
        Assert.Equal("Imperatore romano (2 titolari, 27 a.C.–37)", office.Etichetta);
    }

    [Fact]
    public void TitleMerger_KeepsPredecessorAndSuccessorFromEitherStatement()
    {
        var merged = TitleMerger.Merge(
        [
            new PersonTitle { Titolo = "Re di Francia", WikidataId = "Q1", Inizio = "1515", Predecessore = "Luigi XII", PredecessoreWikidataId = "Q11" },
            new PersonTitle { Titolo = "Re di Francia", WikidataId = "Q1", Inizio = "1515", Fine = "1547", Successore = "Enrico II", SuccessoreWikidataId = "Q3" },
        ], new Dictionary<string, HashSet<string>>());

        var title = Assert.Single(merged);
        Assert.Equal(("Luigi XII", "Q11"), (title.Predecessore, title.PredecessoreWikidataId));
        Assert.Equal(("Enrico II", "Q3"), (title.Successore, title.SuccessoreWikidataId));
    }

    [Fact]
    public void Store_LoadsTitlesSavedBeforeSuccessionFields()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-office-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var id = store.CreatePerson("Francesco I", Sessi.Maschio);
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(dir, "historic.db")}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE People SET Titles = $titles WHERE Id = $id";
                command.Parameters.AddWithValue("$titles", "[{\"Titolo\":\"Re di Francia\",\"Numerazione\":\"\",\"Inizio\":\"1515\",\"CausaInizio\":\"\",\"Fine\":\"1547\",\"CausaFine\":\"\",\"WikidataId\":\"\",\"FontiDate\":[]}]");
                command.Parameters.AddWithValue("$id", id);
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            var title = Assert.Single(store.GetPerson(id)!.Titles);
            Assert.Equal("Re di Francia", title.Titolo);
            Assert.Equal(string.Empty, title.Predecessore ?? string.Empty);

            title.Successore = "Enrico II";
            var person = store.GetPerson(id)!;
            person.Titles = [title];
            store.Update(person);
            Assert.Equal("Enrico II", Assert.Single(store.GetPerson(id)!.Titles).Successore);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OfficesWindow_OpensOnPersonOffice()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-office-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                store.Add(Person(0, "Luigi XII", "", Re("1498", "1515")));
                store.Add(Person(0, "Francesco I", "", new PersonTitle { Titolo = "Duca di Milano", Inizio = "1515", Fine = "1521" }, Re("1515", "1547")));
                var francesco = store.LoadAll().Single(p => p.Name == "Francesco I");
                francesco.MainTitle = "Re di Francia";
                store.Update(francesco);

                var window = new OfficesWindow(store, francesco.Id);
                var office = Assert.IsType<OfficeChronology>(window.OfficeList.SelectedItem);
                Assert.Equal("Re di Francia", office.Nome);
                Assert.Equal(francesco.Id, Assert.IsType<OfficeRow>(window.ChronologyGrid.SelectedItem).Mandato.PersonId);
                window.Close();
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
