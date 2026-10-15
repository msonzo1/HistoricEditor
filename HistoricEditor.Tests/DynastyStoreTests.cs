using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class DynastyStoreTests
{
    [Fact]
    public void Dynasties_EnsureRenameAndPreventCycles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-dyn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            store.Add(new HistoricalPerson { Name = "Filippo", Family = "Asburgo di Spagna" });

            var asburgo = store.EnsureDynasty("Asburgo", "Q153134", null);
            var spagna = store.EnsureDynasty("Asburgo di Spagna", "", asburgo);
            Assert.Equal(spagna, store.EnsureDynasty("ASBURGO DI SPAGNA", "", null));
            Assert.Equal(asburgo, store.GetDynasties().Single(d => d.Id == spagna).ParentId);

            // Una dinastia non può diventare ramo di un proprio ramo.
            Assert.Throws<InvalidOperationException>(() => store.SaveDynasty(new Dinastia { Id = asburgo, Nome = "Asburgo", ParentId = spagna }));

            // Rinominando la dinastia si aggiorna la famiglia delle schede collegate.
            store.SaveDynasty(new Dinastia { Id = spagna, Nome = "Asburgo-Spagna", ParentId = asburgo });
            Assert.Equal("Asburgo-Spagna", store.LoadAll().Single().Family);

            store.DeleteDynasty(asburgo);
            Assert.Null(store.GetDynasties().Single(d => d.Id == spagna).ParentId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
