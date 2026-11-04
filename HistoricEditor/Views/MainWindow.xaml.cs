using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class MainWindow : Window
{
    private readonly PeopleStore store;

    private readonly ObservableCollection<HistoricalPerson> people = [];

	 private HistoricalPerson? SelectedPerson => (PeopleTree.SelectedItem as PersonEntry)?.Person;

	 // Persona al centro dell'albero genealogico; segue la selezione nella vista Famiglie.
	 private int? pedigreeRootId;

	 private bool IsPedigreeView => PedigreeViewToggle.IsChecked == true;

	 // Persona a cui si applicano i comandi della barra, nella vista attiva.
	 private HistoricalPerson? CurrentPerson => IsPedigreeView
		  ? people.FirstOrDefault(p => p.Id == pedigreeRootId)
		  : SelectedPerson;

	 public MainWindow(PeopleStore store)
	 {
		  this.store = store;
		  InitializeComponent();
		  Closed += (_, _) => store.Dispose();
		  PlacesPanel.PlacesChanged += (_, _) => ReloadAfterFamilyChange(pedigreeRootId ?? 0);
		  EventsPanel.DataChanged += (_, _) => ReloadAfterSectionChange();
		  ArtworksPanel.DataChanged += (_, _) => ReloadAfterSectionChange();
		  OfficesPanel.DataChanged += (_, _) => ReloadAfterSectionChange();
		  DynastiesPanel.DataChanged += (_, _) => ReloadAfterSectionChange();
		  DynastiesPanel.ShowEventsRequested += (_, dynastyId) =>
		  {
				EventsTab.IsChecked = true;
				EventsPanel.ShowDynasty(dynastyId);
		  };
		  LoadPeople();
		  SyncThemeMenu();
		  openMode = SettingsStore.Load().PersonOpenMode;
		  RecordNavigator.Mode = openMode;
		  SyncOpenMode();
		  AddShortcut(System.Windows.Input.Key.M, ToggleOpenMode);
		  SectionShortcuts();
		  FamiliesViewToggle.IsChecked = true;
		  PeopleTab.IsChecked = true;
	 }

	 private void AddShortcut(System.Windows.Input.Key key, Action action)
	 {
		  var command = new System.Windows.Input.RoutedCommand();
		  CommandBindings.Add(new System.Windows.Input.CommandBinding(command, (_, _) => action()));
		  InputBindings.Add(new System.Windows.Input.KeyBinding(command, key, System.Windows.Input.ModifierKeys.Control));
	 }

	 // Ctrl+1…6 passano da una sezione all'altra, nell'ordine della barra laterale.
	 private void SectionShortcuts()
	 {
		  var tabs = new[] { PeopleTab, DynastiesTab, OfficesTab, EventsTab, ArtworksTab, PlacesTab };
		  var keys = new[] { System.Windows.Input.Key.D1, System.Windows.Input.Key.D2, System.Windows.Input.Key.D3, System.Windows.Input.Key.D4, System.Windows.Input.Key.D5, System.Windows.Input.Key.D6 };
		  for (var i = 0; i < tabs.Length; i++)
		  {
				var tab = tabs[i];
				AddShortcut(keys[i], () =>
				{
					 tab.IsChecked = true;
					 tab.Focus();
				});
		  }
	 }

	 // Dopo una modifica fatta da una sezione: aggiorna l'elenco delle persone e il backup JSON.
	 private void ReloadAfterSectionChange()
	 {
		  var keep = CurrentPerson?.Id ?? pedigreeRootId;
		  people.Clear();
		  LoadPeople(keep is int id && store.GetPerson(id) is not null ? id : null);
		  SavePeople();
	 }

	 private void SectionTab_Checked(object sender, RoutedEventArgs e) => ShowSection();

	 // Mostra solo il pannello della scheda laterale attiva.
	 private void ShowSection()
	 {
		  if (PeoplePanel is null || PlacesPanel is null || EventsPanel is null || ArtworksPanel is null || OfficesPanel is null || DynastiesPanel is null)
		  {
				return;
		  }

		  var people = PeopleTab.IsChecked == true;
		  var places = PlacesTab.IsChecked == true;
		  var events = EventsTab.IsChecked == true;
		  var artworks = ArtworksTab.IsChecked == true;
		  var offices = OfficesTab.IsChecked == true;
		  var dynasties = DynastiesTab.IsChecked == true;
		  PeoplePanel.Visibility = people ? Visibility.Visible : Visibility.Collapsed;
		  PlacesPanel.Visibility = places ? Visibility.Visible : Visibility.Collapsed;
		  EventsPanel.Visibility = events ? Visibility.Visible : Visibility.Collapsed;
		  ArtworksPanel.Visibility = artworks ? Visibility.Visible : Visibility.Collapsed;
		  OfficesPanel.Visibility = offices ? Visibility.Visible : Visibility.Collapsed;
		  DynastiesPanel.Visibility = dynasties ? Visibility.Visible : Visibility.Collapsed;
		  SectionTitle.Text = places ? "LUOGHI" : events ? "EVENTI" : artworks ? "OPERE D'ARTE"
				: offices ? "CARICHE" : dynasties ? "DINASTIE" : "PERSONAGGI";

		  if (places)
		  {
				PlacesPanel.Activate(store);
		  }
		  else if (events)
		  {
				EventsPanel.Activate(store);
		  }
		  else if (artworks)
		  {
				ArtworksPanel.Activate(store);
		  }
		  else if (offices)
		  {
				OfficesPanel.Activate(store);
		  }
		  else if (dynasties)
		  {
				DynastiesPanel.Activate(store);
		  }
	 }

	 private void ThemeLight_Click(object sender, RoutedEventArgs e)
	 {
		  ThemeManager.Apply(AppTheme.Light);
		  SyncThemeMenu();
	 }

	 private void ThemeDark_Click(object sender, RoutedEventArgs e)
	 {
		  ThemeManager.Apply(AppTheme.Dark);
		  SyncThemeMenu();
	 }

	 private void SyncThemeMenu()
	 {
		  ThemeLightMenuItem.IsChecked = ThemeManager.Current == AppTheme.Light;
		  ThemeDarkMenuItem.IsChecked = ThemeManager.Current == AppTheme.Dark;
	 }

	 // Modalità lettura o modifica: decide cosa aprono doppio clic, Invio e clic sulla persona al centro dell'albero.
	 private PersonOpenMode openMode;

	 private void OpenMode_Checked(object sender, RoutedEventArgs e)
		  => SetOpenMode(sender == OpenViewToggle ? PersonOpenMode.View : PersonOpenMode.Edit);

	 private void OpenModeView_Click(object sender, RoutedEventArgs e) => SetOpenMode(PersonOpenMode.View);

	 private void OpenModeEdit_Click(object sender, RoutedEventArgs e) => SetOpenMode(PersonOpenMode.Edit);

	 // Ctrl+M: passa da lettura a modifica e viceversa.
	 private void ToggleOpenMode()
		  => SetOpenMode(openMode == PersonOpenMode.View ? PersonOpenMode.Edit : PersonOpenMode.View);

	 private void SetOpenMode(PersonOpenMode mode)
	 {
		  if (mode != openMode)
		  {
				openMode = mode;
				RecordNavigator.Mode = mode;
				SettingsStore.Update(s => s.OpenMode = mode.ToString());
				Feedback.Success(StatusMessage, mode == PersonOpenMode.View
					 ? "Modalità lettura: in ogni sezione il doppio clic apre la scheda di lettura (Ctrl+M per cambiare)."
					 : "Modalità modifica: in ogni sezione il doppio clic apre la scheda di modifica (Ctrl+M per cambiare).");
		  }

		  SyncOpenMode();
	 }

	 private void SyncOpenMode()
	 {
		  OpenViewToggle.IsChecked = openMode == PersonOpenMode.View;
		  OpenEditToggle.IsChecked = openMode == PersonOpenMode.Edit;
		  OpenViewMenuItem.IsChecked = openMode == PersonOpenMode.View;
		  OpenEditMenuItem.IsChecked = openMode == PersonOpenMode.Edit;
	 }

	 // Apertura «predefinita» di un personaggio secondo la modalità scelta.
	 private void OpenPerson(HistoricalPerson? person)
	 {
		  if (openMode == PersonOpenMode.View)
		  {
				ViewPerson(person);
		  }
		  else
		  {
				EditPerson(person);
		  }
	 }

	 private void NewPerson_Click(object sender, RoutedEventArgs e) => OpenEditor(new PersonEditorWindow(store));

	 private void EditPerson_Click(object sender, RoutedEventArgs e) => EditPerson(CurrentPerson);

	 private void ViewPerson_Click(object sender, RoutedEventArgs e) => ViewPerson(CurrentPerson);

	 private void ViewPerson(HistoricalPerson? person)
	 {
		  if (person is null)
		  {
				return;
		  }

		  var window = new PersonViewWindow(store, person) { Owner = this };
		  window.ShowDialog();
		  if (window.Changed)
		  {
				var keep = store.GetPerson(person.Id) is null ? (int?)null : person.Id;
				people.Clear();
				LoadPeople(keep);
				SavePeople();
		  }
	 }

	 private void EditFamily_Click(object sender, RoutedEventArgs e) => EditFamily(CurrentPerson);

	 private void AddSpouse_Click(object sender, RoutedEventArgs e) => AddSpouse(CurrentPerson);

	 private void AddChild_Click(object sender, RoutedEventArgs e) => AddChild(CurrentPerson);

	 private void EditPerson(HistoricalPerson? person)
	 {
		  if (person is not null)
		  {
				OpenEditor(new PersonEditorWindow(store, existing: person));
		  }
	 }

	 private void EditFamily(HistoricalPerson? person)
	 {
		  if (person is not null && FamilyActions.EditFamily(this, store, person.Id, person.DisplayName))
		  {
				ReloadAfterFamilyChange(person.Id);
		  }
	 }

	 private void AddSpouse(HistoricalPerson? person)
	 {
		  if (person is not null && FamilyActions.AddSpouse(this, store, person.Id, person.DisplayName))
		  {
				ReloadAfterFamilyChange(person.Id);
		  }
	 }

	 private void AddChild(HistoricalPerson? person)
	 {
		  if (person is not null && FamilyActions.AddChild(this, store, person.Id))
		  {
				ReloadAfterFamilyChange(person.Id);
		  }
	 }

	 // I legami e le nuove schede create dal selettore richiedono di ricaricare elenco e albero.
	 private void ReloadAfterFamilyChange(int selectId)
	 {
		  people.Clear();
		  LoadPeople(selectId);
		  SavePeople();
	 }

	 private void PeopleTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
	 {
		  if (SelectedPerson is { } person)
		  {
				e.Handled = true;
				OpenPerson(person);
		  }
	 }

	 private void PeopleTree_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	 {
		  if (e.Key == System.Windows.Input.Key.Delete && SelectedPerson is not null)
		  {
				e.Handled = true;
				DeletePerson_Click(sender, e);
		  }
		  else if (e.Key == System.Windows.Input.Key.Enter && SelectedPerson is { } person)
		  {
				e.Handled = true;
				OpenPerson(person);
		  }
	 }

	 // Apre la scheda a tab; alla chiusura con salvataggio ricarica l'elenco mantenendo la selezione.
	 private void OpenEditor(PersonEditorWindow editor)
	 {
		  editor.Owner = this;
		  editor.ShowDialog();
		  if (editor.Saved)
		  {
				people.Clear();
				LoadPeople(editor.Deleted ? null : editor.SavedPersonId);
				SavePeople();
				if (editor.Deleted)
				{
					 Feedback.Success(StatusMessage, "Scheda eliminata.");
				}
		  }

		  // L'utente ha scelto di aprire la scheda già presente invece di crearne un doppione.
		  if (editor.OpenExistingId is int existingId && people.Any(p => p.Id == existingId))
		  {
				people.Clear();
				LoadPeople(existingId);
				if (people.FirstOrDefault(p => p.Id == existingId) is { } existing)
				{
					 OpenEditor(new PersonEditorWindow(store, existing: existing));
				}
		  }
	 }

	 private void PeopleTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
	 {
		  if (SelectedPerson is { } person)
		  {
				pedigreeRootId = person.Id;
		  }

		  UpdateCommands();
	 }

	 private void UpdateCommands()
	 {
		  var hasPerson = CurrentPerson is not null;
		  ViewPersonButton.IsEnabled = hasPerson;
		  EditPersonButton.IsEnabled = hasPerson;
		  DeleteButton.IsEnabled = hasPerson;
		  FamilyButton.IsEnabled = hasPerson;
		  AddSpouseButton.IsEnabled = hasPerson;
		  AddChildButton.IsEnabled = hasPerson;
	 }

	 private void PeopleView_Checked(object sender, RoutedEventArgs e) => ShowPeopleView();

	 private void ShowPeopleView()
	 {
		  if (PeopleTree is null || FamiliesView is null || PedigreeView is null || PedigreeHint is null)
		  {
				return;
		  }

		  var pedigree = IsPedigreeView;
		  FamiliesView.Visibility = pedigree ? Visibility.Collapsed : Visibility.Visible;
		  if (pedigree)
		  {
				RenderPedigree();
		  }
		  else
		  {
				PedigreeView.Visibility = Visibility.Collapsed;
				PedigreeHint.Visibility = Visibility.Collapsed;
				SelectInTree(pedigreeRootId);
		  }

		  UpdateCommands();
	 }

	 private void DeletePerson_Click(object sender, RoutedEventArgs e) => DeletePerson(CurrentPerson);

	 private void DeletePerson(HistoricalPerson? person)
	 {
		  if (person is null)
		  {
				return;
		  }

		  var confirm = MessageBox.Show(
				this,
				$"Eliminare definitivamente la scheda di {person.DisplayName}? I legami familiari verranno scollegati.",
				"Elimina scheda",
				MessageBoxButton.YesNo,
				MessageBoxImage.Warning);
		  if (confirm != MessageBoxResult.Yes)
		  {
				return;
		  }

		  try
		  {
				store.Delete(person.Id);
		  }
		  catch (Exception ex) when (ex is IOException or DbUpdateException or InvalidOperationException)
		  {
				Feedback.Error(StatusMessage, "Impossibile eliminare la scheda dal database.");
				return;
		  }

		  var keepId = pedigreeRootId == person.Id ? null : pedigreeRootId;
		  pedigreeRootId = null;
		  people.Clear();
		  LoadPeople(keepId);
		  Feedback.Success(StatusMessage, "Scheda eliminata.");
		  SavePeople();
	 }

	 private bool unimportedBackupNotified;

	 private void LoadPeople(int? selectId = null)
	 {
		  try
		  {
				foreach (var person in store.LoadAll())
				{
					 people.Add(person);
				}

				if (!unimportedBackupNotified && store.UnimportedBackupPath is { } preserved)
				{
					 unimportedBackupNotified = true;
					 Feedback.Error(StatusMessage, $"Il backup JSON non è stato importato. Una copia è conservata in: {preserved}");
				}
		  }
		  catch (Exception ex) when (ex is IOException or DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
		  {
				Feedback.Error(StatusMessage, store.UnimportedBackupPath is { } preserved
					 ? $"Impossibile importare il backup JSON. Una copia è conservata in: {preserved}"
					 : "Impossibile leggere l’archivio locale.");
		  }

		  UpdatePeopleCount();
		  var expanded = ExpandedFamilies();
		  var (dynasties, links) = LoadDynasties();
		  PeopleTree.ItemsSource = PeopleGrouping.Build(people, dynasties, links);
		  RestoreExpandedFamilies(expanded);
		  if (selectId is not null)
		  {
				pedigreeRootId = selectId;
		  }

		  if (pedigreeRootId is int id && people.All(p => p.Id != id))
		  {
				pedigreeRootId = null;
		  }

		  SelectInTree(pedigreeRootId);
		  if (IsPedigreeView)
		  {
				RenderPedigree();
		  }

		  UpdateCommands();
	 }

	 // Famiglie aperte nell'albero, per ripristinarle dopo il ricaricamento (es. eliminazione di una scheda).
	 private HashSet<string> ExpandedFamilies()
		  => PeopleTree.ItemsSource is IEnumerable<PersonGroup> groups
				? groups.SelectMany(g => g.SelfAndDescendants()).Where(g => g.IsExpanded).Select(g => g.Famiglia).ToHashSet(StringComparer.OrdinalIgnoreCase)
				: [];

	 private void RestoreExpandedFamilies(HashSet<string> expanded)
	 {
		  if (expanded.Count == 0 || PeopleTree.ItemsSource is not IEnumerable<PersonGroup> groups)
		  {
				return;
		  }

		  foreach (var group in groups.SelectMany(g => g.SelfAndDescendants()).Where(g => expanded.Contains(g.Famiglia)))
		  {
				group.IsExpanded = true;
		  }
	 }

	 private void SelectInTree(int? personId)
	 {
		  if (PeopleTree.ItemsSource is not IEnumerable<PersonGroup> groups)
		  {
				return;
		  }

		  foreach (var group in groups)
		  {
				if (SelectInGroup(group, personId))
				{
					 return;
				}
		  }
	 }

	 // Cerca la persona nella dinastia e nei suoi rami, espandendo il percorso trovato.
	 private static bool SelectInGroup(PersonGroup group, int? personId)
	 {
		  if (group.Persone.FirstOrDefault(p => p.Person.Id == personId) is { } entry)
		  {
				group.IsExpanded = true;
				entry.IsSelected = true;
				return true;
		  }

		  if (group.Rami.Any(r => SelectInGroup(r, personId)))
		  {
				group.IsExpanded = true;
				return true;
		  }

		  return false;
	 }

	 private (IReadOnlyList<Dinastia> Dynasties, IReadOnlyList<PersonaDinastia> Links) LoadDynasties()
	 {
		  try
		  {
				return (store.GetDynasties(), store.GetDynastyLinks());
		  }
		  catch (Exception ex) when (ex is IOException or DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
		  {
				return ([], []);
		  }
	 }

	 private void EditDynasties_Click(object sender, RoutedEventArgs e)
	 {
		  new DynastiesWindow(store) { Owner = this }.ShowDialog();
		  ReloadAfterFamilyChange(pedigreeRootId ?? 0);
	 }

	 private void EditEvents_Click(object sender, RoutedEventArgs e) => OpenEvents(CurrentPerson?.Id);

	 private void OpenMap_Click(object sender, RoutedEventArgs e) => OpenMap(null);

	 // Con personId la mappa si apre già filtrata sul personaggio.
	 private void OpenMap(int? personId)
	 {
		  var window = new MapWindow(store, personId) { Owner = this };
		  window.ShowDialog();
		  if (window.Changed)
		  {
				ReloadAfterFamilyChange(personId ?? CurrentPerson?.Id ?? pedigreeRootId ?? 0);
		  }
	 }

	 private void OpenCalendarConverter_Click(object sender, RoutedEventArgs e)
		  => new CalendarConverterWindow() { Owner = this }.Show();

	 private void OpenOffices_Click(object sender, RoutedEventArgs e) => OpenOffices(CurrentPerson?.Id);

	 private void OpenTimeline_Click(object sender, RoutedEventArgs e) => OpenTimeline(CurrentPerson?.Id);

	 private void OpenTimeline(int? personId)
	 {
		  var window = new TimelineWindow(store, personId) { Owner = this };
		  window.ShowDialog();
		  if (window.Changed)
		  {
				ReloadAfterFamilyChange(personId ?? CurrentPerson?.Id ?? pedigreeRootId ?? 0);
		  }
	 }

	 private void OpenOffices(int? personId)
	 {
		  var window = new OfficesWindow(store, personId) { Owner = this };
		  window.ShowDialog();
		  if (window.Changed)
		  {
				ReloadAfterFamilyChange(personId ?? CurrentPerson?.Id ?? pedigreeRootId ?? 0);
		  }
	 }

	 private void EditArtworks_Click(object sender, RoutedEventArgs e) => OpenArtworks(CurrentPerson?.Id);

	 private void OpenArtworks_Click(object sender, RoutedEventArgs e) => OpenArtworks(PeopleTab.IsChecked == true ? CurrentPerson?.Id : null);

	 // Con personId l'elenco si apre filtrato sulle opere collegate al personaggio.
	 private void OpenArtworks(int? personId)
	 {
		  var window = new ArtworksWindow(store, personId) { Owner = this };
		  window.ShowDialog();
		  if (window.PeopleChanged)
		  {
				ReloadAfterFamilyChange(personId ?? CurrentPerson?.Id ?? pedigreeRootId ?? 0);
		  }
		  else
		  {
				SavePeople();
		  }
	 }

	 private void OpenEvents(int? personId)
	 {
		  var window = new EventsWindow(store, personId) { Owner = this };
		  window.ShowDialog();
		  if (window.PeopleChanged)
		  {
				ReloadAfterFamilyChange(personId ?? CurrentPerson?.Id ?? pedigreeRootId ?? 0);
		  }
		  else
		  {
				SavePeople();
		  }
	 }

	 private void SavePeople()
	 {
		  try
		  {
				store.ExportJson(people);
		  }
		  catch (IOException)
		  {
				Feedback.Error(StatusMessage, "Archivio aggiornato, ma non è stato possibile aggiornare il backup JSON.");
		  }

		  UpdatePeopleCount();
	 }

	 private void UpdatePeopleCount()
	 {
		  EmptyMessage.Visibility = people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		  PeopleContent.Visibility = people.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
	 }

	 private void ManagePlaces_Click(object sender, RoutedEventArgs e)
	 {
		  PlacesTab.IsChecked = true;
	 }

    private const string ArchiveFilter = "Archivio Alberi Storici (*.db)|*.db|Tutti i file (*.*)|*.*";

    private void ImportDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Importa archivio", Filter = ArchiveFilter };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!PeopleStore.IsArchive(dialog.FileName))
        {
            MessageBox.Show(this, "Il file selezionato non è un archivio di Alberi Storici.", "Importa archivio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "L’archivio attuale verrà sostituito da quello selezionato. Prima verrà salvata una copia di sicurezza. Continuare?",
            "Importa archivio",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var backup = store.ImportDatabase(dialog.FileName);
            people.Clear();
            LoadPeople();
            // Allinea people.json all'archivio importato, altrimenti un archivio vuoto verrebbe ripopolato dal vecchio backup.
            SavePeople();
            Feedback.Success(StatusMessage, $"Archivio importato. Copia di sicurezza del precedente: {backup}");
        }
        catch (Exception ex) when (ex is AggregateException or IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            MessageBox.Show(this, $"Importazione non riuscita: {ex.Message}", "Importa archivio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Esporta archivio",
            Filter = ArchiveFilter,
            FileName = $"alberi-storici-{DateTime.Now:yyyyMMdd}.db",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            store.ExportDatabase(dialog.FileName);
            Feedback.Success(StatusMessage, $"Archivio esportato in {dialog.FileName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            MessageBox.Show(this, $"Esportazione non riuscita: {ex.Message}", "Esporta archivio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Ripristina" : "Ingrandisci");
        // Senza cornice di sistema, la finestra massimizzata sborderebbe oltre lo schermo.
        RootBorder.Margin = maximized ? new Thickness(7) : new Thickness(0);
    }
}