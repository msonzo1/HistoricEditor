using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class HistoricPlaceEditorWindow : Window
{
    private readonly PeopleStore store;
    private int? parentId;

    public int? ResultId { get; private set; }

    public HistoricPlaceEditorWindow(PeopleStore store, int? defaultParentId)
    {
        InitializeComponent();
        this.store = store;
        parentId = defaultParentId;
        TipoBox.ItemsSource = store.GetHistoricPlaceTypes();
        UpdateParent();
        NomeBox.Focus();
    }

    private void UpdateParent()
    {
        var text = store.DescribePlace(parentId);
        ParentText.Text = text.Length > 0 ? text : "Nessuno (luogo di primo livello)";
    }

    private void ChooseParent_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PlacePickerWindow(store, "Luogo che contiene il luogo storico", allowSpecific: false, parentId, null, allowClear: true, startHistoric: true) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            parentId = picker.ResultLuogoId;
            UpdateParent();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var nome = NomeBox.Text.Trim();
        if (nome.Length == 0)
        {
            EditorMessage.Text = "Indica il nome.";
            NomeBox.Focus();
            return;
        }

        var tipo = TipoBox.Text.Trim();
        if (tipo.Length == 0)
        {
            tipo = "Luogo storico";
        }

        if (!CoordinateText.TryParse(LatBox.Text, out var lat))
        {
            EditorMessage.Text = "Latitudine non valida.";
            LatBox.Focus();
            return;
        }

        if (!CoordinateText.TryParse(LonBox.Text, out var lon))
        {
            EditorMessage.Text = "Longitudine non valida.";
            LonBox.Focus();
            return;
        }

        if (store.FindPlace(parentId, tipo, nome) is not null)
        {
            EditorMessage.Text = "Esiste già un luogo con questo nome e tipo nello stesso contenitore.";
            return;
        }

        try
        {
            ResultId = store.AddHistoricPlace(nome, tipo, parentId, PeriodoBox.Text, lat, lon, NoteBox.Text);
        }
        catch (DbUpdateException)
        {
            EditorMessage.Text = "Esiste già un luogo con questo nome e tipo nello stesso contenitore.";
            return;
        }

        DialogResult = true;
    }
}
