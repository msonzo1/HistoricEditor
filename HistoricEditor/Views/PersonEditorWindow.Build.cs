using System.Windows;

namespace HistoricEditor;

public partial class PersonEditorWindow
{
    private readonly System.Collections.ObjectModel.ObservableCollection<PersonTitle> titles = [];

    private void SetTitles(IEnumerable<PersonTitle> source)
    {
        titles.Clear();
        foreach (var title in source)
        {
            titles.Add(title.Clone());
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
        $"{t.Titolo}|{t.Numerazione}|{t.Inizio}|{t.CausaInizio}|{t.Fine}|{t.CausaFine}"));
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
        person.Suffix = SuffixInput.Text.Trim();
        person.Family = FamilyInput.Text.Trim();
        person.Sesso = SelectedSex;
        person.Nickname = NicknameInput.Text.Trim();
        person.MainTitle = MainTitleInput.Text.Trim();
        TitlesGrid.CommitEdit();
        person.Titles = titles.Where(t => !t.IsEmpty()).Select(t => t.Clone()).ToList();
        person.LocalizedNames = localizedNames.Select(n => n.Clone()).ToList();
        person.Birth = BirthInput.Text.Trim();
        person.Death = DeathInput.Text.Trim();
        person.BirthPlace = BirthPlaceField.DisplayText;
        person.DeathPlace = DeathPlaceField.DisplayText;
        person.BurialPlace = BurialPlaceField.DisplayText;
        person.WikipediaTitle = pendingImport?.ArticleTitle ?? person.WikipediaTitle;
        person.WikipediaUrl = pendingImport?.ArticleUrl ?? person.WikipediaUrl;
        person.WikipediaExtract = pendingImport?.Extract ?? person.WikipediaExtract;
        person.WikidataId = pendingImport?.WikidataId ?? person.WikidataId;
        person.BirthDateEvidence = BuildDateEvidence(BirthInput.Text, birthSources, pendingImport?.BirthDates ?? editingPerson?.BirthDateEvidence);
        person.DeathDateEvidence = BuildDateEvidence(DeathInput.Text, deathSources, pendingImport?.DeathDates ?? editingPerson?.DeathDateEvidence);
        person.BirthPlaces = BuildPlaceRecords(BirthPlaceField.ImportedRecords, BirthPlaceField.GetHistoricalContext());
        person.DeathPlaces = BuildPlaceRecords(DeathPlaceField.ImportedRecords, DeathPlaceField.GetHistoricalContext());
        person.BurialPlaces = BuildPlaceRecords(BurialPlaceField.ImportedRecords, BurialPlaceField.GetHistoricalContext());
        person.BirthLuogoId = BirthPlaceField.LuogoId;
        person.BirthLuogoSpecificoId = BirthPlaceField.LuogoSpecificoId;
        person.DeathLuogoId = DeathPlaceField.LuogoId;
        person.DeathLuogoSpecificoId = DeathPlaceField.LuogoSpecificoId;
        person.BurialLuogoId = BurialPlaceField.LuogoId;
        person.BurialLuogoSpecificoId = BurialPlaceField.LuogoSpecificoId;
    }

    private void LoadPersonIntoForm(HistoricalPerson person)
    {
        NameInput.Text = person.Name;
        SuffixInput.Text = person.Suffix;
        FamilyInput.Text = person.Family;
        SelectSex(person.Sesso);
        NicknameInput.Text = person.Nickname;
        MainTitleInput.Text = person.MainTitle;
        SetTitles(person.Titles);
        SetLocalizedNames(person.LocalizedNames);

        BirthInput.Text = person.BirthDateEvidence.Count > 0 ? FormatDateInput(person.BirthDateEvidence) : person.Birth;
        DeathInput.Text = person.DeathDateEvidence.Count > 0 ? FormatDateInput(person.DeathDateEvidence) : person.Death;
        birthSources = JoinEvidenceSources(person.BirthDateEvidence);
        deathSources = JoinEvidenceSources(person.DeathDateEvidence);
        sourceUrl = person.WikipediaUrl;

        LoadPlaceField(BirthPlaceField, person.BirthPlaces, person.BirthLuogoId, person.BirthLuogoSpecificoId);
        LoadPlaceField(DeathPlaceField, person.DeathPlaces, person.DeathLuogoId, person.DeathLuogoSpecificoId);
        LoadPlaceField(BurialPlaceField, person.BurialPlaces, person.BurialLuogoId, person.BurialLuogoSpecificoId);
    }

    private void PrefillFromImport(WikipediaImportResult import)
    {
        ShowWorkingLanguage();
        NameInput.Text = import.Name;
        SuffixInput.Text = import.Suffix;
        NicknameInput.Text = import.Nickname;
        MainTitleInput.Text = import.MainTitle;
        SetTitles(import.Titles);
        SetLocalizedNames(import.LocalizedNames);
        FamilyInput.Text = import.Family;
        SelectSex(import.Sesso);
        BirthInput.Text = FormatDateInput(import.BirthDates);
        DeathInput.Text = FormatDateInput(import.DeathDates);
        sourceUrl = import.ArticleUrl;
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
        var (luogoId, specificoId) = ResolveOrEditPlace(records, field.Title);
        field.SetPlace(luogoId, specificoId);
    }

    // Se la catena amministrativa importata è incompleta o incerta, apre l'editor dei luoghi e avvisa l'utente.
    private (int? LuogoId, int? SpecificoId) ResolveOrEditPlace(IReadOnlyList<HistoricalPlaceRecord> places, string role)
    {
        var place = places.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.CurrentName));
        if (place is null)
        {
            return (null, null);
        }

        var derived = store.DerivePlace(place);
        int? classicId;
        if (derived.Confident)
        {
            classicId = store.EnsurePlace(derived.Levels, null, null);
        }
        else
        {
            var notice = derived.SpecificName is null
                ? $"{role}: la gerarchia di «{place.CurrentName}» è incompleta o incerta. Verificala prima di salvare."
                : $"{role}: «{place.CurrentName}» non è un’unità amministrativa e sarà salvato come luogo specifico. Verifica il luogo che lo contiene.";
            var editor = new PlacesEditorWindow(derived.Levels, notice: notice, store: store) { Owner = this };
            classicId = editor.ShowDialog() == true
                ? store.EnsurePlace(editor.ResultLevels, editor.ResultLatitude, editor.ResultLongitude)
                : null;
        }

        if (classicId is not int luogoId)
        {
            return (null, null);
        }

        return derived.SpecificName is { Length: > 0 } specificName
            ? (null, store.EnsureSpecificPlace(specificName, luogoId, place.WikidataId, place.Latitudine, place.Longitudine))
            : (luogoId, null);
    }

    private List<HistoricalDateEvidence> BuildDateEvidence(string dateText, string sourceText, IEnumerable<HistoricalDateEvidence>? importedEvidence)
    {
        var values = SplitUserValues(dateText);
        var sources = SplitUserValues(sourceText);
        var evidence = importedEvidence?
            .Where(item => values.Contains(item.Value, StringComparer.OrdinalIgnoreCase))
            .Select(CloneEvidence)
            .ToList() ?? [];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (evidence.Any(item => string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var citation = index < sources.Count
                ? new SourceCitation { Title = "Fonte della data", Url = sources[index] }
                : CreateInputCitation();
            evidence.Add(new HistoricalDateEvidence
            {
                Value = value,
                RawValue = value,
                Precision = "testo libero",
                Sources = citation is null ? [] : [CloneCitation(citation)],
            });
        }

        return evidence;
    }

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

    private static List<string> SplitUserValues(string value) =>
        value.Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private SourceCitation? CreateInputCitation()
    {
        var url = sourceUrl.Trim();
        return url.Length == 0 ? null : new SourceCitation { Title = "Fonte inserita", Url = url };
    }

    private static HistoricalDateEvidence CloneEvidence(HistoricalDateEvidence source) => new()
    {
        Value = source.Value,
        RawValue = source.RawValue,
        Precision = source.Precision,
        Rank = source.Rank,
        Qualifiers = source.Qualifiers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.Ordinal),
        Sources = source.Sources.Select(CloneCitation).ToList(),
    };

    private static HistoricalPlaceRecord ClonePlace(HistoricalPlaceRecord source) => new()
    {
        WikidataId = source.WikidataId,
        CurrentName = source.CurrentName,
        CurrentCountry = source.CurrentCountry,
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

    private static string FormatDateInput(IEnumerable<HistoricalDateEvidence> evidence) =>
        string.Join("; ", evidence.Select(item => item.Value).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string JoinEvidenceSources(IEnumerable<HistoricalDateEvidence> evidence) =>
        string.Join("; ", evidence
            .SelectMany(item => item.Sources)
            .Select(source => source.Url)
            .Where(url => url.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
}
