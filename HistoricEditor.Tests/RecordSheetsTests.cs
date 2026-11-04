using System.IO;
using System.Windows;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class RecordSheetsTests
{
    private static string NameOf(int id) => id switch
    {
        1 => "Leonardo da Vinci",
        2 => "Lisa Gherardini",
        3 => "Andrea del Verrocchio",
        4 => "Ludovico il Moro",
        _ => $"Scheda {id}",
    };

    [Fact]
    public void Matches_IgnoresCaseAndAccents()
    {
        Assert.True(RecordSheet.Matches("", "qualsiasi"));
        Assert.True(RecordSheet.Matches("  ", "qualsiasi"));
        Assert.True(RecordSheet.Matches("NIKOLA", "Nikolà Tesla"));
        Assert.True(RecordSheet.Matches("citta", null, "Città del Vaticano"));
        Assert.False(RecordSheet.Matches("pavia", "Battaglia di Marignano", null));
    }

    [Fact]
    public void Sources_SplitLinesDropDuplicatesAndDetectLinks()
    {
        var sources = RecordSheet.Sources("https://it.wikipedia.org/wiki/Gioconda\r\n\r\nVasari, Le vite\nhttps://it.wikipedia.org/wiki/Gioconda\nftp://esempio.it/x");

        Assert.Equal(["https://it.wikipedia.org/wiki/Gioconda", "Vasari, Le vite", "ftp://esempio.it/x"], sources.Select(s => s.Testo));
        Assert.True(sources[0].IsLink);
        Assert.False(sources[1].IsLink);
        Assert.False(sources[2].IsLink);
        Assert.Equal("https://www.wikidata.org/wiki/Q12418", RecordSheet.WikidataUrl(" Q12418 "));
        Assert.Equal(string.Empty, RecordSheet.WikidataUrl(""));
    }

    [Fact]
    public void ArtworkSheet_PeopleFollowRoleOrderAndSameArtistsSkipSubjects()
    {
        var gioconda = new Opera
        {
            Id = 10,
            Titolo = "La Gioconda",
            Data = "1503",
            Tipo = "Dipinto",
            Persone =
            [
                new OperaPersona { PersonaId = 2, Ruolo = OperaRuoli.Soggetto },
                new OperaPersona { PersonaId = 1, Ruolo = OperaRuoli.Autore },
            ],
        };
        var cenacolo = new Opera { Id = 11, Titolo = "Ultima Cena", Data = "1495", Persone = [new OperaPersona { PersonaId = 1, Ruolo = OperaRuoli.Autore }, new OperaPersona { PersonaId = 4, Ruolo = OperaRuoli.Committente }] };
        var battesimo = new Opera { Id = 12, Titolo = "Battesimo di Cristo", Data = "1475", Persone = [new OperaPersona { PersonaId = 3, Ruolo = OperaRuoli.Autore }, new OperaPersona { PersonaId = 1, Ruolo = OperaRuoli.Bottega }] };
        var ritratto = new Opera { Id = 13, Titolo = "Ritratto di Lisa", Persone = [new OperaPersona { PersonaId = 2, Ruolo = OperaRuoli.Soggetto }] };

        var people = ArtworkSheet.People(gioconda, NameOf);
        Assert.Equal([1, 2], people.Select(p => p.Id));
        Assert.Equal(OperaRuoli.Info(OperaRuoli.Soggetto).Etichetta, people[1].Dettaglio);
        Assert.Contains("Leonardo da Vinci", ArtworkSheet.Subtitle(gioconda, NameOf));

        var fields = ArtworkSheet.MainFields(gioconda, "Parigi");
        Assert.Equal(["Tipo", "Data", "Collocazione"], fields.Select(f => f.Etichetta));

        // Stesso artista anche come bottega; il soggetto in comune non conta. Ordine per anno.
        Assert.Equal([12, 11], ArtworkSheet.SameArtists(gioconda, [gioconda, cenacolo, battesimo, ritratto]).Select(o => o.Id));
        Assert.Empty(ArtworkSheet.SameArtists(ritratto, [gioconda, ritratto]));
    }

    [Fact]
    public void ArtworkGallery_FiltersByTextTypeAndArtist()
    {
        var works = new[]
        {
            new Opera { Id = 1, Titolo = "Ultima Cena", Tipo = "Affresco", Tecnica = "Tempera grassa", Persone = [new OperaPersona { PersonaId = 1, Ruolo = OperaRuoli.Autore }, new OperaPersona { PersonaId = 4, Ruolo = OperaRuoli.Committente }] },
            new Opera { Id = 2, Titolo = "Battesimo di Cristo", Tipo = "Dipinto", Persone = [new OperaPersona { PersonaId = 3, Ruolo = OperaRuoli.Autore }, new OperaPersona { PersonaId = 1, Ruolo = OperaRuoli.Bottega }] },
            new Opera { Id = 3, Titolo = "Annunciazione", Tipo = "Dipinto", Persone = [new OperaPersona { PersonaId = 1, Ruolo = OperaRuoli.Autore }] },
        };

        var rows = ArtworkGallery.Rows(works, NameOf);
        Assert.Equal([3, 2, 1], rows.Select(r => r.Id));
        Assert.Equal([3, 2], ArtworkGallery.Filter(rows, null, "dipinto", null).Select(r => r.Id));
        Assert.Equal([1], ArtworkGallery.Filter(rows, "ludovico", null, null).Select(r => r.Id));
        Assert.Equal([1], ArtworkGallery.Filter(rows, "tempera", null, null).Select(r => r.Id));
        Assert.Equal([2], ArtworkGallery.Filter(rows, null, null, 3).Select(r => r.Id));
        Assert.Empty(ArtworkGallery.Filter(rows, null, null, 4));

        var artists = ArtworkGallery.Artists(works, NameOf);
        Assert.Equal(["Andrea del Verrocchio (1)", "Leonardo da Vinci (3)"], artists.Select(a => a.Etichetta));
    }

    [Fact]
    public void EventSheet_RowsAndFilters()
    {
        var pavia = new Evento
        {
            Id = 1,
            Titolo = "Battaglia di Pavia",
            Tipo = "Battaglia",
            Data1 = "24 febbraio 1525",
            Descrizione = "Francesco I fatto prigioniero",
            Partecipanti = [new EventoPartecipante { PersonaId = 4, Ruolo = "comandante" }, new EventoPartecipante { PersonaId = 1 }],
        };
        var cambrai = new Evento { Id = 2, Titolo = "Pace di Cambrai", Tipo = "Trattato", Partecipanti = [new EventoPartecipante { PersonaId = 2 }] };

        var participants = EventSheet.Participants(pavia, NameOf);
        Assert.Equal(["Leonardo da Vinci", "Ludovico il Moro — comandante"], participants.Select(p => p.Etichetta));
        Assert.Equal(["Tipo", "Data", "Luogo"], EventSheet.MainFields(pavia, "Pavia").Select(f => f.Etichetta));

        var rows = EventSheet.Rows([pavia, cambrai], NameOf, e => e.Id == 1 ? "Pavia" : string.Empty);
        Assert.Equal("Leonardo da Vinci, Ludovico il Moro", rows[0].Partecipanti);
        Assert.Equal([2], EventSheet.Filter(rows, null, "trattato", null).Select(r => r.Id));
        Assert.Equal([1], EventSheet.Filter(rows, "prigioniero", null, null).Select(r => r.Id));
        Assert.Equal([1], EventSheet.Filter(rows, "pavia", null, null).Select(r => r.Id));
        Assert.Equal([1], EventSheet.Filter(rows, null, null, new HashSet<int> { 4, 9 }).Select(r => r.Id));
        Assert.Empty(EventSheet.Filter(rows, null, null, new HashSet<int>()));
        Assert.True(EventSheet.InvolvesAny(cambrai, new HashSet<int> { 2 }));
    }

    [Fact]
    public void DynastySheet_PathMembersAndBranches()
    {
        var dynasties = new[]
        {
            new Dinastia { Id = 1, Nome = "Asburgo" },
            new Dinastia { Id = 2, Nome = "Asburgo di Spagna", ParentId = 1 },
            new Dinastia { Id = 3, Nome = "Valois" },
        };
        var people = new[]
        {
            new HistoricalPerson { Id = 1, Name = "Filippo II", Family = "Asburgo di Spagna", Birth = "1527", Death = "1598" },
            new HistoricalPerson { Id = 2, Name = "Massimiliano I", Family = "Asburgo", Birth = "1459" },
            new HistoricalPerson { Id = 3, Name = "Francesco I", Family = "Valois", Birth = "1494" },
        };

        Assert.Equal("Asburgo › Asburgo di Spagna", DynastySheet.Path(dynasties[1], dynasties));
        Assert.Equal([2, 1], DynastySheet.Members(1, dynasties, people).Select(p => p.Id));
        Assert.Equal([1], DynastySheet.Members(2, dynasties, people).Select(p => p.Id));

        var rows = DynastySheet.MemberRows(dynasties[0], dynasties, people);
        Assert.Equal(("1459 – ?", ""), (rows[0].Anni, rows[0].Ramo));
        Assert.Equal(("1527 – 1598", "Asburgo di Spagna"), (rows[1].Anni, rows[1].Ramo));
    }

    [Fact]
    public void Windows_ReadSheetsAndSectionsShowArchive()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-sheets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var leonardo = store.CreatePerson("Leonardo da Vinci", Sessi.Maschio);
                var lisa = store.CreatePerson("Lisa Gherardini", Sessi.Femmina);
                var person = store.GetPerson(leonardo)!;
                person.Family = "Da Vinci";
                store.Update(person);
                store.SaveDynasty(new Dinastia { Nome = "Da Vinci" });
                var gioconda = store.SaveArtwork(new Opera
                {
                    Titolo = "La Gioconda",
                    Note = "Ritratto di Lisa",
                    Persone = [new OperaPersona { PersonaId = leonardo, Ruolo = OperaRuoli.Autore }, new OperaPersona { PersonaId = lisa, Ruolo = OperaRuoli.Soggetto }],
                });
                store.SaveArtwork(new Opera { Titolo = "Annunciazione", Persone = [new OperaPersona { PersonaId = leonardo, Ruolo = OperaRuoli.Autore }] });
                var evento = store.SaveEvent(new Evento
                {
                    Titolo = "Arrivo in Francia",
                    Tipo = "Viaggio",
                    Data1 = "1516",
                    Partecipanti = [new EventoPartecipante { PersonaId = leonardo }],
                });
                store.SaveEvent(new Evento { Titolo = "Matrimonio di Lisa", Tipo = "Matrimonio", Partecipanti = [new EventoPartecipante { PersonaId = lisa }] });

                var artwork = new ArtworkViewWindow(store, gioconda);
                Assert.Equal("La Gioconda", artwork.HeaderText.Text);
                Assert.Equal(2, artwork.PeopleList.Items.Count);
                Assert.Single(artwork.OtherWorksList.Items);
                Assert.Equal(Visibility.Visible, artwork.NotesCard.Visibility);
                Assert.Equal(Visibility.Collapsed, artwork.ImageCard.Visibility);
                artwork.Close();

                var eventView = new EventViewWindow(store, evento);
                Assert.Equal("Arrivo in Francia", eventView.HeaderText.Text);
                Assert.Single(eventView.PeopleList.Items);
                Assert.Equal(Visibility.Collapsed, eventView.DescriptionCard.Visibility);
                eventView.Close();

                var events = new EventsSection();
                events.Activate(store);
                Assert.Equal(2, events.EventsGrid.Items.Count);
                events.ShowDynasty(store.GetDynasties().Single().Id);
                Assert.Single(events.EventsGrid.Items);
                events.SearchBox.Text = "lisa";
                Assert.Empty(events.EventsGrid.Items);

                var artworks = new ArtworksSection();
                artworks.Activate(store);
                Assert.Equal(2, artworks.Gallery.Items.Count);
                artworks.SearchBox.Text = "ritratto";
                Assert.Single(artworks.Gallery.Items);

                var dynasties = new DynastiesSection();
                dynasties.Activate(store);
                Assert.Single(dynasties.MembersGrid.Items);

                var offices = new OfficesSection();
                offices.Activate(store);
                Assert.Empty(offices.OfficeList.Items);
                Assert.Equal(Visibility.Visible, offices.EmptyText.Visibility);

                var main = new MainWindow(store);
                foreach (var (tab, panel) in new (System.Windows.Controls.RadioButton, UIElement)[]
                {
                    (main.DynastiesTab, main.DynastiesPanel), (main.OfficesTab, main.OfficesPanel), (main.EventsTab, main.EventsPanel),
                    (main.ArtworksTab, main.ArtworksPanel), (main.PlacesTab, main.PlacesPanel), (main.PeopleTab, main.PeoplePanel),
                })
                {
                    tab.IsChecked = true;
                    Assert.Equal(Visibility.Visible, panel.Visibility);
                }

                Assert.Equal(2, main.EventsPanel.EventsGrid.Items.Count);
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
