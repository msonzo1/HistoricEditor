using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Relazione non familiare vista dalla persona della scheda: la voce scelta stabilisce anche il verso (es. «Allievo di»).
public partial class RelationEditorWindow : Window
{
    private readonly PeopleStore store;
    private readonly Relazione data;
    private readonly int personId;
    private readonly int otherId;
    private readonly string personName;
    private readonly string otherName;
    private readonly string sesso;

    public RelationEditorWindow(PeopleStore store, Relazione data, int personId, string personName, string otherName)
    {
        InitializeComponent();
        this.store = store;
        this.data = data;
        this.personId = personId;
        this.personName = personName;
        this.otherName = otherName;
        otherId = data.Persona1Id == personId ? data.Persona2Id : data.Persona1Id;
        sesso = store.GetSex(personId);

        HeaderText.Text = $"Relazione tra {personName} e {otherName}";
        TipoLabel.Content = $"_Relazione di {personName} con {otherName}";
        TipoBox.ItemsSource = Relazioni.Scelte(sesso);
        if (data.Tipo.Trim().Length > 0)
        {
            TipoBox.Text = Relazioni.Find(data.Tipo) is null
                ? data.Tipo
                : Relazioni.Etichetta(data.Tipo, data.Persona1Id == personId, sesso);
        }

        InizioBox.Text = data.Inizio;
        FineBox.Text = data.Fine;
        NoteBox.Text = data.Note;
        UpdatePreview();
        Loaded += (_, _) => TipoBox.Focus();
    }

    private void TipoBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void TipoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(UpdatePreview);

    // Frase di controllo: «Leonardo da Vinci: Allievo di Andrea del Verrocchio».
    private void UpdatePreview()
    {
        var scelta = Relazioni.Interpreta(TipoBox.Text, sesso);
        PreviewText.Text = scelta is null
            ? string.Empty
            : $"Si legge: {personName} — {Relazioni.Etichetta(scelta.Tipo, scelta.Attivo, sesso).TrimEnd(':')} {otherName}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Relazioni.Interpreta(TipoBox.Text, sesso) is not { } scelta)
        {
            Feedback.Error(EditorMessage, "Indica il tipo di relazione.");
            TipoBox.Focus();
            return;
        }

        var (persona1, persona2) = scelta.Attivo ? (personId, otherId) : (otherId, personId);
        if (data.Id == 0 && store.HasRelation(persona1, persona2, scelta.Tipo))
        {
            Feedback.Error(EditorMessage, "Questa relazione è già registrata.");
            return;
        }

        data.Persona1Id = persona1;
        data.Persona2Id = persona2;
        data.Tipo = scelta.Tipo;
        data.Inizio = InizioBox.Text;
        data.Fine = FineBox.Text;
        data.Note = NoteBox.Text;

        try
        {
            store.SaveRelation(data);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            Feedback.Error(EditorMessage, ex is InvalidOperationException ? ex.Message : "Impossibile salvare la relazione.");
            return;
        }

        DialogResult = true;
    }
}
