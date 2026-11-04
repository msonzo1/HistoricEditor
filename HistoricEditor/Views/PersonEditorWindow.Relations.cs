using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Sezione «Altre relazioni» della tab Parentele: legami non familiari con altri personaggi.
public partial class PersonEditorWindow
{
    private void RefreshRelations(int personId)
    {
        var relations = store.GetRelationsForPerson(personId);
        RelationsList.ItemsSource = relations;
        NoRelationsText.Visibility = relations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddRelation_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var picker = new PersonPickerWindow(store, "Scegli il personaggio collegato", person.Id, string.Empty) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedId is int otherId)
        {
            EditRelation(new Relazione { Persona1Id = person.Id, Persona2Id = otherId });
        }
    }

    private void EditRelation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RelationView view && store.GetRelation(view.Id) is { } relazione)
        {
            EditRelation(relazione);
        }
    }

    private void EditRelation(Relazione relazione)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var otherId = relazione.Persona1Id == person.Id ? relazione.Persona2Id : relazione.Persona1Id;
        var other = store.GetPersonSummaries().FirstOrDefault(p => p.Id == otherId)?.Nome ?? "personaggio";
        var editor = new RelationEditorWindow(store, relazione, person.Id, person.DisplayName, other) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            Saved = true;
            Feedback.Success(FamilyMessage, "Relazione salvata.");
            RefreshFamily();
        }
    }

    private void RemoveRelation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RelationView view)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Rimuovere la relazione «{view.Descrizione}»? Le schede dei due personaggi restano in archivio.",
            "Rimuovi relazione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            RunFamily(() => store.DeleteRelation(view.Id), "Relazione rimossa.");
        }
    }
}
