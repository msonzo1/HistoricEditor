using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HistoricEditor;

// Sezione Opere della finestra principale: galleria con miniature, ricerca e filtri per tipo e artista.
public partial class ArtworksSection : UserControl
{
    public sealed record ArtworkTile(ArtworkRow Row, ImageSource? Miniatura);

    private PeopleStore? store;
    private IReadOnlyList<ArtworkRow> rows = [];
    private readonly Dictionary<string, ImageSource?> thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private bool loading;

    // Sollevato quando dalla sezione si sono potute cambiare schede di personaggi.
    public event EventHandler? DataChanged;

    public ArtworksSection()
    {
        InitializeComponent();
    }

    private Window Owner => Window.GetWindow(this);

    private ArtworkRow? Selected => (Gallery.SelectedItem as ArtworkTile)?.Row;

    private int? SelectedArtistId => (ArtistCombo.SelectedItem as ChoiceOption)?.Id;

    public void Activate(PeopleStore peopleStore)
    {
        store = peopleStore;
        Reload(Selected?.Id);
    }

    private void Reload(int? selectId)
    {
        if (store is null)
        {
            return;
        }

        loading = true;
        var names = store.GetPersonSummaries().ToDictionary(p => p.Id, p => p.Nome);
        string NameOf(int id) => names.GetValueOrDefault(id, $"Scheda {id}");
        var works = store.GetArtworks();
        rows = ArtworkGallery.Rows(works, NameOf);
        thumbnails.Clear();

        var type = (TypeCombo.SelectedItem as ChoiceOption)?.Valore;
        TypeCombo.ItemsSource = new[] { new ChoiceOption("Tutti i tipi") }
            .Concat(store.GetArtworkTypes().Where(t => t.Trim().Length > 0).Select(t => new ChoiceOption(t, Valore: t)))
            .ToList();
        TypeCombo.SelectedItem = TypeCombo.Items.OfType<ChoiceOption>().FirstOrDefault(o => o.Valore == type) ?? TypeCombo.Items[0];

        var artistId = SelectedArtistId;
        ArtistCombo.ItemsSource = new[] { new ChoiceOption("Tutti gli artisti") }
            .Concat(ArtworkGallery.Artists(works, NameOf).Select(a => new ChoiceOption(a.Etichetta, a.Id)))
            .ToList();
        ArtistCombo.SelectedItem = ArtistCombo.Items.OfType<ChoiceOption>().FirstOrDefault(o => o.Id == artistId) ?? ArtistCombo.Items[0];
        loading = false;

        ApplyFilter(selectId);
    }

    private ImageSource? Thumbnail(string image)
    {
        if (store is null || string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        if (!thumbnails.TryGetValue(image, out var source))
        {
            source = ArtworkImages.LoadThumbnail(store.ImagesDirectory, image);
            thumbnails[image] = source;
        }

        return source;
    }

    private void ApplyFilter(int? selectId)
    {
        if (loading || store is null)
        {
            return;
        }

        var visible = ArtworkGallery.Filter(rows, SearchBox.Text, (TypeCombo.SelectedItem as ChoiceOption)?.Valore, SelectedArtistId)
            .Select(r => new ArtworkTile(r, Thumbnail(r.Immagine)))
            .ToList();
        Gallery.ItemsSource = visible;
        Gallery.SelectedItem = visible.FirstOrDefault(t => t.Row.Id == selectId);
        if (Gallery.SelectedItem is not null)
        {
            Gallery.ScrollIntoView(Gallery.SelectedItem);
        }

        CountText.Text = visible.Count == rows.Count
            ? $"{rows.Count} {(rows.Count == 1 ? "opera" : "opere")}"
            : $"{visible.Count} di {rows.Count} opere";
        EmptyText.Text = rows.Count == 0
            ? "Ancora nessuna opera.\n\nUsa «Nuova» per aggiungere dipinti, sculture e affreschi, anche importandoli da Wikipedia o dalle collezioni dei musei."
            : "Nessuna opera corrisponde ai filtri.";
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        ViewButton.IsEnabled = Selected is not null;
        EditButton.IsEnabled = Selected is not null;
        ArtistButton.IsEnabled = SelectedArtistId is not null;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter(Selected?.Id);

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        loading = true;
        SearchBox.Text = string.Empty;
        TypeCombo.SelectedIndex = 0;
        ArtistCombo.SelectedIndex = 0;
        loading = false;
        ApplyFilter(Selected?.Id);
        SearchBox.Focus();
    }

    private void Gallery_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommands();

    private void Gallery_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Gallery, source) is ListBoxItem)
        {
            Open(RecordNavigator.OpenArtwork);
        }
    }

    private void Gallery_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected is not null)
        {
            e.Handled = true;
            Open(RecordNavigator.OpenArtwork);
        }
    }

    private void View_Click(object sender, RoutedEventArgs e) => Open(RecordNavigator.ViewArtwork);

    private void Edit_Click(object sender, RoutedEventArgs e) => Open((owner, s, id) => RecordNavigator.EditArtwork(owner, s, id));

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (store is not null && RecordNavigator.EditArtwork(Owner, store, null))
        {
            AfterChange(Selected?.Id);
        }
    }

    private void OpenArtist_Click(object sender, RoutedEventArgs e)
    {
        if (store is not null && SelectedArtistId is int id && RecordNavigator.OpenPerson(Owner, store, id))
        {
            AfterChange(Selected?.Id);
        }
    }

    private void Open(Func<Window, PeopleStore, int, bool> open)
    {
        if (store is not null && Selected is { } row && open(Owner, store, row.Id))
        {
            AfterChange(row.Id);
        }
    }

    private void AfterChange(int? selectId)
    {
        Reload(selectId);
        DataChanged?.Invoke(this, EventArgs.Empty);
        Gallery.Focus();
    }
}
