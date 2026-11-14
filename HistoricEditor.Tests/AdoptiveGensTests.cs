using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

// Gens di nascita e gens d'adozione in campi separati (2026-11-14).
public class AdoptiveGensTests
{
    private static HistoricalPerson MarcusAurelius() => new()
    {
        Id = 1, Name = "Marco Aurelio", Onomastica = Onomastiche.Romana,
        Gens = "gens Annia", GensAdozione = "gens Aurelia", Family = "Dinastia Nerva-Antonina",
    };

    // Nell'elenco delle Persone il personaggio compare solo sotto la gens di nascita.
    [Fact]
    public void Grouping_UsesOnlyBirthGens()
    {
        Dinastia[] dynasties =
        [
            new() { Id = 1, Nome = "gens Annia", Tipo = TipiDinastia.Gens },
            new() { Id = 2, Nome = "gens Aurelia", Tipo = TipiDinastia.Gens },
            new() { Id = 3, Nome = "Dinastia Nerva-Antonina" },
        ];
        PersonaDinastia[] links = [new() { PersonaId = 1, DinastiaId = 1 }, new() { PersonaId = 1, DinastiaId = 2 }, new() { PersonaId = 1, DinastiaId = 3 }];

        var groups = PeopleGrouping.Build([MarcusAurelius()], dynasties, links);
        var all = groups.SelectMany(g => g.SelfAndDescendants()).SelectMany(g => g.Persone.Select(p => g.Famiglia)).ToList();
        Assert.Equal(["gens Annia"], all);

        // Per i collegamenti (sezione Dinastie, mappa, linea del tempo) conta anche la gens d'adozione.
        Assert.Contains("gens Aurelia", PeopleGrouping.AllFamilyNames(MarcusAurelius()));
    }

    [Fact]
    public void Sheet_ShowsBothGens()
    {
        var fields = PersonSheet.MainFields(MarcusAurelius());
        Assert.Equal("gens Annia", fields.Single(f => f.Etichetta == "Gens di nascita").Valore);
        Assert.Equal("gens Aurelia", fields.Single(f => f.Etichetta == "Gens d'adozione").Valore);
    }

    // Il figlio senza gens prende quella del padre al momento della nascita: la gens d'adozione, se c'è (Commodo, figlio di
    // Marco Aurelio, è della gens Aurelia). La gens d'adozione si salva, si rilegge e segue la rinomina della gens.
    [Fact]
    public void Store_KeepsAdoptiveGensAndChildInheritsIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-adoption-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var father = MarcusAurelius();
            father.Id = 0;
            store.Add(father);
            var gens = store.EnsureDynasty("gens Aurelia", string.Empty, null, TipiDinastia.Gens);
            var child = new HistoricalPerson { Name = "Commodo", FatherId = father.Id };
            store.Add(child);

            Assert.True(store.InheritFatherFamilyIfMissing(child.Id));
            Assert.Equal("gens Aurelia", store.GetPerson(child.Id)!.Gens);

            store.SaveDynasty(new Dinastia { Id = gens, Nome = "gens Aurelia (adottiva)", Tipo = TipiDinastia.Gens });
            Assert.Equal("gens Aurelia (adottiva)", store.GetPerson(father.Id)!.GensAdozione);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    // Nome latino delle dinastie (2026-11-14): l'import lo scrive solo se manca, il modulo dell'archivio lo cambia; resta
    // nel backup.
    [Fact]
    public void Store_KeepsLatinNameOfDynasties()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-latin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using (var store = new PeopleStore(dir))
            {
                var giulia = store.EnsureDynasty("gens Giulia", "Q510193", null, TipiDinastia.Gens, "gens Iulia");
                Assert.Equal(giulia, store.EnsureDynasty("gens Giulia", "Q510193", null, TipiDinastia.Gens, "Iulii"));
                Assert.Equal("gens Iulia", store.GetDynasties().Single(d => d.Id == giulia).NomeLatino);

                var ottavia = store.EnsureDynasty("gens Ottavia", string.Empty, null, TipiDinastia.Gens);
                store.EnsureDynasty("gens Ottavia", "Q1759265", null, TipiDinastia.Gens, "gens Octavia");
                Assert.Equal("gens Octavia", store.GetDynasties().Single(d => d.Id == ottavia).NomeLatino);

                store.SaveDynasty(new Dinastia { Id = giulia, Nome = "gens Giulia", NomeLatino = " gens Iulia (Iulii) ", Tipo = TipiDinastia.Gens });
                Assert.Equal("gens Giulia (gens Iulia (Iulii))", store.GetDynasties().Single(d => d.Id == giulia).NomeCompleto);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
