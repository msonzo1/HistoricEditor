using System.IO;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class EventTests
{
    [Theory]
    [InlineData(DataEvento.Esatta, "24 febbraio 1525", "", "24 febbraio 1525")]
    [InlineData(DataEvento.Circa, "1525", "", "circa 1525")]
    [InlineData(DataEvento.Prima, "1525", "", "prima del 1525")]
    [InlineData(DataEvento.Prima, "11 maggio 1525", "", "prima dell’11 maggio 1525")]
    [InlineData(DataEvento.Dopo, "8 marzo 1525", "", "dopo l’8 marzo 1525")]
    [InlineData(DataEvento.DaA, "1618", "1648", "dal 1618 al 1648")]
    [InlineData(DataEvento.DaA, "1 aprile 1559", "3 aprile 1559", "dall’1 aprile 1559 al 3 aprile 1559")]
    [InlineData(DataEvento.DaA, "1618", "", "dal 1618 in poi")]
    [InlineData(DataEvento.Tra, "1520", "820", "tra il 1520 e l’820")]
    [InlineData(DataEvento.Circa, "", "", "")]
    // Un qualificatore senza intervallo ignora la seconda data.
    [InlineData(DataEvento.Circa, "1525", "1530", "circa 1525")]
    public void Format_ComposesReadableDate(string qualifier, string data1, string data2, string expected)
        => Assert.Equal(expected, DataEvento.Format(qualifier, data1, data2));

    [Fact]
    public void Qualifier_DisplaysItsLabel()
        => Assert.Equal("Circa", DataEvento.Info(DataEvento.Circa).ToString());

    [Fact]
    public void Validate_RejectsRangeEndingBeforeStart()
    {
        Assert.NotNull(DataEvento.Validate(DataEvento.DaA, "1648", "1618"));
        Assert.Null(DataEvento.Validate(DataEvento.Tra, "44 a.C.", "40 a.C."));
        Assert.Null(DataEvento.Validate(DataEvento.Circa, "1648", "1618"));
    }

    [Fact]
    public void Events_SaveWithParticipants_AndCleanUpOnPersonDelete()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-evt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new PeopleStore(dir);
            var carlo = store.CreatePerson("Carlo V", Sessi.Maschio);
            var francesco = store.CreatePerson("Francesco I", Sessi.Maschio);

            Assert.Throws<InvalidOperationException>(() => store.SaveEvent(new Evento { Titolo = " " }));
            Assert.Throws<InvalidOperationException>(() => store.SaveEvent(new Evento { Titolo = "X", DataQualificatore = DataEvento.DaA, Data1 = "1530", Data2 = "1520" }));

            var id = store.SaveEvent(new Evento
            {
                Titolo = " Battaglia di Pavia ",
                Tipo = "Battaglia",
                Data1 = "24 febbraio 1525",
                Data2 = "ignorata",
                Partecipanti =
                [
                    new EventoPartecipante { PersonaId = carlo, Ruolo = "vincitore" },
                    new EventoPartecipante { PersonaId = francesco, Ruolo = "sconfitto" },
                    new EventoPartecipante { PersonaId = francesco, Ruolo = "doppione" },
                ],
            });
            store.SaveEvent(new Evento { Titolo = "Pace di Cambrai", Tipo = "Pace", Data1 = "1529", Partecipanti = [new EventoPartecipante { PersonaId = francesco }] });

            var saved = store.GetEvents();
            Assert.Equal(["Battaglia di Pavia", "Pace di Cambrai"], saved.Select(e => e.Titolo));
            var pavia = saved[0];
            Assert.Equal(string.Empty, pavia.Data2);
            Assert.Equal(2, pavia.Partecipanti.Count);
            Assert.Equal("sconfitto", pavia.Partecipanti.Single(p => p.PersonaId == francesco).Ruolo);
            Assert.Equal(2, store.GetEventsForPerson(francesco).Count);

            // Aggiornando si sostituiscono i partecipanti e i ruoli.
            store.SaveEvent(new Evento { Id = id, Titolo = "Battaglia di Pavia", Data1 = "1525", Partecipanti = [new EventoPartecipante { PersonaId = carlo, Ruolo = "imperatore" }] });
            Assert.Equal("imperatore", Assert.Single(store.GetEventsForPerson(carlo)).Partecipanti.Single().Ruolo);
            Assert.Single(store.GetEventsForPerson(francesco));

            // Eliminando una persona l'evento resta, senza di lei tra i partecipanti.
            store.Delete(francesco);
            Assert.Equal(2, store.GetEvents().Count);
            Assert.Empty(store.GetEvents().Single(e => e.Titolo == "Pace di Cambrai").Partecipanti);

            store.DeleteEvent(id);
            Assert.Empty(store.GetEventsForPerson(carlo));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Backup_RoundTrip_RestoresEvents()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), $"historic-evt-{Guid.NewGuid():N}");
        var restoreDir = Path.Combine(Path.GetTempPath(), $"historic-evt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(restoreDir);
        try
        {
            using (var store = new PeopleStore(sourceDir))
            {
                var enrico = store.CreatePerson("Enrico II", Sessi.Maschio);
                store.SaveEvent(new Evento
                {
                    Titolo = "Pace di Cateau-Cambrésis",
                    Tipo = "Trattato",
                    DataQualificatore = DataEvento.DaA,
                    Data1 = "2 aprile 1559",
                    Data2 = "3 aprile 1559",
                    Partecipanti = [new EventoPartecipante { PersonaId = enrico, Ruolo = "firmatario" }],
                });
                store.ExportJson(store.LoadAll());
            }

            File.Copy(Path.Combine(sourceDir, "people.json"), Path.Combine(restoreDir, "people.json"));

            using var restored = new PeopleStore(restoreDir);
            var enricoId = Assert.Single(restored.LoadAll()).Id;
            var evento = Assert.Single(restored.GetEvents());
            Assert.Equal("dal 2 aprile 1559 al 3 aprile 1559", evento.DataTesto);
            var partecipante = Assert.Single(evento.Partecipanti);
            Assert.Equal(enricoId, partecipante.PersonaId);
            Assert.Equal("firmatario", partecipante.Ruolo);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(restoreDir, recursive: true);
        }
    }
}
