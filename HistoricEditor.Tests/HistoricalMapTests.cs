using System.IO;
using System.Threading;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class HistoricalMapTests
{
    private static readonly Dictionary<int, Luogo> Places = new()
    {
        [1] = new Luogo { Id = 1, Nome = "Lombardia", Latitudine = 45.6, Longitudine = 9.8 },
        [2] = new Luogo { Id = 2, Nome = "Pavia", ParentId = 1, Latitudine = 45.18, Longitudine = 9.15 },
        [3] = new Luogo { Id = 3, Nome = "Borgo senza coordinate", ParentId = 2 },
    };

    private static readonly Dictionary<int, LuogoSpecifico> Specifics = new()
    {
        [10] = new LuogoSpecifico { Id = 10, Nome = "Parco Visconteo", LuogoId = 2, Latitudine = 45.22, Longitudine = 9.14 },
        [11] = new LuogoSpecifico { Id = 11, Nome = "Cascina", LuogoId = 3 },
    };

    [Fact]
    public void ResolveCoordinates_PrefersSpecificPlace_ThenWalksUpParents()
    {
        Assert.Equal((45.22, 9.14, false), HistoricalMap.ResolveCoordinates(null, 10, Places, Specifics));
        Assert.Equal((45.18, 9.15, false), HistoricalMap.ResolveCoordinates(2, null, Places, Specifics));
        // Senza coordinate si risale al luogo contenitore, segnalando la posizione come approssimata.
        Assert.Equal((45.18, 9.15, true), HistoricalMap.ResolveCoordinates(3, null, Places, Specifics));
        Assert.Equal((45.18, 9.15, true), HistoricalMap.ResolveCoordinates(null, 11, Places, Specifics));
        Assert.Null(HistoricalMap.ResolveCoordinates(99, null, Places, Specifics));
    }

    [Fact]
    public void BuildPoints_CreatesPersonEventAndPlacePoints()
    {
        var people = new[]
        {
            new HistoricalPerson { Id = 1, Name = "Francesco I", Family = "Valois", Birth = "1494", BirthLuogoId = 3, Death = "1547", BurialLuogoSpecificoId = 10 },
        };
        var events = new[]
        {
            new Evento { Id = 5, Titolo = "Battaglia di Pavia", Tipo = "Battaglia", Data1 = "1525", LuogoSpecificoId = 10, Partecipanti = [new EventoPartecipante { PersonaId = 1 }] },
            new Evento { Id = 6, Titolo = "Senza luogo", Data1 = "1526" },
        };

        var points = HistoricalMap.BuildPoints(people, events, Places.Values, Specifics.Values);

        var birth = Assert.Single(points, p => p.Kind == MapPointKind.Nascita);
        Assert.True(birth.Approssimato);
        Assert.Equal(1494, birth.Anno);
        // La sepoltura usa l'anno di morte.
        Assert.Equal(1547, Assert.Single(points, p => p.Kind == MapPointKind.Sepoltura).Anno);
        Assert.DoesNotContain(points, p => p.Kind == MapPointKind.Morte);
        var battle = Assert.Single(points, p => p.Kind == MapPointKind.Evento);
        Assert.Equal([1], battle.PersoneIds);
        Assert.Equal("Parco Visconteo, Pavia", battle.Luogo);
        Assert.Equal(3, points.Count(p => p.Kind == MapPointKind.Luogo));
    }

    [Fact]
    public void Filter_AppliesPeriodPersonDynastyAndType()
    {
        MapPoint Point(MapPointKind kind, int? year, int[] people, string type = "")
            => new(kind, 45, 9, kind.ToString(), string.Empty, string.Empty, year, null, null, people, string.Empty, type, false);

        var points = new[]
        {
            Point(MapPointKind.Nascita, 1494, [1]),
            Point(MapPointKind.Morte, null, [1]),
            Point(MapPointKind.Evento, 1525, [1, 2], "Battaglia"),
            Point(MapPointKind.Evento, 1529, [2], "Pace"),
            Point(MapPointKind.Luogo, null, []),
        };
        var families = new Dictionary<int, string> { [1] = "Valois", [2] = "Asburgo" };

        Assert.Equal(5, HistoricalMap.Filter(points, new MapFilter(), families).Count);

        // Con un periodo gli elementi senza anno sono esclusi; i luoghi restano.
        var period = HistoricalMap.Filter(points, new MapFilter { DaAnno = 1500, AAnno = 1530 }, families);
        Assert.Equal([MapPointKind.Evento, MapPointKind.Evento, MapPointKind.Luogo], period.Select(p => p.Kind));

        // Con un personaggio i luoghi dell'archivio non compaiono.
        var person = HistoricalMap.Filter(points, new MapFilter { PersonId = 2 }, families);
        Assert.Equal([1525, 1529], person.Select(p => p.Anno!.Value));

        var dynasty = HistoricalMap.Filter(points, new MapFilter { Famiglie = new HashSet<string>(["valois"], HistoricalMap.FamilyComparer) }, families);
        Assert.Equal(3, dynasty.Count);
        Assert.DoesNotContain(dynasty, p => p.TipoEvento == "Pace");

        // Il filtro sul tipo riguarda solo gli eventi.
        var type = HistoricalMap.Filter(points, new MapFilter { TipoEvento = "pace", Luoghi = false, Nascite = false }, families);
        Assert.Equal([MapPointKind.Morte, MapPointKind.Evento], type.Select(p => p.Kind));
    }

    [Fact]
    public void GroupByLocation_MergesPointsAtSamePosition()
    {
        MapPoint Point(double lat, string title, MapPointKind kind = MapPointKind.Evento)
            => new(kind, lat, 9, title, "Pavia", string.Empty, null, null, null, [], string.Empty, string.Empty, false);

        var groups = HistoricalMap.GroupByLocation([Point(45.18, "B"), Point(45.180001, "A"), Point(45.5, "Milano", MapPointKind.Luogo)]);

        Assert.Equal(2, groups.Count);
        var pavia = groups.Single(g => g.Punti.Count == 2);
        Assert.Equal(["A", "B"], pavia.Punti.Select(p => p.Titolo));
        Assert.Equal("Pavia: 2 elementi", pavia.Etichetta);
    }

    [Fact]
    public void FamiliesOfDynasty_IncludesBranches()
    {
        var dynasties = new[]
        {
            new Dinastia { Id = 1, Nome = "Capetingi" },
            new Dinastia { Id = 2, Nome = "Valois", ParentId = 1 },
            new Dinastia { Id = 3, Nome = "Valois-Angoulême", ParentId = 2 },
            new Dinastia { Id = 4, Nome = "Asburgo" },
        };

        var families = HistoricalMap.FamiliesOfDynasty(2, dynasties);

        Assert.Equal(2, families.Count);
        Assert.Contains("valois-angoulême", families);
    }

    [Fact]
    public void Store_SavesEventPlace_AndBackupRestoresIt()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), $"historic-map-{Guid.NewGuid():N}");
        var restoreDir = Path.Combine(Path.GetTempPath(), $"historic-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(restoreDir);
        try
        {
            using (var store = new PeopleStore(sourceDir))
            {
                var pavia = store.EnsurePlace(
                    [new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Lombardia"), new GeoLevel("Provincia", "Pavia"), new GeoLevel("Comune", "Pavia")],
                    45.18,
                    9.15)!.Value;
                var parco = store.EnsureSpecificPlace("Parco Visconteo", pavia, "Q1", 45.22, 9.14);

                Assert.Throws<InvalidOperationException>(() => store.SaveEvent(new Evento { Titolo = "X", LuogoId = 999_999 }));
                Assert.Throws<InvalidOperationException>(() => store.SaveEvent(new Evento { Titolo = "X", LuogoSpecificoId = 999_999 }));

                // Il luogo specifico determina il luogo amministrativo.
                store.SaveEvent(new Evento { Titolo = "Battaglia di Pavia", Data1 = "1525", LuogoSpecificoId = parco });
                var saved = Assert.Single(store.GetEvents());
                Assert.Equal(pavia, saved.LuogoId);
                Assert.Equal(parco, saved.LuogoSpecificoId);
                store.ExportJson(store.LoadAll());
            }

            File.Copy(Path.Combine(sourceDir, "people.json"), Path.Combine(restoreDir, "people.json"));

            using var restored = new PeopleStore(restoreDir);
            restored.LoadAll();
            var evento = Assert.Single(restored.GetEvents());
            Assert.Equal(new PlaceDescription("Pavia, Pavia, Lombardia, Italia", "Parco Visconteo"), restored.DescribeRole(evento.LuogoId, evento.LuogoSpecificoId));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(sourceDir, recursive: true);
            Directory.Delete(restoreDir, recursive: true);
        }
    }

    [Fact]
    public void MapWindow_OpensWithMapControl()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var person = store.CreatePerson("Francesco I", Sessi.Maschio);
                var window = new MapWindow(store, person);
                Assert.NotNull(window.MapView.Map);
                Assert.Equal(person, (window.PersonBox.SelectedItem as MapWindow.PersonOption)?.Id);
                window.Close();
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
