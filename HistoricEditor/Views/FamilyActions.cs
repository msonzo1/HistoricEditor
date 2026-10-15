using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Azioni sui legami familiari condivise dalla barra comandi e dall'albero genealogico.
// Il selettore permette sia di scegliere una persona esistente sia di crearne una nuova.
public static class FamilyActions
{
    public static bool AddParent(Window owner, PeopleStore store, int childId, bool mother)
    {
        var picker = new PersonPickerWindow(store, mother ? "Scegli la madre" : "Scegli il padre", childId, mother ? Sessi.Femmina : Sessi.Maschio) { Owner = owner };
        return picker.ShowDialog() == true && picker.SelectedId is int id
            && Run(owner, () => store.SetParent(childId, id, mother));
    }

    public static bool AddSpouse(Window owner, PeopleStore store, int personId, string personName)
    {
        var picker = new PersonPickerWindow(store, "Scegli il coniuge", personId, Sessi.Opposto(store.GetSex(personId))) { Owner = owner };
        if (picker.ShowDialog() != true || picker.SelectedId is not int spouseId)
        {
            return false;
        }

        var spouse = store.GetPersonSummaries().FirstOrDefault(p => p.Id == spouseId)?.Nome ?? "coniuge";
        var editor = new UnionEditorWindow(store, new Unione { Persona1Id = personId, Persona2Id = spouseId }, $"Unione tra {personName} e {spouse}") { Owner = owner };
        return editor.ShowDialog() == true;
    }

    public static bool AddChild(Window owner, PeopleStore store, int parentId)
    {
        var picker = new PersonPickerWindow(store, "Scegli il figlio", parentId, string.Empty) { Owner = owner };
        return picker.ShowDialog() == true && picker.SelectedId is int childId
            && Run(owner, () => store.AssignChild(parentId, childId, null));
    }

    public static bool EditFamily(Window owner, PeopleStore store, int personId, string personName)
    {
        var editor = new FamilyEditorWindow(store, personId, personName) { Owner = owner };
        editor.ShowDialog();
        return editor.Changed;
    }

    private static bool Run(Window owner, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            var message = ex is InvalidOperationException ? ex.Message : "Impossibile salvare il legame.";
            MessageBox.Show(owner, message, "Legame non salvato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
