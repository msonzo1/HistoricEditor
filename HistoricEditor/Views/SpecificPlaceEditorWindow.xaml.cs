using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class SpecificPlaceEditorWindow : Window
{
    private readonly PeopleStore store;
    private readonly int? specificId;
    private int? classicId;

    public int? ResultId { get; private set; }

    public SpecificPlaceEditorWindow(PeopleStore store, int? specificId, int? defaultLuogoId)
    {
        InitializeComponent();
        this.store = store;
        this.specificId = specificId;

        TipoBox.ItemsSource = store.GetSpecificPlaceTypes();
        CondizioneBox.ItemsSource = CondizioniLuogo.Tutte;

        var existing = specificId is int id ? store.GetSpecificPlace(id) : null;
        if (existing is not null)
        {
            Title = "Modifica luogo specifico";
            NomeBox.Text = existing.Nome;
            TipoBox.Text = existing.Tipo;
            classicId = existing.LuogoId;
            IndirizzoBox.Text = existing.Indirizzo;
            CondizioneBox.SelectedItem = CondizioniLuogo.Tutte.Contains(existing.Condizione) ? existing.Condizione : CondizioniLuogo.Esistente;
            DataDistruzioneBox.Text = existing.DataDistruzione;
            LatBox.Text = CoordinateText.Format(existing.Latitudine);
            LonBox.Text = CoordinateText.Format(existing.Longitudine);
            WikidataBox.Text = existing.WikidataId;
            NoteBox.Text = existing.Note;

            var count = store.CountPeopleUsingSpecific(existing.Id);
            if (count > 0)
            {
                UsageText.Text = $"Collegato a {count} {(count == 1 ? "personaggio" : "personaggi")}: le modifiche, compreso il luogo amministrativo, valgono per tutti.";
            }
        }
        else
        {
            Title = "Nuovo luogo specifico";
            classicId = defaultLuogoId;
            CondizioneBox.SelectedItem = CondizioniLuogo.Esistente;
        }

        CondizioneBox.SelectionChanged += (_, _) => UpdateDestruction();
        UpdateDestruction();
        UpdateClassic();
        NomeBox.Focus();
    }

    private void UpdateDestruction()
        => DataDistruzioneBox.IsEnabled = !Equals(CondizioneBox.SelectedItem, CondizioniLuogo.Esistente);

    private void UpdateClassic()
    {
        var text = store.DescribePlace(classicId);
        ClassicText.Text = text.Length > 0 ? text : "Nessun luogo selezionato";
    }

    private void ChangeClassic_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PlacePickerWindow(store, "Luogo che contiene il luogo specifico", allowSpecific: false, classicId, null, allowClear: false) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            classicId = picker.ResultLuogoId;
            UpdateClassic();
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

        if (classicId is not int luogoId)
        {
            EditorMessage.Text = "Indica il luogo amministrativo che lo contiene.";
            return;
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

        var condizione = CondizioneBox.SelectedItem as string ?? CondizioniLuogo.Esistente;
        var data = new LuogoSpecifico
        {
            Id = specificId ?? 0,
            Nome = nome,
            Tipo = TipoBox.Text,
            LuogoId = luogoId,
            Indirizzo = IndirizzoBox.Text,
            Condizione = condizione,
            DataDistruzione = condizione == CondizioniLuogo.Esistente ? string.Empty : DataDistruzioneBox.Text,
            Latitudine = lat,
            Longitudine = lon,
            Note = NoteBox.Text,
            WikidataId = WikidataBox.Text,
        };

        try
        {
            ResultId = store.SaveSpecificPlace(data);
        }
        catch (DbUpdateException)
        {
            EditorMessage.Text = "Esiste già un luogo specifico con questo nome nello stesso luogo.";
            return;
        }

        DialogResult = true;
    }
}
