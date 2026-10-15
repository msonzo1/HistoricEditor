using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class PersonEditorWindow : Window
{
    private readonly PeopleStore store;
    private WikipediaImportResult? pendingImport;
    private HistoricalPerson? editingPerson;
    private bool relativesLinked;

    // Fonti modificate nella finestra «Fonti…».
    private string sourceUrl = string.Empty;
    private string birthSources = string.Empty;
    private string deathSources = string.Empty;

    // Stato del modulo all'ultimo salvataggio; null = dati importati non ancora salvati.
    private string? savedSnapshot;

    public bool Saved { get; private set; }

    public bool Deleted { get; private set; }

    public int? SavedPersonId => editingPerson?.Id;

    public PersonEditorWindow(PeopleStore store, HistoricalPerson? existing = null, WikipediaImportResult? import = null)
    {
        InitializeComponent();
        this.store = store;
        editingPerson = existing;
        pendingImport = import;
        Closed += (_, _) => wikipediaCancellation?.Cancel();

        BirthPlaceField.Store = store;
        DeathPlaceField.Store = store;
        BurialPlaceField.Store = store;
        LoadDynastyChoices();

        if (existing is not null)
        {
            HeaderText.Text = existing.DisplayName;
            HeaderSubtitle.Text = "Modifica della scheda esistente.";
            LoadPersonIntoForm(existing);
            UpdateDateNotice();
        }
        else if (import is not null)
        {
            HeaderText.Text = string.IsNullOrWhiteSpace(import.Name) ? "Nuova scheda" : import.Name;
            HeaderSubtitle.Text = import.WikidataId.Length == 0
                ? "Importata da Wikipedia (senza collegamento a Wikidata): verifica i dati."
                : "Importata da Wikipedia: verifica i dati prima di salvare.";
            // La precompilazione può aprire l'editor dei luoghi: va eseguita quando la finestra è già visibile.
            Loaded += OnLoadedPrefill;
        }

        UpdateFamilyAccess();
        UpdateNavButtons();
        UpdateWikipediaAccess();
        UpdateSourcesSummary();
        if (import is null)
        {
            savedSnapshot = FormSnapshot();
        }
    }

    private string FormSnapshot()
    {
        static string Place(PlaceFieldControl field)
        {
            var context = field.GetHistoricalContext();
            return $"{field.LuogoId}|{field.LuogoSpecificoId}|{context?.NameAtTime}|{context?.PoliticalEntity}|{context?.AdministrativeUnit}|{context?.Period}";
        }

        // Se è mostrata un'altra lingua, il confronto usa i valori modificabili della scheda.
        var (name, suffix, nickname, mainTitle) = workingValues ?? (NameInput.Text, SuffixInput.Text, NicknameInput.Text, MainTitleInput.Text);
        return string.Join("\u001f",
            name.Trim(), suffix.Trim(), FamilyInput.Text.Trim(), SelectedSex,
            nickname.Trim(), mainTitle.Trim(), TitlesSnapshot(),
            BirthInput.Text.Trim(), DeathInput.Text.Trim(), birthSources, deathSources, sourceUrl,
            Place(BirthPlaceField), Place(DeathPlaceField), Place(BurialPlaceField));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || Deleted || savedSnapshot == FormSnapshot())
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Ci sono modifiche ai dati principali non salvate. Salvarle prima di chiudere?",
            "Modifiche non salvate",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !EnsureSaved()))
        {
            e.Cancel = true;
        }
    }

    private void OnLoadedPrefill(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedPrefill;
        if (pendingImport is not null)
        {
            PrefillFromImport(pendingImport);
            UpdateDateNotice();
        }
    }

    private void UpdateDateNotice()
    {
        var (birth, death) = CurrentDateEvidence();
        var show = DateDiscrepancy.HasAny(birth, death);
        DateNoticePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            DateNoticeText.Text = "Le date di nascita/morte differiscono tra le fonti o le edizioni linguistiche di Wikipedia.";
        }
    }

    private (IReadOnlyList<HistoricalDateEvidence> Birth, IReadOnlyList<HistoricalDateEvidence> Death) CurrentDateEvidence()
    {
        if (pendingImport is not null)
        {
            return (pendingImport.BirthDates, pendingImport.DeathDates);
        }

        if (editingPerson is not null)
        {
            return (editingPerson.BirthDateEvidence, editingPerson.DeathDateEvidence);
        }

        return ([], []);
    }

    private void DateDetails_Click(object sender, RoutedEventArgs e)
    {
        var (birth, death) = CurrentDateEvidence();
        var details = DateDiscrepancy.DescribeAll(birth, death);
        MessageBox.Show(
            this,
            details.Length > 0 ? details : "Nessuna differenza di data registrata.",
            "Differenze tra le date",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Sources_Click(object sender, RoutedEventArgs e)
    {
        var window = new SourcesWindow(sourceUrl, birthSources, deathSources) { Owner = this };
        if (window.ShowDialog() == true)
        {
            sourceUrl = window.SourceUrl;
            birthSources = window.BirthSources;
            deathSources = window.DeathSources;
            UpdateSourcesSummary();
        }
    }

    private void UpdateSourcesSummary()
    {
        var count = (sourceUrl.Length > 0 ? 1 : 0) + SplitUserValues(birthSources).Count + SplitUserValues(deathSources).Count;
        SourcesSummary.Text = count == 0 ? "Nessuna fonte indicata." : count == 1 ? "1 fonte indicata." : $"{count} fonti indicate.";
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs))
        {
            return;
        }

        UpdateFamilyAccess();
        UpdateNavButtons();
    }

    private void UpdateNavButtons()
    {
        var index = Tabs.SelectedIndex;
        BackButton.IsEnabled = index > 0;
        NextButton.Visibility = index < Tabs.Items.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStepHeader(index);
    }

    private void UpdateStepHeader(int index)
    {
        Seg1.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "GreenBrush");
        Seg2.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, index >= 1 ? "GreenBrush" : "AppBorder");
        Seg3.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, index >= 2 ? "GreenBrush" : "AppBorder");
        Seg4.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, index >= 3 ? "GreenBrush" : "AppBorder");
        Step1Label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, index == 0 ? "InkBrush" : "QuietBrush");
        Step2Label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, index == 1 ? "InkBrush" : "QuietBrush");
        Step3Label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, index == 2 ? "InkBrush" : "QuietBrush");
        Step4Label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, index == 3 ? "InkBrush" : "QuietBrush");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedIndex > 0)
        {
            Tabs.SelectedIndex--;
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        // Dalla prima tab si prosegue solo dopo aver salvato i dati principali.
        if (Tabs.SelectedIndex == 0 && !EnsureSaved())
        {
            return;
        }

        if (Tabs.SelectedIndex < Tabs.Items.Count - 1)
        {
            Tabs.SelectedIndex++;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (EnsureSaved())
        {
            DialogResult = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = Saved;

    // Crea o aggiorna la scheda con i dati della prima tab; al primo salvataggio di un import collega i parenti.
    private bool EnsureSaved()
    {
        ShowWorkingLanguage();
        var name = NameInput.Text.Trim();
        if (name.Length == 0)
        {
            Feedback.Error(EditorMessage, "Inserisci almeno il nome del personaggio.");
            Tabs.SelectedIndex = 0;
            NameInput.Focus();
            return false;
        }

        // Si compila una copia: se il salvataggio fallisce, la scheda originale (mostrata nell'elenco) resta invariata.
        var person = editingPerson is null ? new HistoricalPerson() : CopyForEdit(editingPerson);
        FillPersonFromForm(person);

        try
        {
            if (editingPerson is null)
            {
                store.Add(person);
                editingPerson = person;
            }
            else
            {
                store.Update(person);
                FillPersonFromForm(editingPerson);
            }
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            Feedback.Error(EditorMessage, "Impossibile salvare il personaggio nel database.");
            return false;
        }

        Saved = true;
        RegisterFamilyAsDynasty(person.Family);
        savedSnapshot = FormSnapshot();
        HeaderText.Text = person.DisplayName;
        Feedback.Success(EditorMessage, "Scheda salvata.");

        if (pendingImport is not null && !relativesLinked)
        {
            RegisterImportedDynasties(pendingImport);
            LinkImportedRelatives();
        }

        UpdateFamilyAccess();
        UpdateWikipediaAccess();
        return true;
    }

    private void LoadDynastyChoices()
    {
        try
        {
            var text = FamilyInput.Text;
            dynasties = store.GetDynasties();
            FamilyInput.ItemsSource = dynasties.Select(d => d.Nome).ToList();
            FamilyInput.Text = text;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            dynasties = [];
            FamilyInput.ItemsSource = null;
        }

        UpdateDynastyPath();
    }

    private IReadOnlyList<Dinastia> dynasties = [];

    // Mostra sotto il campo la posizione della famiglia nella gerarchia, es. «Ramo di: Asburgo».
    private void UpdateDynastyPath()
    {
        var name = FamilyInput.Text.Trim();
        var dynasty = dynasties.FirstOrDefault(d => string.Equals(d.Nome, name, StringComparison.CurrentCultureIgnoreCase));
        var parents = new List<string>();
        var visited = new HashSet<int>();
        while (dynasty?.ParentId is int p && visited.Add(p) && dynasties.FirstOrDefault(d => d.Id == p) is { } parent)
        {
            parents.Insert(0, parent.Nome);
            dynasty = parent;
        }

        DynastyPathText.Text = name.Length == 0 ? string.Empty
            : parents.Count > 0 ? $"Ramo di: {string.Join(" › ", parents)}"
            : dynasties.Any(d => string.Equals(d.Nome, name, StringComparison.CurrentCultureIgnoreCase)) ? "Dinastia principale"
            : "Nuova dinastia: verrà registrata al salvataggio";
    }

    private void FamilyInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateDynastyPath();

    private void EditDynasty_Click(object sender, RoutedEventArgs e)
    {
        var text = FamilyInput.Text.Trim();
        var window = new DynastiesWindow(store, text) { Owner = this };
        window.ShowDialog();
        LoadDynastyChoices();
        // Se la dinastia è stata rinominata nella finestra, la scheda segue il nuovo nome.
        FamilyInput.Text = window.ResolveRename(text);
        UpdateDynastyPath();
    }

    // Una famiglia scritta a mano diventa una dinastia (senza ramo superiore) se non esiste già.
    private void RegisterFamilyAsDynasty(string family)
    {
        try
        {
            foreach (var name in family.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                store.EnsureDynasty(name, string.Empty, null);
            }
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            Feedback.Error(EditorMessage, "Scheda salvata, ma non è stato possibile registrare la famiglia tra le dinastie.");
            return;
        }

        LoadDynastyChoices();
    }

    // Registra le famiglie importate come dinastie, collegandole alla dinastia di cui sono ramo.
    private void RegisterImportedDynasties(WikipediaImportResult import)
    {
        try
        {
            foreach (var dynasty in import.Dynasties.Where(d => d.Nome.Trim().Length > 0))
            {
                int? parentId = dynasty.ParentNome.Trim().Length > 0
                    ? store.EnsureDynasty(dynasty.ParentNome, dynasty.ParentWikidataId, null)
                    : null;
                store.EnsureDynasty(dynasty.Nome, dynasty.WikidataId, parentId);
            }
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            Feedback.Error(EditorMessage, "Scheda salvata, ma non è stato possibile registrare le dinastie.");
        }
    }

    private static HistoricalPerson CopyForEdit(HistoricalPerson source) => new()
    {
        Id = source.Id,
        FatherId = source.FatherId,
        MotherId = source.MotherId,
        WikipediaTitle = source.WikipediaTitle,
        WikipediaUrl = source.WikipediaUrl,
        WikipediaExtract = source.WikipediaExtract,
        WikidataId = source.WikidataId,
    };

    // Collega i parenti letti da Wikidata solo se già presenti in archivio (match per WikidataId).
    private void LinkImportedRelatives()
    {
        relativesLinked = true;
        if (editingPerson is not { } person || pendingImport is null)
        {
            return;
        }

        var linked = 0;
        foreach (var fatherId in pendingImport.FatherIds)
        {
            linked += TryLink(fatherId, id => store.SetParent(person.Id, id, mother: false));
        }

        foreach (var motherId in pendingImport.MotherIds)
        {
            linked += TryLink(motherId, id => store.SetParent(person.Id, id, mother: true));
        }

        foreach (var spouseId in pendingImport.SpouseIds)
        {
            linked += TryLink(spouseId, id => store.SaveUnion(new Unione { Persona1Id = person.Id, Persona2Id = id }));
        }

        var ownSex = store.GetSex(person.Id);
        if (ownSex is Sessi.Maschio or Sessi.Femmina)
        {
            foreach (var childId in pendingImport.ChildIds)
            {
                linked += TryLink(childId, id => store.AssignChild(person.Id, id, null));
            }
        }

        if (linked > 0)
        {
            Feedback.Success(EditorMessage, $"Scheda salvata. Collegati {linked} parenti già in archivio: controllali nella tab «Parentele».");
        }
    }

    // Restituisce 1 solo se il parente era in archivio ed è stato collegato.
    private int TryLink(string wikidataId, Action<int> link)
    {
        if (store.FindPersonIdByWikidataId(wikidataId) is not int id)
        {
            return 0;
        }

        try
        {
            link(id);
            return 1;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
        catch (DbUpdateException)
        {
            return 0;
        }
    }

    private void UpdateFamilyAccess()
    {
        var available = editingPerson is not null;
        FamilyScroll.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        FamilyLockedText.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        DeletePersonButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (available && Tabs.SelectedItem == FamilyTab)
        {
            RefreshFamily();
        }
    }

    private void DeletePerson_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is not { } person)
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
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            Feedback.Error(EditorMessage, "Impossibile eliminare la scheda.");
            return;
        }

        Saved = true;
        Deleted = true;
        DialogResult = true;
    }

    private void RefreshFamily()
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var family = store.GetFamily(person.Id);
        FatherText.Text = family.Padre?.Etichetta ?? "Non indicato";
        MotherText.Text = family.Madre?.Etichetta ?? "Non indicata";
        UnionsList.ItemsSource = family.Unioni;
        NoUnionsText.Visibility = family.Unioni.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherChildrenList.ItemsSource = family.AltriFigli;
    }

    private void RunFamily(Action action, string? success = null)
    {
        try
        {
            action();
            Saved = true;
            Feedback.Success(FamilyMessage, success ?? string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            Feedback.Error(FamilyMessage, ex.Message);
        }
        catch (DbUpdateException)
        {
            Feedback.Error(FamilyMessage, "Impossibile salvare la modifica.");
        }

        RefreshFamily();
    }

    private void ChooseFather_Click(object sender, RoutedEventArgs e) => ChooseParent(mother: false);

    private void ChooseMother_Click(object sender, RoutedEventArgs e) => ChooseParent(mother: true);

    private void ChooseParent(bool mother)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var picker = new PersonPickerWindow(store, mother ? "Scegli la madre" : "Scegli il padre", person.Id, mother ? Sessi.Femmina : Sessi.Maschio) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedId is int id)
        {
            RunFamily(() => store.SetParent(person.Id, id, mother));
        }
    }

    private void RemoveFather_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is { } person)
        {
            RunFamily(() => store.SetParent(person.Id, null, mother: false));
        }
    }

    private void RemoveMother_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is { } person)
        {
            RunFamily(() => store.SetParent(person.Id, null, mother: true));
        }
    }

    private void AddSpouse_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var picker = new PersonPickerWindow(store, "Scegli il coniuge", person.Id, Sessi.Opposto(store.GetSex(person.Id))) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedId is int spouseId)
        {
            EditUnion(new Unione { Persona1Id = person.Id, Persona2Id = spouseId });
        }
    }

    private void RegisterUnion_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is { } person && (sender as FrameworkElement)?.DataContext is UnionView { Coniuge: { } coniuge })
        {
            EditUnion(new Unione { Persona1Id = person.Id, Persona2Id = coniuge.Id });
        }
    }

    private void EditUnion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UnionView { UnionId: int id } && store.GetUnion(id) is { } unione)
        {
            EditUnion(unione);
        }
    }

    private void EditUnion(Unione unione)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var spouseId = unione.Persona1Id == person.Id ? unione.Persona2Id : unione.Persona1Id;
        var spouse = store.GetPersonSummaries().FirstOrDefault(p => p.Id == spouseId)?.Nome ?? "coniuge";
        var editor = new UnionEditorWindow(store, unione, $"Unione tra {person.DisplayName} e {spouse}") { Owner = this };
        if (editor.ShowDialog() == true)
        {
            Saved = true;
            RefreshFamily();
        }
    }

    private void RemoveUnion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not UnionView { UnionId: int id } view)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Rimuovere l’unione con {view.Coniuge?.Nome ?? "il coniuge"}? I figli restano collegati a entrambi i genitori.",
            "Rimuovi unione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            RunFamily(() => store.DeleteUnion(id));
        }
    }

    private void AddChildToUnion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UnionView view)
        {
            AddChild(view.Coniuge?.Id);
        }
    }

    private void AddChild_Click(object sender, RoutedEventArgs e) => AddChild(null);

    private void AddChild(int? otherParentId)
    {
        if (editingPerson is not { } person)
        {
            return;
        }

        var sesso = store.GetSex(person.Id);
        if (sesso is not (Sessi.Maschio or Sessi.Femmina))
        {
            Feedback.Error(FamilyMessage, "Indica prima il sesso nella tab «Dati principali», per sapere se è padre o madre.");
            return;
        }

        var picker = new PersonPickerWindow(store, "Scegli il figlio o la figlia", person.Id, string.Empty) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedId is not int childId)
        {
            return;
        }

        var (motherId, fatherId) = store.GetParents(childId);
        var ownSlot = sesso == Sessi.Femmina ? motherId : fatherId;
        var otherSlot = sesso == Sessi.Femmina ? fatherId : motherId;
        var replaces = (ownSlot is int own && own != person.Id) || (otherParentId is int other && otherSlot is int existing && existing != other);
        if (replaces && MessageBox.Show(
                this,
                "Questa persona ha già un altro genitore registrato. Sostituirlo?",
                "Aggiungi figlio",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        RunFamily(() => store.AssignChild(person.Id, childId, otherParentId));
    }

    private void RemoveChild_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is { } person && (sender as FrameworkElement)?.DataContext is PersonSummary child)
        {
            RunFamily(() => store.RemoveChild(person.Id, child.Id));
        }
    }
}
