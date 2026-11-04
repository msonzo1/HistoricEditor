using System.IO;
using System.Windows.Controls;
using HistoricEditor;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HistoricEditor.Tests;

public sealed class HistoricalTimelineTests
{
    private static HistoricalPerson Person(int id, string name, string birth = "", string death = "", string family = "", int? father = null, int? mother = null)
        => new() { Id = id, Name = name, Birth = birth, Death = death, Family = family, FatherId = father, MotherId = mother };

    private static Evento Event(int id, string title, string date, params int[] people)
        => new()
        {
            Id = id,
            Titolo = title,
            Tipo = "Battaglia",
            Data1 = date,
            Partecipanti = [.. people.Select(p => new EventoPartecipante { PersonaId = p, Ruolo = p == 1 ? "comandante" : "" })],
        };

    [Fact]
    public void Build_OrdersLifeOfficesUnionsAndEvents()
    {
        var francesco = Person(1, "Francesco I", "12 settembre 1494", "31 marzo 1547");
        francesco.BirthPlace = "Cognac";
        francesco.Titles = [new PersonTitle { Titolo = "Re di Francia", Inizio = "1 gennaio 1515", CausaInizio = "Morte del predecessore", Fine = "31 marzo 1547" }];
        var claudia = Person(2, "Claudia di Francia", "13 ottobre 1499", "20 luglio 1524");
        var carlo = Person(3, "Carlo V", "24 febbraio 1500", "21 settembre 1558");
        var unions = new[] { new Unione { Persona1Id = 1, Persona2Id = 2, Inizio = "18 maggio 1514" } };
        var events = new[] { Event(10, "Battaglia di Pavia", "24 febbraio 1525", 1, 3), Event(11, "Senza data", "", 1) };

        var data = HistoricalTimeline.Build([francesco, claudia, carlo], unions, events, new HashSet<int> { 1 });

        Assert.Equal(
            ["Nascita", "Matrimonio", "Inizio carica", "Battaglia", "Fine carica", "Morte", "Battaglia"],
            data.Voci.Select(v => v.Tipo));
        Assert.Equal("Nascita · Cognac", data.Voci[0].Descrizione);
        Assert.Equal("Matrimonio tra Francesco I e Claudia di Francia", data.Voci[1].Descrizione);
        Assert.Equal("Re di Francia (morte del predecessore)", data.Voci[2].Descrizione);
        Assert.Equal("Francesco I (comandante)", data.Voci[3].Persone);
        Assert.Equal(10, data.Voci[3].EventoId);
        Assert.Null(data.Voci[^1].Anno);
        Assert.Equal("—, Battaglia: Senza data — Francesco I (comandante)", data.Voci[^1].Etichetta);

        var lane = Assert.Single(data.Corsie);
        Assert.Equal([(TimelineCategory.Vita, 1494, 1547), (TimelineCategory.Carica, 1515, 1547)],
            lane.Barre.Select(b => (b.Categoria, b.Inizio, b.Fine)));
        Assert.Equal((1494, 1547), data.Intervallo);
    }

    [Fact]
    public void Build_UsesModernYearAndOpenOffices()
    {
        var cosimo = Person(1, "Cosimo", "1519", "1574");
        cosimo.Titles = [new PersonTitle { Titolo = "Duca di Firenze", Inizio = "1537" }];
        var events = new[]
        {
            new Evento { Id = 1, Titolo = "Fiorentino", Data1 = "10 febbraio 1555", Stile = HistoricalCalendar.StileFiorentino, Partecipanti = [new() { PersonaId = 1 }] },
            new Evento { Id = 2, Titolo = "Maggio", Data1 = "1 maggio 1555", Partecipanti = [new() { PersonaId = 1 }] },
            new Evento { Id = 3, Titolo = "Guerra", DataQualificatore = DataEvento.DaA, Data1 = "1552", Data2 = "1559", Partecipanti = [new() { PersonaId = 1 }] },
        };

        var data = HistoricalTimeline.Build([cosimo], [], events, new HashSet<int> { 1 });

        Assert.Equal(["Guerra", "Maggio", "Fiorentino"], data.Voci.Where(v => v.EventoId is not null).Select(v => v.Descrizione));
        Assert.Equal(1556, data.Voci.Single(v => v.Descrizione == "Fiorentino").Anno);
        var bars = data.Corsie.Single().Barre;
        var office = bars.Single(b => b.Categoria == TimelineCategory.Carica);
        Assert.Equal((1537, 1574, true), (office.Inizio, office.Fine, office.FineAperta));
        Assert.Contains(bars, b => b is { Categoria: TimelineCategory.Evento, Inizio: 1552, Fine: 1559 });
    }

