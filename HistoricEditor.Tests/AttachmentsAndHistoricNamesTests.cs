using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HistoricEditor;
using Xunit;

namespace HistoricEditor.Tests;

public class HistoricalPlaceNamesTests
{
    private static readonly List<NomeStorico> Istanbul =
    [
        new() { Nome = "Bisanzio", Dal = -667, Al = 330 },
        new() { Nome = "Costantinopoli", Dal = 330, Al = 1922 },
    ];

    [Theory]
    [InlineData(-500, "Bisanzio")]
    // Nell'anno di passaggio vale il nome nuovo.
    [InlineData(330, "Costantinopoli")]
    [InlineData(1453, "Costantinopoli")]
    [InlineData(1950, null)]
    [InlineData(-800, null)]
    public void NameAt_PicksNameInUseThatYear(int year, string? expected)
        => Assert.Equal(expected, HistoricalPlaceNames.NameAt("Istanbul", Istanbul, year));

    [Fact]
    public void Describe_AddsCurrentNameOnlyWhenDifferent()
    {
        Assert.Equal("Costantinopoli (oggi Istanbul)", HistoricalPlaceNames.Describe("Istanbul", Istanbul, 1500));
        Assert.Equal("Istanbul", HistoricalPlaceNames.Describe("Istanbul", Istanbul, null));
        Assert.Equal("Istanbul", HistoricalPlaceNames.Describe("Istanbul", [new() { Nome = "İstanbul", Dal = 1900, Al = 2000 }], 1950));
    }

    [Fact]
    public void Merge_WidensSameNameAndKeepsManualEntries()
    {
        List<NomeStorico> existing = [new() { Nome = "Costantinopoli", Dal = 400, Al = 1900, Fonte = "Inserito a mano" }, new() { Nome = "Nova Roma" }];
        List<NomeStorico> incoming =
        [
            new() { Nome = "costantinopoli", Dal = 330, Al = 1922, Fonte = "Wikidata" },
            new() { Nome = "Bisanzio", Dal = -667, Al = 330 },
            new() { Nome = "Istanbul", Dal = 1930 },
            new() { Nome = "Senza date" },
        ];

        var merged = HistoricalPlaceNames.Merge(existing, incoming, "Istanbul");

        Assert.Equal(["Nova Roma", "Bisanzio", "Costantinopoli"], merged.Select(n => n.Nome));
        var costantinopoli = merged.Single(n => n.Nome == "Costantinopoli");
        Assert.Equal((330, 1922), (costantinopoli.Dal, costantinopoli.Al));
        Assert.Equal("Inserito a mano", costantinopoli.Fonte);
    }

    [Theory]
    [InlineData("330", true, 330)]
    [InlineData(" -667 ", true, -667)]
    [InlineData("667 a.C.", true, -667)]
    [InlineData("44 a. C.", true, -44)]
    [InlineData("330 d.C.", true, 330)]
    [InlineData("", true, null)]
    [InlineData("0", false, null)]
    [InlineData("-5 a.C.", false, null)]
    [InlineData("circa 300", false, null)]
    public void TryParseYear_AcceptsBcNotation(string text, bool ok, int? expected)
    {
        Assert.Equal(ok, HistoricalPlaceNames.TryParseYear(text, out var year));
        Assert.Equal(expected, year);
    }

