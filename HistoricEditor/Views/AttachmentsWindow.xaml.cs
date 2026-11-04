using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace HistoricEditor;

// Riga dell'elenco allegati (finestra di gestione e schede di lettura).
public sealed record AttachmentItem(Allegato Allegato, BitmapSource? Miniatura)
{
    public int Id => Allegato.Id;

    public string Titolo => Allegato.Titolo.Length > 0 ? Allegato.Titolo : Allegato.NomeOriginale;

    public string Estensione => Miniatura is null ? Path.GetExtension(Allegato.NomeOriginale).TrimStart('.').ToUpperInvariant() : string.Empty;

    public string Dettaglio => string.Join(" · ", new[]
    {
        Allegato.Ritratto ? "Ritratto" : string.Empty,
        Allegato.Data,
        Allegato.Immagine ? "Immagine" : Path.GetExtension(Allegato.NomeOriginale).TrimStart('.').ToUpperInvariant(),
        Allegato.Descrizione,
    }.Where(s => s.Trim().Length > 0).Select(s => s.Trim().ReplaceLineEndings(" ")));

    public string Etichetta => Dettaglio.Length > 0 ? $"{Titolo}, {Dettaglio}" : Titolo;

    public static IReadOnlyList<AttachmentItem> Load(PeopleStore store, string ambito, int schedaId)
        => [.. store.GetAttachments(ambito, schedaId)
            .Select(a => new AttachmentItem(a, a.Immagine ? ArtworkImages.LoadThumbnail(store.AttachmentsDirectory, a.File) : null))];

    // Apre il file con il programma predefinito di Windows.
    public static string? Open(PeopleStore store, Allegato allegato)
    {
        var path = store.AttachmentPath(allegato);
        if (!File.Exists(path))
        {
            return $"Il file «{allegato.NomeOriginale}» non è più nella cartella degli allegati.";
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"Impossibile aprire il file: {ex.Message}";
        }
    }
}

public partial class AttachmentsWindow : Window
{
    private readonly PeopleStore store;
    private readonly string ambito;
    private readonly int schedaId;

    // Vero se gli allegati sono stati modificati (per aggiornare la scheda o il ritratto).
    public bool Changed { get; private set; }

    public AttachmentsWindow(PeopleStore store, string ambito, int schedaId, string titoloScheda)
    {
        InitializeComponent();
        this.store = store;
        this.ambito = ambito;
        this.schedaId = schedaId;
        Title = $"Allegati — {titoloScheda}";
        if (ambito == AmbitiAllegato.Persona)
        {
            IntroText.Text += " La prima immagine diventa il ritratto del personaggio; puoi sceglierne un’altra.";
        }

        Reload(null);
    }

    private AttachmentItem? Selected => AttachmentList.SelectedItem as AttachmentItem;

    private void Reload(int? selectId)
    {
        var items = AttachmentItem.Load(store, ambito, schedaId);
        AttachmentList.ItemsSource = items;
        AttachmentList.SelectedItem = items.FirstOrDefault(i => i.Id == selectId) ?? items.FirstOrDefault();
        if (AttachmentList.SelectedItem is null)
        {
            ShowDetails(null);
        }
    }

    private void AttachmentList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ShowDetails(Selected);

    private void ShowDetails(AttachmentItem? item)
    {
        DetailPanel.IsEnabled = item is not null;
        TitleBox.Text = item?.Allegato.Titolo ?? string.Empty;
        DateBox.Text = item?.Allegato.Data ?? string.Empty;
        DescriptionBox.Text = item?.Allegato.Descrizione ?? string.Empty;
        PreviewImage.Source = item?.Miniatura;
        var index = item is null ? -1 : AttachmentList.Items.IndexOf(item);
        UpButton.IsEnabled = index > 0;
        DownButton.IsEnabled = index >= 0 && index < AttachmentList.Items.Count - 1;
        var canPortrait = ambito == AmbitiAllegato.Persona && item?.Allegato.Immagine == true;
        PortraitButton.Visibility = canPortrait ? Visibility.Visible : Visibility.Collapsed;
        PortraitButton.Content = item?.Allegato.Ritratto == true ? "Togli _ritratto" : "Usa come _ritratto";
        System.Windows.Automation.AutomationProperties.SetName(PortraitButton,
            item?.Allegato.Ritratto == true ? "Non usare più come ritratto" : "Usa l’immagine come ritratto del personaggio");
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Scegli i file da allegare",
            Multiselect = true,
            Filter = "Tutti i file|*.*|" + ArtworkImages.FileFilter.Split("|Tutti i file")[0] + "|Documenti|*.pdf;*.doc;*.docx;*.odt;*.txt;*.rtf",
        };
        if (dialog.ShowDialog(this) == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void AddFiles(IReadOnlyList<string> files)
    {
        var added = 0;
        int? lastId = null;
        var errors = new List<string>();
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            foreach (var file in files.Where(File.Exists))
            {
                try
                {
                    lastId = store.AddAttachment(ambito, schedaId, file).Id;
                    added++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DbUpdateException)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        Changed |= added > 0;
        Reload(lastId ?? Selected?.Id);
        Message.Text = errors.Count == 0
            ? $"{(added == 1 ? "Aggiunto 1 allegato" : $"Aggiunti {added} allegati")}."
            : $"Aggiunti {added}; non aggiunti: {string.Join("; ", errors)}";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddFiles(files);
        }
    }

    private void SaveDetails_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        Try(() => store.UpdateAttachment(item.Id, TitleBox.Text, DescriptionBox.Text, DateBox.Text), item.Id, "Dettagli salvati.");
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void AttachmentList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (Selected is { } item)
        {
            Message.Text = AttachmentItem.Open(store, item.Allegato) ?? string.Empty;
        }
    }

    private void Portrait_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
        {
            Try(() => store.TogglePortrait(item.Id), item.Id, item.Allegato.Ritratto ? "Ritratto tolto." : "Ritratto scelto.");
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int direction)
    {
        if (Selected is { } item)
        {
            Try(() => store.MoveAttachment(item.Id, direction), item.Id, string.Empty);
            AttachmentList.Focus();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        var answer = MessageBox.Show(this, $"Eliminare l’allegato «{item.Titolo}»? Verrà tolta anche la copia nell’archivio; il file originale non viene toccato.",
            "Elimina allegato", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            Try(() => store.DeleteAttachment(item.Id), null, "Allegato eliminato.");
        }
    }

    private void Try(Action action, int? selectId, string done)
    {
        try
        {
            action();
            Changed = true;
            Reload(selectId);
            Message.Text = done;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            Message.Text = ex is InvalidOperationException ? ex.Message : "Operazione non riuscita.";
        }
    }
}
