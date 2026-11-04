using System.IO;
using System.Text.Json;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class ImportedPersonCreatorTests
{
    private static string Statement(string id) => "{\"rank\":\"normal\",\"mainsnak\":{\"datavalue\":{\"value\":{\"id\":\"" + id + "\"}}}}";

    [Fact]
    public void ReadEventIds_CombinesSignificantEventsParticipationAndConflicts()
    {
        using var document = JsonDocument.Parse($$"""
            {"P793":[{{Statement("Q1")}}],"P1344":[{{Statement("Q2")}},{{Statement("Q1")}}],"P607":[{{Statement("Q3")}}]}
            """);

        Assert.Equal(["Q1", "Q2", "Q3"], WikipediaImportService.ReadEventIds(document.RootElement));
    }

    [Fact]
    public void IsDatedEvent_RejectsClassesAndUndatedItems()
    {
        const string date = """{"rank":"normal","mainsnak":{"datavalue":{"value":{"time":"+1521-01-28T00:00:00Z","precision":11}}}}""";
        using var dated = JsonDocument.Parse($$"""{"P580":[{{date}}]}""");
        using var generic = JsonDocument.Parse($$"""{"P279":[{{Statement("Q209715")}}],"P585":[{{date}}]}""");
        using var undated = JsonDocument.Parse($$"""{"P31":[{{Statement("Q1190554")}}]}""");

        Assert.True(WikipediaImportService.IsDatedEvent(dated.RootElement));
        Assert.False(WikipediaImportService.IsDatedEvent(generic.RootElement));
        Assert.False(WikipediaImportService.IsDatedEvent(undated.RootElement));
    }

    [Fact]
    public void BuildPerson_MapsImportLikeTheForm()
    {
        var person = ImportedPersonCreator.BuildPerson(new WikipediaImportResult
        {
            Name = "Carlo",
            Suffix = "V",
            Family = "Asburgo",
            Sesso = Sessi.Maschio,
            WikidataId = "Q32500",
            ArticleTitle = "Carlo V d'Asburgo",
            ArticleUrl = "https://it.wikipedia.org/wiki/Carlo_V",
            BirthDates =
            [
                new HistoricalDateEvidence { Value = "24 febbraio 1500", Precision = "giorno", Calendario = HistoricalCalendar.Giuliano },
                new HistoricalDateEvidence { Value = "24 febbraio 1500" },
            ],
            Titles = [new PersonTitle { Titolo = "Imperatore" }, new PersonTitle()],
        });

        Assert.Equal("Carlo V", person.DisplayName);
        Assert.Equal("24 febbraio 1500", person.Birth);
        Assert.Equal(HistoricalCalendar.Giuliano, person.BirthCalendario);
        Assert.Equal(HistoricalCalendar.NonIndicato, person.DeathCalendario);
        Assert.Equal("Q32500", person.WikidataId);
        Assert.Equal("Carlo V d'Asburgo", person.WikipediaTitle);
        Assert.Equal("Imperatore", Assert.Single(person.Titles).Titolo);
        Assert.NotEmpty(person.BirthDateEvidence);
    }

    [Fact]
    public void Create_LinksRelativesFromEitherSide_AndListsOnlyMissingOnes()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-imp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var carloImport = new WikipediaImportResult
            {
                Name = "Carlo", Suffix = "V", Sesso = Sessi.Maschio, Family = "Asburgo", WikidataId = "Q32500",
                FatherIds = ["Q157098"], MotherIds = ["Q171136"], ChildIds = ["Q34417", "Q32500"],
            };
            var (carlo, unresolved) = ImportedPersonCreator.Create(store, carloImport);
            Assert.Equal(0, unresolved);
            Assert.Equal(
                [new MissingRelative("Q157098", "padre"), new MissingRelative("Q171136", "madre"), new MissingRelative("Q34417", "figlio o figlia")],
                ImportedPersonCreator.MissingRelatives(store, carloImport));

            // Il padre importato dopo si collega attraverso i propri figli; il figlio attraverso il proprio padre.
            var (filippo, _) = ImportedPersonCreator.Create(store, new WikipediaImportResult
            {
                Name = "Filippo", Suffix = "I", Sesso = Sessi.Maschio, Family = "Asburgo", WikidataId = "Q157098", ChildIds = ["Q32500"],
            });
            var (figlio, _) = ImportedPersonCreator.Create(store, new WikipediaImportResult
            {
                Name = "Filippo", Suffix = "II", Sesso = Sessi.Maschio, WikidataId = "Q34417", FatherIds = ["Q32500"],
            });

            Assert.Equal((null, filippo.Id), store.GetParents(carlo.Id));
            Assert.Equal((null, carlo.Id), store.GetParents(figlio.Id));
            Assert.Equal([new MissingRelative("Q171136", "madre")], ImportedPersonCreator.MissingRelatives(store, carloImport));
            Assert.Contains(store.GetDynasties(), d => d.Nome == "Asburgo");
            Assert.Equal("Asburgo", store.GetPerson(figlio.Id)!.Family);
            Assert.Equal(3, store.GetDynastyLinks().Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LinkRelatives_ChildWithoutFamilyInheritsFatherFamily_EvenIfImportedFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-imp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var (taddea, _) = ImportedPersonCreator.Create(store, new WikipediaImportResult
            {
                Name = "Taddea", Sesso = Sessi.Femmina, WikidataId = "Q6137930", FatherIds = ["Q32500"],
            });
            var (margherita, _) = ImportedPersonCreator.Create(store, new WikipediaImportResult
            {
                Name = "Margherita", Sesso = Sessi.Femmina, Family = "Farnese", WikidataId = "Q233311", FatherIds = ["Q32500"],
            });
            Assert.Equal(string.Empty, store.GetPerson(taddea.Id)!.Family);

            ImportedPersonCreator.Create(store, new WikipediaImportResult
            {
                Name = "Carlo", Suffix = "V", Sesso = Sessi.Maschio, Family = "Asburgo di Spagna; Casa d'Asburgo", WikidataId = "Q32500",
                ChildIds = ["Q6137930", "Q233311"],
            });

            Assert.Equal("Asburgo di Spagna; Casa d'Asburgo", store.GetPerson(taddea.Id)!.Family);
            Assert.Equal("Farnese", store.GetPerson(margherita.Id)!.Family);
            Assert.Equal(2, store.GetDynastyLinks().Count(l => l.PersonaId == taddea.Id));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ImportedEvents_LinkOnlyArchivedParticipants_AndAreReusedByWikidataId()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-imp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var carlo = store.CreatePerson("Carlo V", Sessi.Maschio);
            var lutero = store.CreatePerson("Martin Lutero", Sessi.Maschio);
            var luteroCard = store.GetPerson(lutero)!;
            luteroCard.WikidataId = "Q9554";
            store.Update(luteroCard);
            var import = new WikipediaEventImport
            {
                Titolo = "Dieta di Worms",
                Tipo = "Concilio",
                DataQualificatore = DataEvento.DaA,
                Data1 = "28/01/1521",
                Data2 = "25/05/1521",
                WikidataId = "Q536822",
                Partecipanti = [new WikipediaEventParticipant("Q9554", "Martin Lutero", "imputato"), new WikipediaEventParticipant("Q1", "Sconosciuto", "partecipante")],
            };

            var id = ImportedEventCreator.Save(store, import, carlo);

            var evento = Assert.Single(store.GetEvents());
            Assert.Equal(id, evento.Id);
            Assert.Equal(
                [(carlo, "partecipante"), (lutero, "imputato")],
                evento.Partecipanti.OrderBy(p => p.PersonaId).Select(p => (p.PersonaId, p.Ruolo)));
            Assert.Equal(id, store.FindEventIdByWikidataId("Q536822"));

            var altro = store.CreatePerson("Federico III di Sassonia", Sessi.Maschio);
            Assert.True(store.AddEventParticipant(evento.Id, altro, "partecipante"));
            Assert.False(store.AddEventParticipant(evento.Id, altro, "partecipante"));
            Assert.Equal(3, store.GetEvents().Single().Partecipanti.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
