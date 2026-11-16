using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Genitori e figli adottivi (2026-11-16): su Wikidata stanno tra padri e figli con il «tipo di parentela» (P1039) padre o
// figlio adottivo. Non diventano mai padre, madre o figli della scheda, ma relazioni «Adozione».
public class AdoptionTests
{
    // Tiberio importato con Augusto già in archivio: Augusto non è il padre (che resta da indicare: Tiberio Claudio Nerone
    // non è in archivio), ma il padre adottivo dal 4 d.C.; è proposto tra i parenti mancanti come padre naturale.
    [Fact]
    public async Task Tiberius_AugustusIsAdoptiveFather()
    {
        var import = await ImportFixtures.ImportAsync("Tiberio");
        var dir = Path.Combine(Path.GetTempPath(), $"historic-adoption-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var augustus = new HistoricalPerson { Name = "Augusto", Sesso = Sessi.Maschio, WikidataId = "Q1405" };
            store.Add(augustus);

            var (tiberius, _) = ImportedPersonCreator.Create(store, import);
            Assert.Null(store.GetPerson(tiberius.Id)!.FatherId);

            var relation = Assert.Single(store.GetRelations());
            Assert.Equal((augustus.Id, tiberius.Id, "Adozione", "0004"), (relation.Persona1Id, relation.Persona2Id, relation.Tipo, relation.Inizio));
            Assert.StartsWith("Figlio adottivo di Augusto", Assert.Single(store.GetRelationsForPerson(tiberius.Id)).Descrizione);
            Assert.StartsWith("Padre adottivo di Tiberio", Assert.Single(store.GetRelationsForPerson(augustus.Id)).Descrizione);

            // Tra i parenti mancanti: il padre naturale come padre, Marco Gallio come genitore adottivo, Germanico come
            // figlio adottivo.
            var missing = ImportedPersonCreator.MissingRelatives(store, import);
            Assert.Contains(new MissingRelative("Q342512", "padre"), missing);
            Assert.Contains(new MissingRelative("Q2165262", "genitore adottivo"), missing);
            Assert.Contains(new MissingRelative("Q191039", "figlio o figlia adottivi"), missing);
            Assert.DoesNotContain(missing, m => m.WikidataId == "Q1405");

            // Un secondo collegamento (come dopo l'import dei parenti mancanti) non duplica la relazione.
            ImportedPersonCreator.LinkRelatives(store, tiberius.Id, import);
            Assert.Single(store.GetRelations());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
