using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
    private string wikiTreeId = string.Empty;
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
        InitializeCalendarInputs();
        InitializeRomanInputs();
        this.store = store;
        editingPerson = existing;
        pendingImport = import;
        Closed += (_, _) => wikipediaCancellation?.Cancel();

        BirthPlaceField.Store = store;
        DeathPlaceField.Store = store;
        BurialPlaceField.Store = store;
        LoadDynastyChoices();
        RebuildLanguageOptions(null);

        if (existing is not null)
        {
            HeaderText.Text = existing.DisplayName;
            HeaderSubtitle.Text = "Modifica della scheda esistente.";
            LoadPersonIntoForm(existing);
            UpdateDateNotice();
        }
        else if (import is not null)
        {
            HeaderText.Text = string.IsNullOrWhiteSpace(import.Name) ? "Nuova scheda" : HistoricalPerson.ComposeName(import.Name, import.Surname, import.Suffix);
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
            return $"{field.LuogoId}|{field.LuogoSpecificoId}|{field.Pending?.ChangeKey}|{context?.NameAtTime}|{context?.PoliticalEntity}|{context?.AdministrativeUnit}|{context?.Period}";
        }

        // Se è mostrata un'altra lingua, il confronto usa i valori modificabili della scheda.
        var (name, suffix, nickname, mainTitle) = ItalianNames();
        return string.Join("\u001f",
            name.Trim(), SurnameInput.Text.Trim(), suffix.Trim(), FamilyInput.Text.Trim(), SelectedSex,
            nickname.Trim(), mainTitle.Trim(), TitlesSnapshot(), LocalizedNamesSnapshot(), RomanSnapshot(),
            BirthInput.Text.Trim(), DeathInput.Text.Trim(), DateCalendarsSnapshot(), birthSources, deathSources, sourceUrl, wikiTreeId,
            Place(BirthPlaceField), Place(DeathPlaceField), Place(BurialPlaceField), NoteEditor.Text);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || Deleted || closingToOpenExisting || savedSnapshot == FormSnapshot())
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

    // Le date discordanti tra le fonti (più ipotesi o date scartate perché indicate da una sola fonte) stanno nella
    // riga delle fonti, accanto al LED di concordanza, che le comprende.
    private void UpdateDateNotice()
    {
        UpdateConsensusLed();
        var (birth, death) = CurrentDateEvidence();
        var text = DateDiscrepancy.DescribeAll(birth, death);
        DateDiscordanceText.Text = text.Length == 0 ? string.Empty : "Date discordanti tra le fonti." + Environment.NewLine + text;
        DateDiscordanceText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private WikipediaConsensusResult EvaluateConsensus()
    {
        var leads = pendingImport is not null ? pendingImport.WikipediaLeads : editingPerson?.WikipediaLeads;
        var (birth, death) = CurrentDateEvidence();
        return WikipediaConsensus.Evaluate(leads, birth, death);
    }

    private void UpdateConsensusLed()
    {
        var result = EvaluateConsensus();
        if (result.Level == ConsensusLevel.Hidden)
        {
            ConsensusLed.Visibility = Visibility.Collapsed;
            return;
        }

        var (color, state) = result.Level switch
        {
            ConsensusLevel.Green => ((Brush)FindResource("GreenBrush"), "verde"),
            ConsensusLevel.Orange => (new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x1E)), "arancione"),
            _ => ((Brush)FindResource("DangerBrush"), "rosso"),
        };
        ConsensusLight.Fill = color;
        ConsensusDetails.Text = result.Details;
        AutomationProperties.SetName(ConsensusLed, $"Concordanza tra le edizioni di Wikipedia: {state}. {result.Summary}");
        AutomationProperties.SetHelpText(ConsensusLed, result.Details + Environment.NewLine + "Premi per vedere le edizioni confrontate e aprire i collegamenti.");
        ConsensusLed.Visibility = Visibility.Visible;
    }

    private void ConsensusLed_Click(object sender, RoutedEventArgs e)
    {
        var result = EvaluateConsensus();
        if (result.Level == ConsensusLevel.Hidden)
        {
            return;
        }

        new WikipediaConsensusWindow(result) { Owner = this }.ShowDialog();
    }

    private (IReadOnlyList<HistoricalDateEvidence> Birth, IReadOnlyList<HistoricalDateEvidence> Death) CurrentDateEvidence()
    {
        if (pendingImport is not null)
        {
            return ([.. pendingImport.AllBirthEvidence], [.. pendingImport.AllDeathEvidence]);
        }

        if (editingPerson is not null)
        {
            return (editingPerson.BirthDateEvidence, editingPerson.DeathDateEvidence);
        }

        return ([], []);
    }

    private void Sources_Click(object sender, RoutedEventArgs e)
    {
        var window = new SourcesWindow(sourceUrl, birthSources, deathSources, wikiTreeId) { Owner = this };
        if (window.ShowDialog() == true)
        {
            sourceUrl = window.SourceUrl;
            birthSources = window.BirthSources;
            deathSources = window.DeathSources;
            wikiTreeId = window.WikiTreeId;
            UpdateSourcesSummary();
        }
    }

    private void UpdateSourcesSummary()
    {
        var count = (sourceUrl.Length > 0 ? 1 : 0) + (wikiTreeId.Length > 0 ? 1 : 0) + SplitUserValues(birthSources).Count + SplitUserValues(deathSources).Count;
        SourcesSummary.Text = count == 0 ? "Nessuna fonte indicata." : count == 1 ? "1 fonte indicata." : $"{count} fonti indicate.";
        var link = SourceLinkUri(sourceUrl);
        SourceLinkButton.Content = link?.AbsoluteUri ?? string.Empty;
        SourceLinkButton.ToolTip = link is null ? null : "Apri la fonte nel browser";
        SourceLinkButton.Visibility = link is null ? Visibility.Collapsed : Visibility.Visible;
        WikiTreeLinkButton.Content = wikiTreeId.Length > 0 ? $"WikiTree: {wikiTreeId}" : string.Empty;
        WikiTreeLinkButton.ToolTip = wikiTreeId.Length > 0 ? WikiTreeLinks.ProfileUrl(wikiTreeId) : null;
        WikiTreeLinkButton.Visibility = wikiTreeId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WikiTreeLink_Click(object sender, RoutedEventArgs e) => OpenInBrowser(SourceLinkUri(WikiTreeLinks.ProfileUrl(wikiTreeId)));

    // Solo indirizzi web: un testo libero non diventa un collegamento.
    internal static Uri? SourceLinkUri(string text)
        => Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    private void SourceLink_Click(object sender, RoutedEventArgs e) => OpenInBrowser(SourceLinkUri(sourceUrl));

    private void OpenInBrowser(Uri? uri)
    {
        if (uri is null)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Feedback.Error(EditorMessage, "Impossibile aprire il collegamento nel browser.");
        }
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
        if (!ConfirmNoDuplicates(person))
        {
            return false;
        }

        // I luoghi importati si creano adesso, insieme alla scheda: se il salvataggio fallisce non resta nessuno dei due e i
        // campi restano in attesa (2026-11-15).
        var placeFields = new[] { BirthPlaceField, DeathPlaceField, BurialPlaceField };
        var created = new (int? LuogoId, int? SpecificoId)?[placeFields.Length];
        try
        {
            store.SaveTogether(() =>
            {
                for (var i = 0; i < placeFields.Length; i++)
                {
                    created[i] = placeFields[i].HasImportedPlace ? placeFields[i].CreatePending() : null;
                }

                SetCreatedPlaces(person, created);
                // Gens e rami letti dall'import si creano adesso, insieme alla scheda (2026-11-15).
                (person.GensId, person.RamoGensId) = GensChoices.Resolve(store, birthGens);
                (person.GensAdozioneId, person.RamoGensAdozioneId) = GensChoices.Resolve(store, adoptiveGens);
                if (editingPerson is null)
                {
                    store.Add(person);
                }
                else
                {
                    store.Update(person);
                }
            });

            for (var i = 0; i < placeFields.Length; i++)
            {
                if (created[i] is { } ids)
                {
                    placeFields[i].SetPlace(ids.LuogoId, ids.SpecificoId);
                }
            }

            birthGens = new GensChoice(person.GensId, person.RamoGensId, null);
            adoptiveGens = new GensChoice(person.GensAdozioneId, person.RamoGensAdozioneId, null);

            if (editingPerson is null)
            {
                editingPerson = person;
            }
            else
            {
                FillPersonFromForm(editingPerson);
            }
        }
        catch (InvalidOperationException ex)
        {
            Feedback.Error(EditorMessage, ex.Message);
            return false;
        }
        catch (DbUpdateException)
        {
            Feedback.Error(EditorMessage, "Impossibile salvare il personaggio nel database.");
            return false;
        }

        Saved = true;
        // Le dinastie importate vanno registrate prima di quelle scritte, così una gens indicata da Wikidata ha il tipo giusto.
        var firstImportSave = pendingImport is not null && !relativesLinked;
        if (firstImportSave)
        {
            RegisterImportedDynasties(pendingImport!);
        }

        RegisterFamilyAsDynasty(person.Family, TipiDinastia.Dinastia);
        savedSnapshot = FormSnapshot();
        HeaderText.Text = person.DisplayName;
        // La gens di nascita è quasi sempre nota: se manca in una scheda romana si salva lo stesso, ma lo si segnala.
        if (romanForm && person.GensId is null)
        {
            Feedback.Error(EditorMessage, "Scheda salvata, ma manca la gens di nascita (pulsante «Dinastia e gens…»).");
        }
        else
        {
            Feedback.Success(EditorMessage, "Scheda salvata.");
        }

        if (firstImportSave)
        {
            LinkImportedRelatives();
            RefreshInheritedFamily();
        }

        UpdateFamilyAccess();
        UpdateWikipediaAccess();
        return true;
    }

    // Luoghi appena creati per i campi in attesa (nascita, morte, sepoltura): id e testo come per un luogo scelto.
    private void SetCreatedPlaces(HistoricalPerson person, (int? LuogoId, int? SpecificoId)?[] created)
    {
        string Text((int? LuogoId, int? SpecificoId) ids) => store.DescribeRole(ids.LuogoId, ids.SpecificoId).DisplayText;
        if (created[0] is { } birth)
        {
            (person.BirthLuogoId, person.BirthLuogoSpecificoId, person.BirthPlace) = (birth.LuogoId, birth.SpecificoId, Text(birth));
        }

        if (created[1] is { } death)
        {
            (person.DeathLuogoId, person.DeathLuogoSpecificoId, person.DeathPlace) = (death.LuogoId, death.SpecificoId, Text(death));
        }

        if (created[2] is { } burial)
        {
            (person.BurialLuogoId, person.BurialLuogoSpecificoId, person.BurialPlace) = (burial.LuogoId, burial.SpecificoId, Text(burial));
        }
    }

    private void LoadDynastyChoices()
    {
        try
        {
            var text = FamilyInput.Text;
            dynasties = store.GetDynasties();
            gentes = store.GetGentes();
            // Nel campo Famiglia le dinastie; le gens si scelgono nel modulo «Dinastia e gens».
            FamilyInput.ItemsSource = dynasties.Where(d => !d.IsGens).Select(d => d.Nome).ToList();
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

    // Mostra sotto il campo la posizione di ciascuna famiglia nella gerarchia, es. «Ramo di: Asburgo».
    private void UpdateDynastyPath()
    {
        DynastyPathText.Text = DynastyDescriptions.Hint(dynasties, FamilyInput.Text, gens: false);
        DynastyMenu.UpdateButton(DynastyButton, FamilyInput.Text);
        UpdateRomanFamily();
    }

    private void FamilyInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateDynastyPath();

    // Con più dinastie nel campo, un menu sceglie quale aprire nell'archivio.
    private void EditDynasty_Click(object sender, RoutedEventArgs e)
        => DynastyMenu.Choose(DynastyButton, dynasties, FamilyInput.Text, OpenDynastyArchive);

    private void OpenDynastyArchive(string? name)
    {
        var names = PeopleGrouping.FamilyNames(FamilyInput.Text);
        var window = new DynastiesWindow(store, name) { Owner = this };
        window.ShowDialog();
        LoadDynastyChoices();
        // Se una dinastia è stata rinominata nella finestra, la scheda segue il nuovo nome.
        if (names.Count > 0)
        {
            FamilyInput.Text = PeopleGrouping.JoinFamilyNames(names.Select(window.ResolveRename).Distinct(PeopleGrouping.FamilyComparer));
        }

        UpdateDynastyPath();
    }

    // Una famiglia (o una gens, dal campo Gens) scritta a mano viene registrata senza ramo superiore, se non esiste già.
    private void RegisterFamilyAsDynasty(string family, string tipo)
    {
        try
        {
            foreach (var name in family.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                store.EnsureDynasty(name, string.Empty, null, tipo);
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
        if (!ImportedPersonCreator.RegisterDynasties(store, string.Empty, import))
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
        WikiTreeId = source.WikiTreeId,
    };

    // La famiglia ereditata dal padre durante il collegamento dei parenti va riportata nel modulo,
    // altrimenti un nuovo salvataggio la cancellerebbe.
    private void RefreshInheritedFamily()
    {
        if (editingPerson is not { } person || store.GetPerson(person.Id) is not { } saved)
        {
            return;
        }

        var changed = false;
        if (FamilyInput.Text.Trim().Length == 0 && saved.Family.Length > 0)
        {
            FamilyInput.Text = saved.Family;
            person.Family = saved.Family;
            changed = true;
        }

        if (birthGens.IsEmpty && saved.GensId is not null)
        {
            SetGens(new GensChoice(saved.GensId, saved.RamoGensId, null), adoptiveGens);
            (person.GensId, person.RamoGensId) = (saved.GensId, saved.RamoGensId);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        savedSnapshot = FormSnapshot();
        LoadDynastyChoices();
    }

    // Collega i parenti già in archivio; poi propone quelli mancanti e importa gli eventi del personaggio.
    private void LinkImportedRelatives()
    {
        relativesLinked = true;
        if (editingPerson is not { } person || pendingImport is null)
        {
            return;
        }

        var import = pendingImport;
        var result = ImportedPersonCreator.LinkRelatives(store, person.Id, import);
        var parts = new List<string>();
        if (result.Relatives > 0)
        {
            parts.Add($"{result.Relatives} parenti");
        }

        if (result.Relations > 0)
        {
            parts.Add(result.Relations == 1 ? "1 relazione (maestri, allievi, influenze)" : $"{result.Relations} relazioni (maestri, allievi, influenze)");
        }

        var message = parts.Count > 0
            ? $"Scheda salvata. Collegati {string.Join(" e ", parts)} con personaggi già in archivio: controlla la tab «Parentele e relazioni»."
            : "Scheda salvata.";
        // I parenti mancanti non vengono più proposti in automatico: l'utente li apre dal pulsante nella tab.
        UpdateMissingRelativesAccess();
        if (MissingRelativesPanel.Visibility == Visibility.Visible)
        {
            message += " Usa «Importa parenti mancanti…» nella tab «Parentele e relazioni» per completare l'importazione.";
        }

        if (parts.Count > 0 || MissingRelativesPanel.Visibility == Visibility.Visible)
        {
            Feedback.Success(EditorMessage, message);
        }
    }

    // Mostra il pulsante dei parenti mancanti solo se c'è ancora qualcosa da proporre dall'import.
    private void UpdateMissingRelativesAccess()
    {
        var available = relativesLinked && editingPerson is { } person && pendingImport is { } import
            && PendingCompletion(person, import) is not null;
        MissingRelativesPanel.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

    private (IReadOnlyList<MissingRelative> Missing, bool UseWikiTree)? PendingCompletion(HistoricalPerson person, WikipediaImportResult import)
    {
        if (import.WikidataId.Length == 0 && person.WikiTreeId.Length == 0)
        {
            return null;
        }

        IReadOnlyList<MissingRelative> missing;
        try
        {
            missing = import.WikidataId.Length == 0 ? [] : ImportedPersonCreator.MissingRelatives(store, import);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            missing = [];
        }

        var useWikiTree = person.WikiTreeId.Length > 0;
        if (missing.Count == 0 && import.EventIds.Count == 0 && !useWikiTree)
        {
            return null;
        }

        return (missing, useWikiTree);
    }

    private void MissingRelatives_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is not { } person || pendingImport is not { } import)
        {
            return;
        }

        var summary = CompleteImport(person, import);
        RefreshFamily();
        UpdateMissingRelativesAccess();
        if (summary.Length > 0)
        {
            Saved = true;
            Feedback.Success(EditorMessage, summary);
        }
    }

    // Parenti senza scheda (con conferma, da Wikidata e WikiTree) ed eventi del personaggio.
    private string CompleteImport(HistoricalPerson person, WikipediaImportResult import)
    {
        if (PendingCompletion(person, import) is not { } pending)
        {
            return string.Empty;
        }

        var (missing, useWikiTree) = pending;

        var intro = missing.Count == 0 && !useWikiTree
            ? $"Wikidata indica eventi di {person.DisplayName} non ancora in archivio."
            : (missing.Count == 0
                ? $"Ricerca su WikiTree dei parenti di {person.DisplayName} senza scheda in archivio. "
                : $"Wikidata indica {(missing.Count == 1 ? "1 parente" : $"{missing.Count} parenti")} di {person.DisplayName} senza scheda in archivio"
                    + (useWikiTree ? "; altri possono venire da WikiTree. " : ". "))
                + "Scegli quali importare: avranno una scheda, collegata a questa."
                + (useWikiTree ? " Con «Importa» vengono completate anche le parentele indicate da WikiTree tra schede già in archivio." : string.Empty);
        var window = new ImportPeopleWindow(store, wikipediaService, intro, missing, person.Id, import, importEvents: true, wikiTreeId: person.WikiTreeId)
        {
            Owner = this,
            Title = missing.Count == 0 && !useWikiTree ? "Eventi del personaggio" : "Parenti mancanti",
        };
        window.ShowDialog();
        LoadDynastyChoices();
        return window.Summary;
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

        UpdateEventsAccess();
        UpdateAttachmentsSummary();
    }

    private void UpdateAttachmentsSummary()
    {
        AttachmentsButton.IsEnabled = editingPerson is not null;
        if (editingPerson is null)
        {
            return;
        }

        var count = store.GetAttachments(AmbitiAllegato.Persona, editingPerson.Id).Count;
        var portrait = store.GetPortrait(editingPerson.Id) is not null ? " (con ritratto)" : string.Empty;
        AttachmentsSummary.Text = count switch
        {
            0 => "Nessun allegato: aggiungi ritratti, immagini o documenti.",
            1 => $"1 allegato{portrait}.",
            _ => $"{count} allegati{portrait}.",
        };
    }

    private void Attachments_Click(object sender, RoutedEventArgs e)
    {
        if (editingPerson is null)
        {
            return;
        }

        var window = new AttachmentsWindow(store, AmbitiAllegato.Persona, editingPerson.Id, editingPerson.DisplayName) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            Saved = true;
        }

        UpdateAttachmentsSummary();
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
        SetRelativeLink(FatherText, family.Padre, "Non indicato");
        SetRelativeLink(MotherText, family.Madre, "Non indicata");
        UnionsList.ItemsSource = family.Unioni;
        NoUnionsText.Visibility = family.Unioni.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherChildrenList.ItemsSource = family.AltriFigli;
        RefreshRelations(person.Id);
    }

    private static void SetRelativeLink(Button link, PersonSummary? relative, string empty)
    {
        link.Content = relative?.Etichetta ?? empty;
        link.Tag = relative?.Id;
        link.IsEnabled = relative is not null;
    }

    // Apre la scheda del parente sopra a questa; alla chiusura le parentele vengono ricaricate.
    private void OpenRelative_Click(object sender, RoutedEventArgs e)
    {
        var id = (sender as FrameworkElement)?.Tag as int? ?? ((sender as FrameworkElement)?.DataContext as PersonSummary)?.Id;
        if (id is not int relativeId || relativeId == editingPerson?.Id)
        {
            return;
        }

        if (store.GetPerson(relativeId) is not { } relative)
        {
            Feedback.Error(FamilyMessage, "La scheda del parente non è più in archivio.");
            RefreshFamily();
            return;
        }

        var editor = new PersonEditorWindow(store, relative) { Owner = this };
        editor.ShowDialog();
        if (editor.Saved)
        {
            Saved = true;
            LoadDynastyChoices();
            RefreshFamily();
        }
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
        if (picker.ShowDialog() == true && picker.SelectedId is int spouseId
            && FamilyActions.ConfirmNewUnion(this, store, person.Id, spouseId, store.GetPersonSummaries().FirstOrDefault(p => p.Id == spouseId)?.Nome ?? "il coniuge"))
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

        if (!FamilyActions.ConfirmParentReplacement(this, store, person.Id, childId, otherParentId))
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
