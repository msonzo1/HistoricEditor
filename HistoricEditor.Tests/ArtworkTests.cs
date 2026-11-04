using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class ArtworkTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-art-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(params string[] dirs)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var dir in dirs)
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // PNG semitrasparente di prova, più grande del lato massimo consentito.
    private static string WritePng(string dir, int width, int height)
    {
        var path = Path.Combine(dir, "originale.png");
        WpfTestThread.Run(() =>
        {
            var pixels = new byte[width * height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 200;
                pixels[i + 3] = 64;
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        });
        return path;
    }

    private static (int Width, int Height) ReadSize(string path)
    {
        var size = (0, 0);
        WpfTestThread.Run(() =>
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            size = (frame.PixelWidth, frame.PixelHeight);
        });
        return size;
    }

    [Theory]
    [InlineData(4000, 3000, 2000, 2000, 1500)]
    [InlineData(1000, 3000, 480, 160, 480)]
    // Le immagini già piccole non vengono ingrandite.
    [InlineData(800, 600, 2000, 800, 600)]
    public void Fit_KeepsAspectRatioWithoutUpscaling(int width, int height, int max, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), ArtworkImages.Fit(width, height, max));

    [Fact]
    public void Import_CompressesToJpegWithThumbnail_AndLeavesOriginalUntouched()
    {
        var dir = NewTempDir();
        try
        {
            var source = WritePng(dir, 3000, 1500);
            var originalBytes = File.ReadAllBytes(source);
            var images = Path.Combine(dir, "Immagini");

            var name = ArtworkImages.Import(source, images);

            Assert.Matches("^[0-9a-f]{32}\\.jpg$", name);
            Assert.Equal((ArtworkImages.MaxSide, 1000), ReadSize(ArtworkImages.FullPath(images, name)));
            Assert.Equal((ArtworkImages.ThumbnailSide, 240), ReadSize(ArtworkImages.ThumbnailPath(images, name)));
            Assert.Equal(originalBytes, File.ReadAllBytes(source));
            BitmapSource? thumbnail = null;
            WpfTestThread.Run(() => thumbnail = ArtworkImages.LoadThumbnail(images, name));
            Assert.NotNull(thumbnail);
            Assert.True(thumbnail!.IsFrozen);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Import_RejectsFilesThatAreNotImages()
    {
        var dir = NewTempDir();
        try
        {
            var source = Path.Combine(dir, "falso.jpg");
            File.WriteAllText(source, "non sono un'immagine");
            var images = Path.Combine(dir, "Immagini");

            Assert.Throws<InvalidOperationException>(() => ArtworkImages.Import(source, images));
            Assert.False(Directory.Exists(images) && Directory.EnumerateFiles(images).Any());
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void DeleteAndRemoveOrphans_TouchOnlyOwnFiles()
    {
        var dir = NewTempDir();
        try
        {
            const string used = "0123456789abcdef0123456789abcdef.jpg";
            const string orphan = "fedcba9876543210fedcba9876543210.jpg";
            foreach (var file in new[] { used, "0123456789abcdef0123456789abcdef.thumb.jpg", orphan, "fedcba9876543210fedcba9876543210.thumb.jpg", "foto-utente.jpg" })
            {
                File.WriteAllText(Path.Combine(dir, file), "x");
            }

            ArtworkImages.Delete(dir, "foto-utente.jpg");
            ArtworkImages.Delete(dir, "..\\foto-utente.jpg");
            Assert.Equal(2, ArtworkImages.RemoveOrphans(dir, [used]));

            Assert.Equal(
                ["0123456789abcdef0123456789abcdef.jpg", "0123456789abcdef0123456789abcdef.thumb.jpg", "foto-utente.jpg"],
                Directory.EnumerateFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));

            ArtworkImages.Delete(dir, used);
            Assert.Equal(["foto-utente.jpg"], Directory.EnumerateFiles(dir).Select(Path.GetFileName));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void DescribeArtists_ListsOnlyMakersInRoleOrder()
    {
        var text = OperaRuoli.DescribeArtists(
        [
            (OperaRuoli.Soggetto, "Lisa Gherardini"),
            (OperaRuoli.Attribuita, "Salaì"),
            (OperaRuoli.Autore, "Leonardo da Vinci"),
            (OperaRuoli.Committente, "Francesco del Giocondo"),
            (OperaRuoli.Bottega, "Verrocchio"),
        ]);

        Assert.Equal("Leonardo da Vinci, bottega di Verrocchio, attribuita a Salaì", text);
    }

    [Fact]
    public void DescribeForPerson_AddsThatPersonsRoles()
    {
        var opera = new Opera
        {
            Titolo = "Autoritratto",
            Data = "1512",
            Tipo = "Disegno",
            Persone =
            [
                new OperaPersona { PersonaId = 1, Ruolo = OperaRuoli.Soggetto },
                new OperaPersona { PersonaId = 1, Ruolo = OperaRuoli.Autore },
                new OperaPersona { PersonaId = 2, Ruolo = OperaRuoli.Committente },
            ],
        };
        string Name(int id) => id == 1 ? "Leonardo" : "Francesco";

        Assert.Equal("Autoritratto — Leonardo · 1512 · Disegno (autore, soggetto raffigurato)", OperaRuoli.DescribeForPerson(opera, 1, Name));
        Assert.Equal("Autoritratto — Leonardo · 1512 · Disegno (committente)", OperaRuoli.DescribeForPerson(opera, 2, Name));
    }

    [Fact]
    public void Store_SavesArtworksWithPeople_AndCleansUp()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var leonardo = store.CreatePerson("Leonardo da Vinci", Sessi.Maschio);
            var lisa = store.CreatePerson("Lisa Gherardini", Sessi.Femmina);

            Assert.Throws<InvalidOperationException>(() => store.SaveArtwork(new Opera { Titolo = "  " }));
            Assert.Throws<InvalidOperationException>(() => store.SaveArtwork(new Opera { Titolo = "X", Persone = [new OperaPersona { PersonaId = leonardo, Ruolo = "pittore" }] }));
            Assert.Throws<InvalidOperationException>(() => store.SaveArtwork(new Opera { Titolo = "X", Persone = [new OperaPersona { PersonaId = 9999, Ruolo = OperaRuoli.Autore }] }));

            var id = store.SaveArtwork(new Opera
            {
                Titolo = " La Gioconda ",
                Tipo = "Dipinto",
                Data = "1503-1519",
                WikidataId = " q12418 ",
                Persone =
                [
                    new OperaPersona { PersonaId = leonardo, Ruolo = OperaRuoli.Autore },
                    new OperaPersona { PersonaId = leonardo, Ruolo = OperaRuoli.Autore },
                    new OperaPersona { PersonaId = lisa, Ruolo = OperaRuoli.Soggetto },
                ],
            });

            var saved = Assert.Single(store.GetArtworks());
            Assert.Equal("La Gioconda", saved.Titolo);
            Assert.Equal("Q12418", saved.WikidataId);
            Assert.Equal(2, saved.Persone.Count);
            Assert.Single(store.GetArtworksForPerson(lisa));
            Assert.Contains("Dipinto", store.GetArtworkTypes());

            store.Delete(lisa);
            Assert.Equal([leonardo], store.GetArtworks().Single().Persone.Select(p => p.PersonaId));
            Assert.Empty(store.GetArtworksForPerson(lisa));

            store.DeleteArtwork(id);
            Assert.Empty(store.GetArtworks());
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Store_ReplacingOrDeletingArtworkImage_RemovesOldFiles()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var source = WritePng(dir, 64, 48);
            var first = ArtworkImages.Import(source, store.ImagesDirectory);
            var id = store.SaveArtwork(new Opera { Titolo = "David", Immagine = first });

            var second = ArtworkImages.Import(source, store.ImagesDirectory);
            var unsaved = ArtworkImages.Import(source, store.ImagesDirectory);
            store.SaveArtwork(new Opera { Id = id, Titolo = "David", Immagine = second });
            Assert.False(File.Exists(ArtworkImages.FullPath(store.ImagesDirectory, first)));
            Assert.False(File.Exists(ArtworkImages.ThumbnailPath(store.ImagesDirectory, first)));

            // Un'immagine scelta ma mai salvata viene rimossa dalla pulizia.
            Assert.Equal(2, store.RemoveUnusedArtworkImages());
            Assert.False(File.Exists(ArtworkImages.FullPath(store.ImagesDirectory, unsaved)));
            Assert.True(File.Exists(ArtworkImages.FullPath(store.ImagesDirectory, second)));

            store.DeleteArtwork(id);
            Assert.Empty(Directory.EnumerateFiles(store.ImagesDirectory));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Backup_RoundTrip_RestoresArtworks()
    {
        var sourceDir = NewTempDir();
        var restoreDir = NewTempDir();
        try
        {
            using (var store = new PeopleStore(sourceDir))
            {
                var michelangelo = store.CreatePerson("Michelangelo", Sessi.Maschio);
                store.SaveArtwork(new Opera
                {
                    Titolo = "David",
                    Tipo = "Scultura",
                    Tecnica = "Marmo",
                    Fonti = "Vasari, Le vite",
                    Persone = [new OperaPersona { PersonaId = michelangelo, Ruolo = OperaRuoli.Autore }],
                });
                store.ExportJson(store.LoadAll());
            }

            File.Copy(Path.Combine(sourceDir, "people.json"), Path.Combine(restoreDir, "people.json"));

            using var restored = new PeopleStore(restoreDir);
            restored.LoadAll();
            var opera = Assert.Single(restored.GetArtworks());
            Assert.Equal("David", opera.Titolo);
            Assert.Equal("Marmo", opera.Tecnica);
            Assert.Equal("Vasari, Le vite", opera.Fonti);
            var autore = Assert.Single(opera.Persone);
            Assert.Equal("Michelangelo", restored.GetPerson(autore.PersonaId)?.Name);
            Assert.Equal(OperaRuoli.Autore, autore.Ruolo);
        }
        finally
        {
            Cleanup(sourceDir, restoreDir);
        }
    }

    [Fact]
    public void Windows_ShowArtworkOfPerson()
    {
        var dir = NewTempDir();
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var leonardo = store.CreatePerson("Leonardo da Vinci", Sessi.Maschio);
                var lisa = store.CreatePerson("Lisa Gherardini", Sessi.Femmina);
                store.SaveArtwork(new Opera { Titolo = "Annunciazione", Persone = [new OperaPersona { PersonaId = leonardo, Ruolo = OperaRuoli.Autore }] });
                var id = store.SaveArtwork(new Opera
                {
                    Titolo = "La Gioconda",
                    Persone =
                    [
                        new OperaPersona { PersonaId = leonardo, Ruolo = OperaRuoli.Autore },
                        new OperaPersona { PersonaId = lisa, Ruolo = OperaRuoli.Soggetto },
                    ],
                });

                var window = new ArtworksWindow(store, lisa, id);
                Assert.Single(window.ArtworkList.Items);
                Assert.Equal("La Gioconda", window.TitleBox.Text);
                Assert.Equal(2, window.PeopleList.Items.Count);
                window.PersonFilter.IsChecked = false;
                Assert.Equal(2, window.ArtworkList.Items.Count);
                window.Close();

                var view = new PersonViewWindow(store, store.GetPerson(lisa)!);
                Assert.Single(view.ArtworksList.Items);
                Assert.Equal(Visibility.Collapsed, view.NoArtworksText.Visibility);
                view.Close();
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Settings_UpdateKeepsOtherPreferences()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            Assert.Equal(PersonOpenMode.Edit, SettingsStore.Load(path).PersonOpenMode);

            SettingsStore.Update(s => s.Theme = nameof(AppTheme.Dark), path);
            SettingsStore.Update(s => s.OpenMode = nameof(PersonOpenMode.View), path);

            var settings = SettingsStore.Load(path);
            Assert.Equal(nameof(AppTheme.Dark), settings.Theme);
            Assert.Equal(PersonOpenMode.View, settings.PersonOpenMode);

            File.WriteAllText(path, "{\"Theme\":\"Dark\",\"OpenMode\":\"boh\"}");
            Assert.Equal(PersonOpenMode.Edit, SettingsStore.Load(path).PersonOpenMode);
        }
        finally
        {
            Cleanup(dir);
        }
    }
}
