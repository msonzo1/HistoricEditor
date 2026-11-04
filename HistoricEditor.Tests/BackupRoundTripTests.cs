using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class BackupRoundTripTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Backup_RoundTrip_RestoresUnions()
    {
        var sourceDir = NewTempDir();
        var restoreDir = NewTempDir();
        try
        {
            using (var store = new PeopleStore(sourceDir))
            {
                var husbandId = store.CreatePerson("Giulio", Sessi.Maschio);
                var wifeId = store.CreatePerson("Clelia", Sessi.Femmina);
                store.SaveUnion(new Unione { Persona1Id = husbandId, Persona2Id = wifeId, Tipo = "Matrimonio", Inizio = "1500" });
                store.ExportJson(store.LoadAll());
            }

            // Un archivio nuovo deve poter ricostruire le unioni dal solo backup JSON.
            File.Copy(Path.Combine(sourceDir, "people.json"), Path.Combine(restoreDir, "people.json"));

            using var restored = new PeopleStore(restoreDir);
            var people = restored.LoadAll();
            Assert.Equal(2, people.Count);

            var giulio = people.Single(p => p.Name == "Giulio");
            var family = restored.GetFamily(giulio.Id);
            var unione = Assert.Single(family.Unioni);
            Assert.Equal("Clelia", unione.Coniuge?.Nome);
            Assert.Equal("Matrimonio", unione.Tipo);
            Assert.Equal("1500", unione.Inizio);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(restoreDir, recursive: true);
        }
    }

    [Fact]
    public void Backup_RoundTrip_RestoresLinkedPlaces()
    {
        var sourceDir = NewTempDir();
        var restoreDir = NewTempDir();
        try
        {
            using (var store = new PeopleStore(sourceDir))
            {
                var comune = store.EnsurePlace(
                    [new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Lombardia"), new GeoLevel("Provincia", "Milano"), new GeoLevel("Comune", "Milano")],
                    45.46,
                    9.19)!.Value;
                var chiesa = store.EnsureSpecificPlace("Basilica di Sant'Ambrogio", comune, "Q1", null, null);
                store.Add(new HistoricalPerson { Name = "Ambrogio", BirthLuogoId = comune, BurialLuogoSpecificoId = chiesa });
                store.ExportJson(store.LoadAll());
            }

            File.Copy(Path.Combine(sourceDir, "people.json"), Path.Combine(restoreDir, "people.json"));

            using var restored = new PeopleStore(restoreDir);
            var person = Assert.Single(restored.LoadAll());
            Assert.Equal("Milano, Milano, Lombardia, Italia", restored.DescribePlace(person.BirthLuogoId));
            Assert.Equal(new PlaceDescription("Milano, Milano, Lombardia, Italia", "Basilica di Sant'Ambrogio"), restored.DescribeRole(null, person.BurialLuogoSpecificoId));
            Assert.Equal(45.46, restored.GetAllPlaces().Single(l => l.Id == person.BirthLuogoId).Latitudine);
            // Italia e regioni create all'avvio vengono riusate, non duplicate.
            Assert.Single(restored.GetAllPlaces(), l => l.Nome == "Lombardia");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(restoreDir, recursive: true);
        }
    }

    [Fact]
    public void Backup_WithoutPlaces_UnlinksMissingPlaceReferences()
    {
        var dir = NewTempDir();
        try
        {
            // Backup precedente all'inclusione dei luoghi: gli id non sono verificabili e non devono bloccare l'import.
            File.WriteAllText(Path.Combine(dir, "people.json"), "{\"People\":[{\"Id\":1,\"Name\":\"Antico\",\"BirthPlace\":\"Milano\",\"BirthLuogoId\":999,\"DeathLuogoId\":2}]}");

            using var store = new PeopleStore(dir);
            var person = Assert.Single(store.LoadAll());

            Assert.Null(person.BirthLuogoId);
            Assert.Null(person.DeathLuogoId);
            Assert.Equal("Milano", person.BirthPlace);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Backup_WithoutPeople_StillRestoresDynastiesAndEvents()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "people.json"),
                "{\"People\":[],\"Dynasties\":[{\"Id\":1,\"Nome\":\"Sforza\"}],"
                + "\"Events\":[{\"Id\":1,\"Titolo\":\"Pace di Lodi\",\"Partecipanti\":[{\"EventoId\":1,\"PersonaId\":7}]}]}");

            using (var store = new PeopleStore(dir))
            {
                Assert.Empty(store.LoadAll());
                Assert.Equal("Sforza", Assert.Single(store.GetDynasties()).Nome);
                Assert.Empty(Assert.Single(store.GetEvents()).Partecipanti);
            }

            // Al riavvio l'archivio non è più vuoto: il backup non deve essere importato una seconda volta.
            using (var reopened = new PeopleStore(dir))
            {
                reopened.LoadAll();
                Assert.Single(reopened.GetDynasties());
                Assert.Single(reopened.GetEvents());
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Backup_Unreadable_IsPreservedBeforeOverwrite()
    {
        var dir = NewTempDir();
        try
        {
            const string corrupt = "{\"People\":[{\"Id\":1,";
            File.WriteAllText(Path.Combine(dir, "people.json"), corrupt);

            using var store = new PeopleStore(dir);
            Assert.Empty(store.LoadAll());
            store.ExportJson([]);

            Assert.NotNull(store.UnimportedBackupPath);
            Assert.Equal(corrupt, File.ReadAllText(store.UnimportedBackupPath));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Backup_FailedImport_IsPreservedAndArchiveStaysEmpty()
    {
        var dir = NewTempDir();
        try
        {
            // Unione verso una persona inesistente: il vincolo di chiave esterna fa fallire l'import.
            const string invalid = "{\"People\":[{\"Id\":1,\"Name\":\"Solo\"}],\"Unions\":[{\"Id\":1,\"Persona1Id\":1,\"Persona2Id\":42}]}";
            File.WriteAllText(Path.Combine(dir, "people.json"), invalid);

            using var store = new PeopleStore(dir);
            Assert.Throws<Microsoft.EntityFrameworkCore.DbUpdateException>(() => store.LoadAll());

            Assert.NotNull(store.UnimportedBackupPath);
            Assert.Equal(invalid, File.ReadAllText(store.UnimportedBackupPath));
            // La transazione annulla anche i luoghi e le persone già inseriti.
            Assert.Empty(store.LoadAll());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Backup_OldArrayFormat_StillImportsPeople()
    {
        var dir = NewTempDir();
        try
        {
            // Vecchio formato: semplice elenco di persone, senza unioni.
            File.WriteAllText(Path.Combine(dir, "people.json"), "[{\"Id\":1,\"Name\":\"Antico\"}]");

            using var store = new PeopleStore(dir);
            var people = store.LoadAll();

            Assert.Single(people);
            Assert.Equal("Antico", people[0].Name);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
