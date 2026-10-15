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
