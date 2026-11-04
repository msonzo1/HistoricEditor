using System.IO;
using System.Windows;
using System.Windows.Media;

namespace HistoricEditor;

// Scheda di lettura di un'opera d'arte: immagine, dati, persone collegate e altre opere degli stessi artisti.
public partial class ArtworkViewWindow : Window
{
    private sealed record OtherWork(int Id, string Titolo, string Etichetta, ImageSource? Miniatura)
    {
        public bool HasMiniatura => Miniatura is not null;
    }

    private readonly PeopleStore store;
    private readonly int artworkId;
    private Opera? opera;

    // Vero se dalla scheda (o da finestre aperte da qui) sono stati salvati cambiamenti all'archivio.
    public bool Changed { get; private set; }

    public ArtworkViewWindow(PeopleStore store, int artworkId)
    {
        InitializeComponent();
        this.store = store;
        this.artworkId = artworkId;
        Load();
        Loaded += (_, _) => EditButton.Focus();
    }

    private void Load()
    {
        opera = store.GetArtwork(artworkId);
        if (opera is null)
        {
            if (IsLoaded)
            {
                Close();
            }

            return;
        }

        var names = store.GetPersonSummaries().ToDictionary(p => p.Id, p => p.Nome);
        string NameOf(int id) => names.GetValueOrDefault(id, $"Scheda {id}");

        Title = $"Opera d'arte – {opera.Titolo}";
        HeaderText.Text = opera.Titolo;
        HeaderSubtitle.Text = ArtworkSheet.Subtitle(opera, NameOf);

        var image = ArtworkImages.LoadThumbnail(store.ImagesDirectory, opera.Immagine);
        ArtworkImage.Source = image;
        System.Windows.Automation.AutomationProperties.SetName(ArtworkImage, $"Immagine di {opera.Titolo}");
        ImageCard.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;

        var place = opera.LuogoId is null && opera.LuogoSpecificoId is null
            ? string.Empty
            : store.DescribeRoleAt(opera.LuogoId, opera.LuogoSpecificoId, HistoricalYear.Parse(opera.Data)).DisplayText;
        var fields = ArtworkSheet.MainFields(opera, place);
        MainList.ItemsSource = fields;
        MainEmptyText.Visibility = fields.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var people = ArtworkSheet.People(opera, NameOf);
        PeopleList.ItemsSource = people;
        NoPeopleText.Visibility = people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        NotesText.Text = opera.Note;
        NotesCard.Visibility = RichNotes.IsEmpty(opera.Note) ? Visibility.Collapsed : Visibility.Visible;
        AttachmentsView.Load(store, AmbitiAllegato.Opera, artworkId);

        var others = ArtworkSheet.SameArtists(opera, store.GetArtworks())
            .Select(o =>
            {
                var detail = string.Join(" · ", new[] { ArtworkSheet.Artists(o, NameOf), o.Data.Trim() }.Where(s => s.Length > 0));
                return new OtherWork(o.Id, o.Titolo, detail.Length > 0 ? $"{o.Titolo} — {detail}" : o.Titolo,
                    ArtworkImages.LoadThumbnail(store.ImagesDirectory, o.Immagine));
            })
            .ToList();
        OtherWorksList.ItemsSource = others;
        OtherWorksCard.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var sources = RecordSheet.Sources(opera.Fonti);
        if (RecordSheet.WikidataUrl(opera.WikidataId) is { Length: > 0 } wikidata && sources.All(s => s.Url != wikidata))
        {
            sources = [.. sources, new SheetSource(wikidata, wikidata)];
        }

        SourcesList.ItemsSource = sources;
        NoSourcesText.Visibility = sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var path = opera is null ? string.Empty : ArtworkImages.FullPath(store.ImagesDirectory, opera.Immagine);
        if (path.Length == 0 || !File.Exists(path) || !RecordNavigator.OpenExternal(path))
        {
            Feedback.Error(StatusMessage, "Impossibile aprire l'immagine.");
        }
    }

    private void OpenPerson_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int id } && RecordNavigator.ViewPerson(this, store, id))
        {
            Changed = true;
            Load();
        }
    }

    private void OpenOtherWork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OtherWork work } && RecordNavigator.ViewArtwork(this, store, work.Id))
        {
            Changed = true;
            Load();
        }
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SheetSource { Url: { } url } } && !RecordNavigator.OpenExternal(url))
        {
            Feedback.Error(StatusMessage, "Impossibile aprire il collegamento nel browser.");
        }
    }

    private void ManageAttachments_Click(object sender, RoutedEventArgs e)
    {
        var window = new AttachmentsWindow(store, AmbitiAllegato.Opera, artworkId, HeaderText.Text) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            Changed = true;
            Load();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        RecordNavigator.EditArtwork(this, store, artworkId);
        Changed = true;
        Load();
        if (IsVisible)
        {
            Feedback.Success(StatusMessage, "Scheda aggiornata.");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
