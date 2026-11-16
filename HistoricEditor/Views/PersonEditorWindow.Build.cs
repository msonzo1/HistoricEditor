using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class PersonEditorWindow
{
    private readonly System.Collections.ObjectModel.ObservableCollection<PersonTitle> titles = [];

    private void SetTitles(IEnumerable<PersonTitle> source)
    {
        titles.Clear();
        foreach (var title in source)
        {
            // Le date dei titoli si mostrano nel formato in uso («24/06/79 d.C.» → «24/06/0079»).
            var copy = title.Clone();
            copy.Inizio = DateFormats.Normalize(copy.Inizio);
            copy.Fine = DateFormats.Normalize(copy.Fine);
            titles.Add(copy);
        }

        TitlesGrid.ItemsSource = titles;
    }

    private void AddTitle_Click(object sender, RoutedEventArgs e)
    {
        TitlesGrid.CommitEdit();
        TitlesGrid.ItemsSource = titles;
        var title = new PersonTitle();
        titles.Add(title);
        TitlesGrid.SelectedItem = title;
        TitlesGrid.ScrollIntoView(title);
        TitlesGrid.Focus();
    }

    private void RemoveTitle_Click(object sender, RoutedEventArgs e)
    {
        TitlesGrid.CommitEdit();
        if (TitlesGrid.SelectedItem is PersonTitle title)
        {
            titles.Remove(title);
        }
    }

    private string TitlesSnapshot() => string.Join("\u001e", titles.Select(t =>
        $"{t.Titolo}|{t.Numerazione}|{t.Inizio}|{t.CausaInizio}|{t.Fine}|{t.CausaFine}|{t.Predecessore}|{t.Successore}"));
    private string SelectedSex => (SexInput.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? string.Empty;

    private void SelectSex(string sesso)
    {
        SexInput.SelectedItem = SexInput.Items
            .OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => (item.Tag as string) == sesso) ?? SexInput.Items[0];
    }

    private void FillPersonFromForm(HistoricalPerson person)
    {
        ShowWorkingLanguage();
        person.Name = NameInput.Text.Trim();
        person.Surname = SurnameInput.Text.Trim();
        person.Suffix = SuffixInput.Text.Trim();
        person.Family = DynastyNames.CapitalizeAll(FamilyInput.Text);
        // Gens e rami per identificativo; quelli letti dall'import si creano al salvataggio (EnsureSaved).
        (person.GensId, person.RamoGensId) = birthGens.Pending is null ? (birthGens.GensId, birthGens.RamoId) : (null, null);
        (person.GensAdozioneId, person.RamoGensAdozioneId) = adoptiveGens.Pending is null ? (adoptiveGens.GensId, adoptiveGens.RamoId) : (null, null);
        person.Sesso = SelectedSex;
        person.Nickname = NicknameInput.Text.Trim();
        person.MainTitle = MainTitleInput.Text.Trim();
        TitlesGrid.CommitEdit();
        person.Titles = titles.Where(t => !t.IsEmpty()).Select(t => t.Clone()).ToList();
        person.LocalizedNames = LocalizedPersonName.Normalize(localizedNames);
        FillRomanName(person);
        person.Birth = DateFormats.Normalize(BirthInput.Text.Trim());
        person.Death = DateFormats.Normalize(DeathInput.Text.Trim());
        FillDateCalendars(person);
        person.BirthPlace = BirthPlaceField.DisplayText;
        person.DeathPlace = DeathPlaceField.DisplayText;
        person.BurialPlace = BurialPlaceField.DisplayText;
        person.WikipediaTitle = pendingImport?.ArticleTitle ?? person.WikipediaTitle;
        person.WikipediaUrl = pendingImport?.ArticleUrl ?? person.WikipediaUrl;
        person.WikipediaExtract = pendingImport?.Extract ?? person.WikipediaExtract;
        person.WikidataId = pendingImport?.WikidataId ?? person.WikidataId;
        person.WikiTreeId = wikiTreeId;
        person.BirthDateEvidence = BuildDateEvidence(BirthInput.Text, birthSources, pendingImport?.AllBirthEvidence ?? editingPerson?.BirthDateEvidence);
        person.DeathDateEvidence = BuildDateEvidence(DeathInput.Text, deathSources, pendingImport?.AllDeathEvidence ?? editingPerson?.DeathDateEvidence);
        person.BirthPlaces = BuildPlaceRecords(BirthPlaceField.ImportedRecords, BirthPlaceField.GetHistoricalContext());
        person.DeathPlaces = BuildPlaceRecords(DeathPlaceField.ImportedRecords, DeathPlaceField.GetHistoricalContext());
        person.BurialPlaces = BuildPlaceRecords(BurialPlaceField.ImportedRecords, BurialPlaceField.GetHistoricalContext());
        person.WikipediaLeads = (pendingImport?.WikipediaLeads ?? editingPerson?.WikipediaLeads ?? []).Select(l => l.Clone()).ToList();
        person.BirthLuogoId = BirthPlaceField.LuogoId;
        person.BirthLuogoSpecificoId = BirthPlaceField.LuogoSpecificoId;
        person.DeathLuogoId = DeathPlaceField.LuogoId;
        person.DeathLuogoSpecificoId = DeathPlaceField.LuogoSpecificoId;
        person.BurialLuogoId = BurialPlaceField.LuogoId;
        person.BurialLuogoSpecificoId = BurialPlaceField.LuogoSpecificoId;
        person.Note = NoteEditor.Text;
    }

    private void LoadPersonIntoForm(HistoricalPerson person)
    {
        NameInput.Text = person.Name;
        SurnameInput.Text = person.Surname;
        SuffixInput.Text = person.Suffix;
        FamilyInput.Text = person.Family;
        SetGens(new GensChoice(person.GensId, person.RamoGensId, null), new GensChoice(person.GensAdozioneId, person.RamoGensAdozioneId, null));
        SelectSex(person.Sesso);
        NicknameInput.Text = person.Nickname;
        MainTitleInput.Text = person.MainTitle;
        SetTitles(person.Titles);
        SetLocalizedNames(person.LocalizedNames);

        BirthInput.Text = person.BirthDateEvidence.Count > 0 ? FormatDateInput(person.BirthDateEvidence) : DateFormats.Normalize(person.Birth);
        DeathInput.Text = person.DeathDateEvidence.Count > 0 ? FormatDateInput(person.DeathDateEvidence) : DateFormats.Normalize(person.Death);
        SetDateCalendars(person.BirthCalendario, person.BirthStile, person.DeathCalendario, person.DeathStile);
        birthSources = JoinEvidenceSources(person.BirthDateEvidence);
        deathSources = JoinEvidenceSources(person.DeathDateEvidence);
        sourceUrl = person.WikipediaUrl;
        wikiTreeId = person.WikiTreeId;

        LoadPlaceField(BirthPlaceField, person.BirthPlaces, person.BirthLuogoId, person.BirthLuogoSpecificoId);
        LoadPlaceField(DeathPlaceField, person.DeathPlaces, person.DeathLuogoId, person.DeathLuogoSpecificoId);
        LoadPlaceField(BurialPlaceField, person.BurialPlaces, person.BurialLuogoId, person.BurialLuogoSpecificoId);
        NoteEditor.Text = person.Note;
        LoadRomanName(person);
    }

    private void PrefillFromImport(WikipediaImportResult import)
    {
        ShowWorkingLanguage();
        NameInput.Text = import.Name;
        SurnameInput.Text = import.Surname;
        SuffixInput.Text = import.Suffix;
        NicknameInput.Text = import.Nickname;
        MainTitleInput.Text = import.MainTitle;
        SetTitles(import.Titles);
        SetLocalizedNames(import.LocalizedNames);
        FamilyInput.Text = import.Family;
        // Una gens d'adozione scritta a mano resta se Wikidata non ne indica una.
        SetGens(GensChoice.FromImport(import.GensNascita), import.GensAdottiva is not null ? GensChoice.FromImport(import.GensAdottiva) : adoptiveGens);
        SelectSex(import.Sesso);
        PrefillRomanName(import);
        BirthInput.Text = FormatDateInput(import.BirthDates);
        DeathInput.Text = FormatDateInput(import.DeathDates);
        SetDateCalendars(ImportedCalendar(import.BirthDates), HistoricalCalendar.StileComune, ImportedCalendar(import.DeathDates), HistoricalCalendar.StileComune);
        sourceUrl = import.ArticleUrl;
        // Un profilo indicato a mano resta se Wikidata non ne indica uno (aggiornamento di una scheda esistente).
        wikiTreeId = import.WikiTreeId.Length > 0 ? import.WikiTreeId : editingPerson?.WikiTreeId ?? string.Empty;
        UpdateSourcesSummary();

        ApplyImportedPlace(BirthPlaceField, import.BirthPlaces);
        ApplyImportedPlace(DeathPlaceField, import.DeathPlaces);
        ApplyImportedPlace(BurialPlaceField, import.BurialPlaces);
    }

    private static void LoadPlaceField(PlaceFieldControl field, List<HistoricalPlaceRecord> records, int? luogoId, int? specificoId)
    {
        // Il primo contesto storico viene mostrato nel modulo e tolto dai dati conservati, per non duplicarlo al salvataggio.
        var clones = records.Select(ClonePlace).ToList();
        HistoricalPlaceContext? shown = null;
        if (clones.FirstOrDefault(r => r.HistoricalContexts.Count > 0) is { } withContext)
        {
            shown = withContext.HistoricalContexts[0];
            withContext.HistoricalContexts.RemoveAt(0);
        }

        field.ImportedRecords = clones;
        field.SetHistoricalContext(shown);
        field.SetPlace(luogoId, specificoId);
    }

    private void ApplyImportedPlace(PlaceFieldControl field, List<HistoricalPlaceRecord> records)
    {
        field.ImportedRecords = records.Select(ClonePlace).ToList();
        field.SetHistoricalContext(null);
        if (PrepareImportedPlace(records, field.Title) is { } pending)
        {
            field.SetPending(pending);
        }
        else
        {
            field.SetPlace(null, null);
        }
    }

    // Se la catena amministrativa importata è incompleta o incerta, apre l'editor dei luoghi e avvisa l'utente. Il luogo
    // resta in memoria e si crea al salvataggio della scheda (2026-11-15). Un errore lascia il campo vuoto senza
    // interrompere il resto dell'import.
    private PendingPlace? PrepareImportedPlace(IReadOnlyList<HistoricalPlaceRecord> places, string role)
    {
        var place = places.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.CurrentName));
        if (place is null)
        {
            return null;
        }

        try
        {
            return ImportedPlaceResolver.Prepare(store, place, role, this);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            Feedback.Error(EditorMessage, $"{role}: impossibile leggere il luogo «{place.CurrentName}». Sceglierlo manualmente.");
            return null;
        }
    }

    private List<HistoricalDateEvidence> BuildDateEvidence(string dateText, string sourceText, IEnumerable<HistoricalDateEvidence>? importedEvidence)
        => DateEvidenceBuilder.Build(dateText, sourceText, importedEvidence, CreateInputCitation());

    private List<HistoricalPlaceRecord> BuildPlaceRecords(IEnumerable<HistoricalPlaceRecord> records, HistoricalPlaceContext? historicalContext)
    {
        var places = records.Select(ClonePlace).ToList();
        if (historicalContext is null)
        {
            return places;
        }

        if (places.Count == 0)
        {
            places.Add(new HistoricalPlaceRecord());
        }

        var citation = CreateInputCitation();
        if (citation is not null)
        {
            historicalContext.Sources.Add(CloneCitation(citation));
        }

        var target = places[0];
        if (!target.HistoricalContexts.Any(item => item.NameAtTime == historicalContext.NameAtTime
            && item.PoliticalEntity == historicalContext.PoliticalEntity
            && item.AdministrativeUnit == historicalContext.AdministrativeUnit
            && item.Period == historicalContext.Period))
        {
            target.HistoricalContexts.Add(historicalContext);
        }

        return places;
    }

    private static List<string> SplitUserValues(string value) => DateEvidenceBuilder.SplitUserValues(value);

    private SourceCitation? CreateInputCitation()
    {
        var url = sourceUrl.Trim();
        return url.Length == 0 ? null : new SourceCitation { Title = "Fonte inserita", Url = url };
    }

    private static HistoricalPlaceRecord ClonePlace(HistoricalPlaceRecord source) => new()
    {
        WikidataId = source.WikidataId,
        CurrentName = source.CurrentName,
        CurrentCountry = source.CurrentCountry,
        TypeIds = [.. source.TypeIds],
        TypeLabel = source.TypeLabel,
        Names = source.Names.Select(ClonePlaceName).ToList(),
        AdministrativeDivisions = source.AdministrativeDivisions.Select(item => new PlaceDivision
        {
            Relation = item.Relation,
            Name = item.Name,
            Names = item.Names.Select(ClonePlaceName).ToList(),
            WikidataId = item.WikidataId,
            SubjectWikidataId = item.SubjectWikidataId,
            SourceUrl = item.SourceUrl,
        }).ToList(),
        HistoricalContexts = source.HistoricalContexts.Select(item => new HistoricalPlaceContext
        {
            NameAtTime = item.NameAtTime,
            PoliticalEntity = item.PoliticalEntity,
            AdministrativeUnit = item.AdministrativeUnit,
            Period = item.Period,
            Notes = item.Notes,
            Sources = item.Sources.Select(CloneCitation).ToList(),
        }).ToList(),
        Sources = source.Sources.Select(CloneCitation).ToList(),
    };

    private static PlaceName ClonePlaceName(PlaceName source) => new() { Language = source.Language, Name = source.Name };

    private static SourceCitation CloneCitation(SourceCitation source) => new()
    {
        Title = source.Title,
        Url = source.Url,
        RetrievedAt = source.RetrievedAt,
        WikidataId = source.WikidataId,
    };

    private static string FormatDateInput(IEnumerable<HistoricalDateEvidence> evidence) => ImportedPersonCreator.FormatDates(evidence);

    private static string JoinEvidenceSources(IEnumerable<HistoricalDateEvidence> evidence) =>
        string.Join("; ", evidence
            .Where(item => !item.Scartata)
            .SelectMany(item => item.Sources)
            .Select(source => source.Url)
            .Where(url => url.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
}