    [Fact]
    public void BuildHistoricalNames_ReadsOfficialNamesAndPredecessorChain()
    {
        static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
        static string Time(int year) => "{\"datavalue\":{\"value\":{\"time\":\"" + (year < 0 ? "-" : "+") + Math.Abs(year).ToString("D4") + "-00-00T00:00:00Z\"}}}";
        static string Item(string id) => "{\"mainsnak\":{\"datavalue\":{\"value\":{\"id\":\"" + id + "\"}}},\"rank\":\"normal\"}";
        static string Official(string text, string language, int start, int? end)
            => "{\"mainsnak\":{\"datavalue\":{\"value\":{\"text\":\"" + text + "\",\"language\":\"" + language + "\"}}},\"rank\":\"normal\","
                + "\"qualifiers\":{\"P580\":[" + Time(start) + "]" + (end is int e ? ",\"P582\":[" + Time(e) + "]" : string.Empty) + "}}";
        static string Dated(string property, int year) => "\"" + property + "\":[{\"mainsnak\":" + Time(year) + ",\"rank\":\"normal\"}]";

        var place = Parse("{\"id\":\"Q656\",\"labels\":{\"it\":{\"value\":\"San Pietroburgo\"}},\"claims\":{\"P1448\":["
            + Official("Петроград", "ru", 1914, 1924) + "," + Official("Petrograd", "en", 1914, 1924) + "," + Official("Санкт-Петербург", "ru", 1991, null)
            + "],\"P1365\":[" + Item("Q1") + "]}}");
        var predecessors = new Dictionary<string, JsonElement>
        {
            ["Q1"] = Parse("{\"id\":\"Q1\",\"labels\":{\"it\":{\"value\":\"Leningrado\"}},\"claims\":{" + Dated("P571", 1924) + "," + Dated("P576", 1991) + ",\"P1365\":[" + Item("Q2") + "]}}"),
            ["Q2"] = Parse("{\"id\":\"Q2\",\"labels\":{\"en\":{\"value\":\"Nyenskans\"}},\"claims\":{" + Dated("P576", -50) + "}}"),
        };
        var names = WikipediaImportService.BuildHistoricalNames(place, predecessors);

        Assert.Equal(["Nyenskans", "Petrograd", "Leningrado"], names.Select(n => n.Nome));
        Assert.Equal((1924, 1991), (names[2].Dal, names[2].Al));
        Assert.Equal(-50, names[0].Al);
        Assert.Equal("en", names[1].Lingua);
    }
}

public class RichNotesTests
{
    private const string Ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void IsSafe_RejectsControlsBindingsAndForeignNamespaces()
    {
        Assert.True(RichNotes.IsSafe($"<Section xmlns=\"{Ns}\"><Paragraph><Bold>Ciao</Bold></Paragraph></Section>"));
        Assert.False(RichNotes.IsSafe($"<Section xmlns=\"{Ns}\"><BlockUIContainer><Button /></BlockUIContainer></Section>"));
        Assert.False(RichNotes.IsSafe($"<Section xmlns=\"{Ns}\"><Paragraph Tag=\"{{Binding Path=X}}\">x</Paragraph></Section>"));
        Assert.False(RichNotes.IsSafe($"<Section xmlns=\"{Ns}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"A\"><Paragraph>x</Paragraph></Section>"));
        Assert.False(RichNotes.IsSafe("<Section><Paragraph>x</Paragraph></Section>"));
        Assert.False(RichNotes.IsSafe($"<!DOCTYPE x [<!ENTITY a \"b\">]><Section xmlns=\"{Ns}\"><Paragraph>&a;</Paragraph></Section>"));
    }

    [Fact]
    public void ToPlainText_KeepsParagraphsAndListMarkers()
    {
        var note = $"<Section xmlns=\"{Ns}\"><Paragraph>Titolo</Paragraph>"
            + "<List MarkerStyle=\"Disc\"><ListItem><Paragraph>Uno</Paragraph></ListItem></List>"
            + "<List MarkerStyle=\"Decimal\"><ListItem><Paragraph>Primo</Paragraph></ListItem><ListItem><Paragraph>Secondo</Paragraph></ListItem></List></Section>";

        Assert.Equal("Titolo\n• Uno\n1. Primo\n2. Secondo", RichNotes.ToPlainText(note).ReplaceLineEndings("\n"));
        Assert.Equal("nota semplice", RichNotes.ToPlainText("  nota semplice "));
        Assert.True(RichNotes.IsEmpty($"<Section xmlns=\"{Ns}\"><Paragraph /></Section>"));
        Assert.True(RichNotes.IsEmpty(null));
    }

    [Fact]
    public void Normalize_RemovesThemeColorsAndRootFont()
    {
        var note = RichNotes.Normalize($"<Section xmlns=\"{Ns}\" FontFamily=\"Segoe UI\" FontSize=\"13\" Foreground=\"#FF000000\"><Paragraph Background=\"#FFFFFFFF\"><Run FontWeight=\"Bold\">x</Run></Paragraph></Section>");

        Assert.DoesNotContain("Foreground", note);
        Assert.DoesNotContain("Background", note);
        Assert.DoesNotContain("FontFamily", note);
        Assert.Contains("FontWeight=\"Bold\"", note);
    }

