using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class UnionEditorWindow : Window
{
    private readonly PeopleStore store;
    private readonly Unione data;

    public UnionEditorWindow(PeopleStore store, Unione data, string header)
    {
        InitializeComponent();
        this.store = store;
        this.data = data;
        HeaderText.Text = header;
        TipoBox.Text = data.Tipo;
        InizioBox.Text = data.Inizio;
        FineBox.Text = data.Fine;
        NoteBox.Text = data.Note;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        data.Tipo = TipoBox.Text.Trim().Length > 0 ? TipoBox.Text : "Matrimonio";
        data.Inizio = InizioBox.Text;
        data.Fine = FineBox.Text;
        data.Note = NoteBox.Text;

        try
        {
            store.SaveUnion(data);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            EditorMessage.Text = ex is InvalidOperationException ? ex.Message : "Impossibile salvare l’unione.";
            return;
        }

        DialogResult = true;
    }
}
