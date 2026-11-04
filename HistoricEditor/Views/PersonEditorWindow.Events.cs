using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HistoricEditor;

// Tab «Altri eventi»: eventi a cui partecipa il personaggio, gestiti nella finestra Eventi.
public partial class PersonEditorWindow
{
    private sealed record PersonEventItem(Evento Evento, string Ruolo)
    {
        public string Etichetta
        {
            get
            {
                var details = string.Join(" · ", new[] { Evento.DataTesto, Evento.Tipo, Ruolo }.Where(s => s.Length > 0));
                return details.Length > 0 ? $"{Evento.Titolo} — {details}" : Evento.Titolo;
            }
        }
    }

    private void UpdateEventsAccess()
    {
        var available = editingPerson is not null;
        EventsLockedText.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        EventsCommands.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        PersonEventsList.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (!available)
        {
            EventsEmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        if (Tabs.SelectedItem == EventsTab)
        {
            RefreshPersonEvents(null);
        }
    }

    private void RefreshPersonEvents(int? selectId)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var items = store.GetEventsForPerson(person.Id)
            .Select(e => new PersonEventItem(e, e.Partecipanti.FirstOrDefault(p => p.PersonaId == person.Id)?.Ruolo ?? string.Empty))
            .ToList();
        PersonEventsList.ItemsSource = items;
        PersonEventsList.SelectedItem = items.FirstOrDefault(i => i.Evento.Id == selectId);
        EventsEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenPersonEventButton.IsEnabled = PersonEventsList.SelectedItem is not null;
    }

    private void OpenEventsWindow(int? eventId, bool onlyThisPerson)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var window = new EventsWindow(store, person.Id, eventId, onlyThisPerson) { Owner = this };
        window.ShowDialog();
        if (window.PeopleChanged)
        {
            Saved = true;
        }

        RefreshPersonEvents(eventId);
    }

    private void NewPersonEvent_Click(object sender, RoutedEventArgs e) => OpenEventsWindow(null, onlyThisPerson: true);

    private void ManageEvents_Click(object sender, RoutedEventArgs e) => OpenEventsWindow(null, onlyThisPerson: false);

    // Opere d'arte del personaggio (dipinte, commissionate o in cui è raffigurato).
    private void PersonArtworks_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var window = new ArtworksWindow(store, person.Id) { Owner = this };
        window.ShowDialog();
        if (window.PeopleChanged)
        {
            Saved = true;
        }
    }

    private void OpenPersonEvent_Click(object sender, RoutedEventArgs e) => OpenSelectedPersonEvent();

    private void OpenSelectedPersonEvent()
    {
        if (PersonEventsList.SelectedItem is PersonEventItem item)
        {
            OpenEventsWindow(item.Evento.Id, onlyThisPerson: true);
        }
    }

    private void PersonEventsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => OpenPersonEventButton.IsEnabled = PersonEventsList.SelectedItem is not null;

    private void PersonEventsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedPersonEvent();

    private void PersonEventsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OpenSelectedPersonEvent();
        }
    }
}