    [Fact]
    public void Editor_RoundTripsFormattingAndReadsOldPlainNotes()
    {
        WpfTestThread.Run(() =>
        {
            var editor = new RichNoteEditor { Text = "Prima riga\nSeconda riga" };
            Assert.Equal("Prima riga\nSeconda riga", RichNotes.ToPlainText(editor.Text).ReplaceLineEndings("\n"));
            Assert.True(RichNotes.IsRich(editor.Text));

            var bold = $"<Section xmlns=\"{Ns}\"><Paragraph><Bold>Importante</Bold></Paragraph></Section>";
            editor.Text = bold;
            Assert.Contains("Bold", editor.Text);
            Assert.Equal("Importante", editor.PlainText);

            // XAML non sicuro: ridotto a testo, mai interpretato.
            editor.Text = $"<Section xmlns=\"{Ns}\"><BlockUIContainer><Button /></BlockUIContainer><Paragraph>Testo</Paragraph></Section>";
            Assert.DoesNotContain("Button", editor.Text);
            Assert.Equal("Testo", editor.PlainText.Trim());

            editor.Text = string.Empty;
            Assert.Equal(string.Empty, editor.Text);
        });
    }
}

public class AttachmentStoreTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"historic-att-{Guid.NewGuid():N}");
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

    private static string WritePng(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        WpfTestThread.Run(() =>
        {
            var pixels = new byte[40 * 30 * 4];
            Array.Fill(pixels, (byte)180);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40, 30, 96, 96, PixelFormats.Bgra32, null, pixels, 40 * 4)));
            using var stream = File.Create(path);
            encoder.Save(stream);
        });
        return path;
    }

    [Fact]
    public void Attachments_FirstImageIsPortrait_OrderAndDeleteCascade()
    {
        var dir = NewTempDir();
        try
        {
            using var store = new PeopleStore(dir);
            var carlo = store.CreatePerson("Carlo V", Sessi.Maschio);
            var document = Path.Combine(dir, "lettera.txt");
            File.WriteAllText(document, "Lettera");

            var letter = store.AddAttachment(AmbitiAllegato.Persona, carlo, document);
            var first = store.AddAttachment(AmbitiAllegato.Persona, carlo, WritePng(dir, "tiziano.png"), "Ritratto di Tiziano");
            var second = store.AddAttachment(AmbitiAllegato.Persona, carlo, WritePng(dir, "seisenegger.png"));

            Assert.False(letter.Immagine);
            Assert.Equal("lettera", letter.Titolo);
            Assert.Equal(first.Id, store.GetPortrait(carlo)?.Id);
            Assert.True(File.Exists(store.AttachmentPath(letter)));
            Assert.Equal("Lettera", File.ReadAllText(store.AttachmentPath(letter)));
            Assert.EndsWith(".jpg", first.File);

            store.TogglePortrait(second.Id);
            Assert.Equal(second.Id, store.GetPortrait(carlo)?.Id);
            store.TogglePortrait(second.Id);
            Assert.Null(store.GetPortrait(carlo));
            Assert.Throws<InvalidOperationException>(() => store.TogglePortrait(letter.Id));

            store.MoveAttachment(second.Id, -1);
            Assert.Equal([letter.Id, second.Id, first.Id], store.GetAttachments(AmbitiAllegato.Persona, carlo).Select(a => a.Id));

            store.UpdateAttachment(letter.Id, " Lettera al figlio ", "Da Yuste", "1557");
            Assert.Equal(("Lettera al figlio", "1557"), (store.GetAttachment(letter.Id)!.Titolo, store.GetAttachment(letter.Id)!.Data));

            Assert.Throws<InvalidOperationException>(() => store.AddAttachment(AmbitiAllegato.Persona, 9999, document));

            var files = store.GetAttachments(AmbitiAllegato.Persona, carlo).Select(store.AttachmentPath).ToList();
            store.Delete(carlo);
            Assert.Empty(store.GetAttachments(AmbitiAllegato.Persona, carlo));
            Assert.All(files, f => Assert.False(File.Exists(f)));
            Assert.True(File.Exists(document));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Backup_RoundTrip_RestoresAttachmentsNotesAndHistoricNames()
    {
        var sourceDir = NewTempDir();
        var restoreDir = NewTempDir();
        try
        {
            using (var store = new PeopleStore(sourceDir))
            {
                var milano = store.EnsurePlace(
                    [new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Lombardia"), new GeoLevel("Provincia", "Milano"), new GeoLevel("Comune", "Milano")],
                    null, null)!.Value;
                store.SetPlaceHistoricNames(milano, [new NomeStorico { Nome = "Mediolanum", Dal = -222, Al = 476, Fonte = "Inserito a mano" }]);
                store.Add(new HistoricalPerson { Name = "Ambrogio", BirthLuogoId = milano, Note = RichNotes.FromPlainText("Vescovo") });
                var document = Path.Combine(sourceDir, "atto.pdf");
                File.WriteAllText(document, "pdf");
                store.AddAttachment(AmbitiAllegato.Luogo, milano, document, "Atto");
                store.ExportJson(store.LoadAll());
            }

            File.Copy(Path.Combine(sourceDir, "people.json"), Path.Combine(restoreDir, "people.json"));

            using var restored = new PeopleStore(restoreDir);
            var person = Assert.Single(restored.LoadAll());
            Assert.Equal("Vescovo", RichNotes.ToPlainText(person.Note));
            Assert.Equal("Mediolanum (oggi Milano), Milano, Lombardia, Italia", restored.DescribePlaceAt(person.BirthLuogoId, 300));
            Assert.Equal("Milano, Milano, Lombardia, Italia", restored.DescribeRoleAt(person.BirthLuogoId, null, 1500).DisplayText);
            Assert.Equal("Atto", Assert.Single(restored.GetAttachments(AmbitiAllegato.Luogo, person.BirthLuogoId!.Value)).Titolo);
        }
        finally
        {
            Cleanup(sourceDir, restoreDir);
        }
    }

    [Fact]
    public void Windows_ShowPortraitNotesAttachmentsAndPeriodName()
    {
        var dir = NewTempDir();
        try
        {
            WpfTestThread.Run(() =>
            {
                using var store = new PeopleStore(dir);
                var milano = store.EnsurePlace(
                    [new GeoLevel("Stato", "Italia"), new GeoLevel("Regione", "Lombardia"), new GeoLevel("Provincia", "Milano"), new GeoLevel("Comune", "Milano")],
                    null, null)!.Value;
                store.SetPlaceHistoricNames(milano, [new NomeStorico { Nome = "Mediolanum", Dal = -222, Al = 476 }]);
                var person = new HistoricalPerson { Name = "Ambrogio", Birth = "340", BirthLuogoId = milano, Note = RichNotes.FromPlainText("Vescovo di Milano") };
                store.Add(person);
                store.AddAttachment(AmbitiAllegato.Persona, person.Id, WritePng(dir, "ritratto.png"));

                var view = new PersonViewWindow(store, store.GetPerson(person.Id)!);
                Assert.Equal(Visibility.Visible, view.PortraitFrame.Visibility);
                Assert.Equal(Visibility.Visible, view.NotesCard.Visibility);
                Assert.Equal(1, view.AttachmentsView.Count);
                Assert.Contains(((IEnumerable<SheetField>)view.LifeList.ItemsSource), f => f.Valore.StartsWith("Mediolanum (oggi Milano)", StringComparison.Ordinal));
                view.Close();

                var attachments = new AttachmentsWindow(store, AmbitiAllegato.Persona, person.Id, "Ambrogio");
                Assert.Single(attachments.AttachmentList.Items);
                Assert.Equal(Visibility.Visible, attachments.PortraitButton.Visibility);
                attachments.Close();

                var editor = new PersonEditorWindow(store, store.GetPerson(person.Id));
                Assert.Equal("Vescovo di Milano", editor.NoteEditor.PlainText);
                Assert.True(editor.AttachmentsButton.IsEnabled);
                editor.Close();
            });
        }
        finally
        {
            Cleanup(dir);
        }
    }
}