    [Fact]
    public void Relatives_IncludesParentsSpousesAndChildren()
    {
        var people = new[]
        {
            Person(1, "Padre"), Person(2, "Madre"), Person(3, "Io", father: 1, mother: 2),
            Person(4, "Coniuge"), Person(5, "Figlio", father: 3, mother: 6), Person(6, "Altra madre"),
            Person(7, "Fratello", father: 1, mother: 2), Person(8, "Estraneo"),
        };
        var unions = new[] { new Unione { Persona1Id = 4, Persona2Id = 3 }, new Unione { Persona1Id = 8, Persona2Id = 99 } };

        Assert.Equal([1, 2, 3, 4, 5, 6], HistoricalTimeline.Relatives(3, people, unions).Order());
    }

    [Fact]
    public void FamilyOptionsAndMembers_IncludeDynastyBranches()
    {
        var people = new[] { Person(1, "A", family: "Asburgo"), Person(2, "B", family: "asburgo di Spagna"), Person(3, "C", family: "Valois"), Person(4, "D") };
        var dynasties = new[] { new Dinastia { Id = 1, Nome = "Asburgo" }, new Dinastia { Id = 2, Nome = "Asburgo di Spagna", ParentId = 1 } };

        var options = HistoricalTimeline.FamilyOptions(people, dynasties);
        Assert.Equal(["Asburgo", "Asburgo di Spagna", "Valois"], options.Select(o => o.Nome));
        Assert.Null(options[2].DinastiaId);

        Assert.Equal([1, 2], HistoricalTimeline.FamilyMembers(people, HistoricalMap.FamiliesOfDynasty(1, dynasties)).Order());
        Assert.Equal([3], HistoricalTimeline.FamilyMembers(people, ["Valois"]));
    }

    [Fact]
    public void Window_ShowsPersonRelativesAndDynasty()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-timeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var francesco = store.CreatePerson("Francesco I", Sessi.Maschio);
                var claudia = store.CreatePerson("Claudia di Francia", Sessi.Femmina);
                foreach (var (id, birth, death) in new[] { (francesco, "1494", "1547"), (claudia, "1499", "1524") })
                {
                    var person = store.GetPerson(id)!;
                    (person.Birth, person.Death, person.Family) = (birth, death, "Valois");
                    store.Update(person);
                }

                store.SaveUnion(new Unione { Persona1Id = francesco, Persona2Id = claudia, Inizio = "1514" });
                store.SaveEvent(new Evento { Titolo = "Battaglia di Pavia", Data1 = "1525", Partecipanti = [new() { PersonaId = francesco }] });
                Assert.Single(store.GetUnions());

                var window = new TimelineWindow(store, francesco);
                Assert.Equal(4, window.TimelineGrid.Items.Count);
                Assert.Single(window.Data.Corsie);

                window.ShowEventsCheck.IsChecked = false;
                Assert.Equal(3, window.TimelineGrid.Items.Count);

                window.RelativesCheck.IsChecked = true;
                Assert.Equal(2, window.Data.Corsie.Count);

                window.FamilyMode.IsChecked = true;
                Assert.Equal("Valois", Assert.IsType<TimelineFamilyOption>(window.FamilyBox.SelectedItem).Nome);
                Assert.Equal(2, window.Data.Corsie.Count);
                Assert.True(window.Chart.Children.Count > 0);
                Assert.False(window.PersonBox.IsEnabled);
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
