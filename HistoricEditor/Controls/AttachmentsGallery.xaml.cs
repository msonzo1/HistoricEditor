using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Allegati di una scheda in sola lettura: un clic apre il file.
public partial class AttachmentsGallery : UserControl
{
    private PeopleStore? store;

    public AttachmentsGallery() => InitializeComponent();

    public int Count { get; private set; }

    public int Load(PeopleStore store, string ambito, int schedaId)
    {
        this.store = store;
        var items = AttachmentItem.Load(store, ambito, schedaId);
        Items.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Message.Text = string.Empty;
        Count = items.Count;
        return Count;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (store is not null && (sender as FrameworkElement)?.Tag is AttachmentItem item)
        {
            Message.Text = AttachmentItem.Open(store, item.Allegato) ?? string.Empty;
        }
    }
}
