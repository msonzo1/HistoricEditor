using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class MainWindow : Window
{
    private readonly PeopleStore store = new();

    private readonly ObservableCollection<HistoricalPerson> people = [];

	 private HistoricalPerson? SelectedPerson => (PeopleTree.SelectedItem as PersonEntry)?.Person;

	 // Persona al centro dell'albero genealogico; segue la selezione nella vista Famiglie.
	 private int? pedigreeRootId;

	 private bool IsPedigreeView => PedigreeViewToggle.IsChecked == true;

	 // Persona a cui si applicano i comandi della barra, nella vista attiva.
	 private HistoricalPerson? CurrentPerson => IsPedigreeView
		  ? people.FirstOrDefault(p => p.Id == pedigreeRootId)
		  : SelectedPerson;

	 public MainWindow()
	 {
		  InitializeComponent();
		  Closed += (_, _) => store.Dispose();
		  LoadPeople();
		  SyncThemeMenu();
		  FamiliesViewToggle.IsChecked = true;
		  PeopleTab.IsChecked = true;
	 }

	 private void SectionTab_Checked(object sender, RoutedEventArgs e) => ShowSection();

	 // Mostra solo il pannello della scheda laterale attiva.
	 private void ShowSection()
	 {
		  if (PeoplePanel is null || PlacesPanel is null || EventsPanel is null)
		  {
				return;
		  }

		  var people = PeopleTab.IsChecked == true;
		  var places = PlacesTab.IsChecked == true;
		  var events = EventsTab.IsChecked == true;
		  PeoplePanel.Visibility = people ? Visibility.Visible : Visibility.Collapsed;
		  PlacesPanel.Visibility = places ? Visibility.Visible : Visibility.Collapsed;
		  EventsPanel.Visibility = events ? Visibility.Visible : Visibility.Collapsed;
		  SectionTitle.Text = places ? "LUOGHI" : events ? "EVENTI" : "PERSONAGGI";

		  if (places)
		  {
				PlacesPanel.Activate(store);
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

	 private void NewPerson_Click(object sender, RoutedEventArgs e) => OpenEditor(new PersonEditorWindow(store));

	 private void EditPerson_Click(object sender, RoutedEventArgs e)
	 {
		  if (CurrentPerson is { } person)
		  {
				OpenEditor(new PersonEditorWindow(store, existing: person));
		  }
	 }

	 private void EditFamily_Click(object sender, RoutedEventArgs e)
	 {
		  if (CurrentPerson is { } person && FamilyActions.EditFamily(this, store, person.Id, person.DisplayName))
		  {
				ReloadAfterFamilyChange(person.Id);
		  }
	 }

	 private void AddSpouse_Click(object sender, RoutedEventArgs e)
	 {
		  if (CurrentPerson is { } person && FamilyActions.AddSpouse(this, store, person.Id, person.DisplayName))
		  {
				ReloadAfterFamilyChange(person.Id);
		  }
	 }

	 private void AddChild_Click(object sender, RoutedEventArgs e)
	 {
		  if (CurrentPerson is { } person && FamilyActions.AddChild(this, store, person.Id))
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
				OpenEditor(new PersonEditorWindow(store, existing: person));
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
				OpenEditor(new PersonEditorWindow(store, existing: person));
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

	 private void DeletePerson_Click(object sender, RoutedEventArgs e)
	 {
		  if (CurrentPerson is not { } person)
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

		  pedigreeRootId = null;
		  people.Clear();
		  LoadPeople();
		  Feedback.Success(StatusMessage, "Scheda eliminata.");
		  SavePeople();
	 }

	 private void LoadPeople(int? selectId = null)
	 {
		  try
		  {
				foreach (var person in store.LoadAll())
				{
					 people.Add(person);
				}
		  }
		  catch (Exception ex) when (ex is IOException or DbUpdateException)
		  {
				Feedback.Error(StatusMessage, "Impossibile leggere l’archivio locale.");
		  }

		  UpdatePeopleCount();
		  PeopleTree.ItemsSource = PeopleGrouping.Build(people, LoadDynasties());
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

	 private IReadOnlyList<Dinastia> LoadDynasties()
	 {
		  try
		  {
				return store.GetDynasties();
		  }
		  catch (Exception ex) when (ex is IOException or DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
		  {
				return [];
		  }
	 }

	 private void EditDynasties_Click(object sender, RoutedEventArgs e)
	 {
		  new DynastiesWindow(store) { Owner = this }.ShowDialog();
		  ReloadAfterFamilyChange(pedigreeRootId ?? 0);
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
            Feedback.Success(StatusMessage, $"Archivio importato. Copia di sicurezza del precedente: {backup}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException)
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