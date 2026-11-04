using System.Diagnostics;
using System.Windows;

namespace HistoricEditor;

// Apertura delle schede in lettura o in modifica, condivisa da finestra principale, sezioni e schede di lettura.
// I metodi restituiscono true se l'archivio può essere cambiato (va ricaricato ciò che è mostrato).
internal static class RecordNavigator
{
    // Modalità scelta con Ctrl+M o dal menu Visualizza; la finestra principale la tiene aggiornata.
    public static PersonOpenMode Mode { get; set; } = PersonOpenMode.Edit;

    public static bool IsViewMode => Mode == PersonOpenMode.View;

    public static bool OpenPerson(Window owner, PeopleStore store, int personId)
        => IsViewMode ? ViewPerson(owner, store, personId) : EditPerson(owner, store, personId);

    public static bool ViewPerson(Window owner, PeopleStore store, int personId)
    {
        if (store.GetPerson(personId) is not { } person)
        {
            return false;
        }

        var window = new PersonViewWindow(store, person) { Owner = owner };
        window.ShowDialog();
        return window.Changed;
    }

    public static bool EditPerson(Window owner, PeopleStore store, int personId)
    {
        if (store.GetPerson(personId) is not { } person)
        {
            return false;
        }

        var editor = new PersonEditorWindow(store, existing: person) { Owner = owner };
        editor.ShowDialog();
        return editor.Saved;
    }

    public static bool OpenArtwork(Window owner, PeopleStore store, int artworkId)
        => IsViewMode ? ViewArtwork(owner, store, artworkId) : EditArtwork(owner, store, artworkId);

    public static bool ViewArtwork(Window owner, PeopleStore store, int artworkId)
    {
        if (store.GetArtwork(artworkId) is null)
        {
            return false;
        }

        var window = new ArtworkViewWindow(store, artworkId) { Owner = owner };
        window.ShowDialog();
        return window.Changed;
    }

    // Senza opera apre il modulo vuoto per una nuova opera; con personId l'elenco è limitato alle sue opere.
    public static bool EditArtwork(Window owner, PeopleStore store, int? artworkId, int? personId = null)
    {
        new ArtworksWindow(store, personId, artworkId) { Owner = owner }.ShowDialog();
        return true;
    }

    public static bool OpenEvent(Window owner, PeopleStore store, int eventId)
        => IsViewMode ? ViewEvent(owner, store, eventId) : EditEvent(owner, store, eventId);

    public static bool ViewEvent(Window owner, PeopleStore store, int eventId)
    {
        if (store.GetEvent(eventId) is null)
        {
            return false;
        }

        var window = new EventViewWindow(store, eventId) { Owner = owner };
        window.ShowDialog();
        return window.Changed;
    }

    public static bool EditEvent(Window owner, PeopleStore store, int? eventId, int? personId = null)
    {
        new EventsWindow(store, personId, eventId, onlyFocusPerson: personId is not null) { Owner = owner }.ShowDialog();
        return true;
    }

    // Apre un indirizzo web o un file con il programma predefinito di Windows; false se non è possibile.
    public static bool OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
