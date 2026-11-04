using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace HistoricEditor;

public partial class ArtworksWindow : Window
{
    private sealed record ArtworkItem(Opera Opera, string Artisti)
    {
        public string Dettaglio => string.Join(" · ", new[] { Artisti, Opera.Data, Opera.Tipo }.Where(s => s.Length > 0));

        public string Etichetta => Dettaglio.Length > 0 ? $"{Opera.Titolo}, {Dettaglio}" : Opera.Titolo;
    }

    private sealed class PersonRow(int personaId, string nome, string ruolo) : INotifyPropertyChanged
    {
        private string ruolo = ruolo;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int PersonaId { get; } = personaId;
        public string Nome { get; } = nome;

        public string Ruolo
        {
            get => ruolo;
            set
            {
                ruolo = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ruolo)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Descrizione)));
            }
        }

        public string Descrizione => $"{Nome}, {OperaRuoli.Info(Ruolo).Etichetta}";
    }

    private readonly PeopleStore store;
    private readonly int? focusPersonId;
    private readonly ObservableCollection<PersonRow> rows = [];
    private Dictionary<int, PersonSummary> people = [];
    private List<ArtworkItem> items = [];
    private int currentId;
    private int? placeId;
    private int? specificPlaceId;
    private string image = string.Empty;
    private string wikidataId = string.Empty;

    // Vero se è stata creata almeno una nuova scheda dal selettore o modificata dalla scheda di lettura.
    public bool PeopleChanged { get; private set; }

    // Con focusPersonId l'elenco può limitarsi alle opere della persona, che diventa autore delle nuove opere.
    public ArtworksWindow(PeopleStore store, int? focusPersonId = null, int? selectArtworkId = null)
    {
        InitializeComponent();
        this.store = store;
        this.focusPersonId = focusPersonId;
        PeopleList.ItemsSource = rows;
        InitializeImport();
        Closed += (_, _) => store.RemoveUnusedArtworkImages();
        LoadPeople();
        if (focusPersonId is int id && people.TryGetValue(id, out var person))
        {
            PersonFilter.Content = $"Solo opere collegate a {person.Nome}";
            PersonFilter.Visibility = Visibility.Visible;
            PersonFilter.IsChecked = true;
        }

        Reload(selectArtworkId);
        Loaded += (_, _) =>
        {
            if (selectArtworkId is null && focusPersonId is not null && ArtworkList.Items.Count == 0)
            {
                TitleBox.Focus();
            }
            else
            {
                SearchBox.Focus();
            }
        };
    }

    private void LoadPeople() => people = store.GetPersonSummaries().ToDictionary(p => p.Id);

    private string PersonLabel(int personId) => people.TryGetValue(personId, out var p) ? p.Etichetta : $"Scheda {personId}";

    private string PersonName(int personId) => people.TryGetValue(personId, out var p) ? p.Nome : $"Scheda {personId}";

    private void Reload(int? selectId)
    {
        items = [.. store.GetArtworks().Select(o => new ArtworkItem(o, OperaRuoli.DescribeArtists(o.Persone.Select(p => (p.Ruolo, PersonName(p.PersonaId))))))];
        TypeBox.ItemsSource = store.GetArtworkTypes();
        ApplyFilter(selectId);
    }

    private void ApplyFilter(int? selectId)
    {
        var query = SearchBox.Text.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        bool Matches(string text) => compare.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

        var visible = items.Where(i =>
                (query.Length == 0 || Matches(i.Opera.Titolo) || Matches(i.Opera.Tipo) || Matches(i.Opera.Tecnica) || Matches(i.Artisti)
                    || i.Opera.Persone.Any(p => Matches(PersonName(p.PersonaId))))
                && (PersonFilter.IsChecked != true || i.Opera.Persone.Any(p => p.PersonaId == focusPersonId)))
            .ToList();
        ArtworkList.ItemsSource = visible;
        var selected = visible.FirstOrDefault(i => i.Opera.Id == selectId);
        if (selected is not null)
        {
            ArtworkList.SelectedItem = selected;
            ArtworkList.ScrollIntoView(selected);
        }
        else if (selectId is null)
        {
            ShowArtwork(null);
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded || sender == PersonFilter)
        {
            ApplyFilter(currentId == 0 ? null : currentId);
        }
    }

    private void ArtworkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArtworkList.SelectedItem is ArtworkItem item)
        {
            ShowArtwork(item.Opera);
        }
    }

    private void ShowArtwork(Opera? opera)
    {
        currentId = opera?.Id ?? 0;
        TitleBox.Text = opera?.Titolo ?? string.Empty;
        TypeBox.Text = opera?.Tipo ?? string.Empty;
        DateBox.Text = opera?.Data ?? string.Empty;
        TechniqueBox.Text = opera?.Tecnica ?? string.Empty;
        SizeBox.Text = opera?.Dimensioni ?? string.Empty;
        NotesBox.Text = opera?.Note ?? string.Empty;
        SourcesBox.Text = opera?.Fonti ?? string.Empty;
        wikidataId = opera?.WikidataId ?? string.Empty;
        SetPlace(opera?.LuogoId, opera?.LuogoSpecificoId);
        SetImage(opera?.Immagine ?? string.Empty, string.Empty);
        rows.Clear();
        if (opera is not null)
        {
            foreach (var p in opera.Persone
                .OrderBy(p => OperaRuoli.Tutti.ToList().FindIndex(r => r.Codice == p.Ruolo))
                .ThenBy(p => PersonLabel(p.PersonaId), StringComparer.CurrentCultureIgnoreCase))
            {
                rows.Add(new PersonRow(p.PersonaId, PersonLabel(p.PersonaId), p.Ruolo));
            }
        }
        else if (focusPersonId is int id && people.ContainsKey(id))
        {
            rows.Add(new PersonRow(id, PersonLabel(id), OperaRuoli.Autore));
        }

        DeleteButton.IsEnabled = opera is not null;
        AttachmentsButton.IsEnabled = opera is not null;
        EditorMessage.Text = string.Empty;
    }

    private void SetPlace(int? luogoId, int? specificoId)
    {
        placeId = luogoId;
        specificPlaceId = specificoId;
        PlaceBox.Text = luogoId is null && specificoId is null ? string.Empty : store.DescribeRole(luogoId, specificoId).DisplayText;
        ClearPlaceButton.IsEnabled = luogoId is not null || specificoId is not null;
    }

    private void ChoosePlace_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PlacePickerWindow(store, "Collocazione dell’opera", allowSpecific: true, placeId, specificPlaceId, allowClear: true) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            SetPlace(picker.ResultLuogoId, picker.ResultSpecificoId);
            PlaceBox.Focus();
        }
    }

    private void ClearPlace_Click(object sender, RoutedEventArgs e)
    {
        SetPlace(null, null);
        PlaceBox.Focus();
    }

    private void SetImage(string name, string info)
    {
        image = name;
        var preview = ArtworkImages.LoadThumbnail(store.ImagesDirectory, name);
        Preview.Source = preview;
        NoImageText.Text = name.Length > 0 && preview is null ? "Immagine non trovata nella cartella dell’archivio" : "Nessuna immagine";
        NoImageText.Visibility = preview is null ? Visibility.Visible : Visibility.Collapsed;
        OpenImageButton.IsEnabled = preview is not null;
        RemoveImageButton.IsEnabled = name.Length > 0;
        ImageInfo.Text = info;
    }

    private async void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Scegli l’immagine dell’opera",
            Filter = ArtworkImages.FileFilter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var source = dialog.FileName;
        var directory = store.ImagesDirectory;
        ChooseImageButton.IsEnabled = false;
        ImageInfo.Text = "Compressione dell’immagine in corso…";
        Cursor = Cursors.Wait;
        try
        {
            var name = await Task.Run(() => ArtworkImages.Import(source, directory));
            var original = new FileInfo(source).Length;
            var compressed = new FileInfo(ArtworkImages.FullPath(directory, name)).Length;
            SetImage(name, $"Copia compressa: {FormatSize(compressed)} (originale {FormatSize(original)}). Salva l’opera per confermare.");
            EditorMessage.Text = string.Empty;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ImageInfo.Text = string.Empty;
            EditorMessage.Text = ex is InvalidOperationException ? ex.Message : "Impossibile leggere o copiare l’immagine.";
        }
        finally
        {
            ChooseImageButton.IsEnabled = true;
            Cursor = null;
        }
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024d / 1024d:0.0} MB")
            : string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, bytes / 1024)} KB");

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var path = ArtworkImages.FullPath(store.ImagesDirectory, image);
        if (image.Length == 0 || !File.Exists(path))
        {
            EditorMessage.Text = "Immagine non trovata.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            EditorMessage.Text = "Impossibile aprire l’immagine.";
        }
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        SetImage(string.Empty, "Immagine rimossa: salva l’opera per confermare.");
        ChooseImageButton.Focus();
    }

    private void PeopleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemovePersonButton.IsEnabled = PeopleList.SelectedItem is not null;
        ViewPersonButton.IsEnabled = PeopleList.SelectedItem is not null;
    }

    private void AddPerson_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PersonPickerWindow(store, "Aggiungi persona collegata all’opera", 0, string.Empty) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedId is not int id)
        {
            return;
        }

        if (!people.ContainsKey(id))
        {
            PeopleChanged = true;
            LoadPeople();
        }

        // La stessa persona può avere più ruoli: si propone il primo ruolo non ancora usato.
        var used = rows.Where(r => r.PersonaId == id).Select(r => r.Ruolo).ToHashSet();
        var ruolo = OperaRuoli.Tutti.FirstOrDefault(r => !used.Contains(r.Codice))?.Codice;
        if (ruolo is null)
        {
            EditorMessage.Text = $"{PersonLabel(id)} ha già tutti i ruoli.";
            return;
        }

        var row = new PersonRow(id, PersonLabel(id), ruolo);
        rows.Add(row);
        PeopleList.SelectedItem = row;
        PeopleList.ScrollIntoView(row);
        EditorMessage.Text = string.Empty;
    }

    private void RemovePerson_Click(object sender, RoutedEventArgs e)
    {
        if (PeopleList.SelectedItem is PersonRow row)
        {
            var index = rows.IndexOf(row);
            rows.Remove(row);
            if (rows.Count > 0)
            {
                PeopleList.SelectedIndex = Math.Min(index, rows.Count - 1);
            }
        }
    }

    private void ViewPerson_Click(object sender, RoutedEventArgs e)
    {
        if (PeopleList.SelectedItem is not PersonRow row || store.GetPerson(row.PersonaId) is not { } person)
        {
            return;
        }

        var view = new PersonViewWindow(store, person) { Owner = this };
        view.ShowDialog();
        if (view.Changed)
        {
            PeopleChanged = true;
            LoadPeople();
            Reload(currentId == 0 ? null : currentId);
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        ArtworkList.SelectedItem = null;
        ShowArtwork(null);
        TitleBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var duplicate = rows.GroupBy(r => (r.PersonaId, r.Ruolo)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            EditorMessage.Text = $"{duplicate.First().Nome} compare due volte con lo stesso ruolo.";
            return;
        }

        var data = new Opera
        {
            Id = currentId,
            Titolo = TitleBox.Text,
            Tipo = TypeBox.Text,
            Data = DateBox.Text,
            Tecnica = TechniqueBox.Text,
            Dimensioni = SizeBox.Text,
            LuogoId = placeId,
            LuogoSpecificoId = specificPlaceId,
            Note = NotesBox.Text,
            Fonti = SourcesBox.Text,
            Immagine = image,
            WikidataId = wikidataId,
            Persone = [.. rows.Select(r => new OperaPersona { PersonaId = r.PersonaId, Ruolo = r.Ruolo })],
        };

        try
        {
            var id = store.SaveArtwork(data);
            currentId = id;
            Reload(id);
            if (ArtworkList.SelectedItem is null)
            {
                // L'opera salvata non rientra nel filtro attivo: si azzera la ricerca per mostrarla.
                SearchBox.Text = string.Empty;
                PersonFilter.IsChecked = false;
                ApplyFilter(id);
            }

            EditorMessage.Text = "Opera salvata.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            EditorMessage.Text = ex is InvalidOperationException ? ex.Message : "Impossibile salvare l’opera.";
        }
    }

    private void Attachments_Click(object sender, RoutedEventArgs e)
    {
        if (currentId != 0)
        {
            new AttachmentsWindow(store, AmbitiAllegato.Opera, currentId, TitleBox.Text.Trim()) { Owner = this }.ShowDialog();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (currentId == 0)
        {
            return;
        }

        var answer = MessageBox.Show(this, $"Eliminare l’opera «{TitleBox.Text}» e la sua immagine? Le schede delle persone collegate non vengono modificate.", "Elimina opera", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            store.DeleteArtwork(currentId);
            currentId = 0;
            Reload(null);
            EditorMessage.Text = "Opera eliminata.";
        }
        catch (DbUpdateException)
        {
            EditorMessage.Text = "Impossibile eliminare l’opera.";
        }
    }
}
