using System.Windows;

namespace HistoricEditor;

// Trasforma un luogo letto da Wikidata in un luogo dell'archivio. Se la gerarchia è incerta la propone
// nell'editor dei luoghi; i luoghi non amministrativi (castelli, monasteri, campi di battaglia…) diventano luoghi specifici,
// le località antiche o scomparse (Falacrine, Aquae Cutiliae) luoghi storici contenuti nel comune attuale.
// Nelle schede, negli eventi e nelle opere il luogo importato resta in memoria (PendingPlace) e si crea solo al salvataggio
// (2026-11-15): annullando l'import non resta nulla nell'archivio.
internal static class ImportedPlaceResolver
{
    // Prepara il luogo senza scrivere nell'archivio; se la gerarchia è incerta la fa confermare. Null se l'utente annulla.
    public static PendingPlace? Prepare(PeopleStore store, HistoricalPlaceRecord place, string role, Window owner)
    {
        var derived = store.DerivePlace(place);
        if (derived.Confident)
        {
            return new PendingPlace(place, derived, derived.Levels, null, null);
        }
        else
        {
            var notice = derived.HistoricName is { Length: > 0 } historic
                ? place.HistoricBuildingName.Trim() is { Length: > 0 } building
                    ? $"{role}: «{historic}» non esiste più e sarà salvato come luogo storico, con «{building}» (ancora visibile) come luogo specifico al suo interno. Verificare il luogo che lo contiene."
                    : $"{role}: «{historic}» non esiste più e sarà salvato come luogo storico. Verificare il luogo attuale che lo contiene: il comune, oppure la provincia, la regione o soltanto lo Stato se non è noto un luogo più preciso (i livelli più bassi si possono lasciare vuoti)."
                : derived.SpecificName is null
                    ? $"{role}: la gerarchia di «{place.CurrentName}» è incompleta o incerta. Verificarla prima di salvare."
                    : $"{role}: «{place.CurrentName}» non è un’unità amministrativa e sarà salvato come luogo specifico. Verificare il luogo che lo contiene.";
            var editor = new PlacesEditorWindow(derived.Levels, notice: notice, store: store, allowPartial: derived.HistoricName is { Length: > 0 }) { Owner = owner };
            return editor.ShowDialog() == true
                ? new PendingPlace(place, derived, editor.ResultLevels, editor.ResultLatitude, editor.ResultLongitude)
                : null;
        }
    }

    // Crea nell'archivio il luogo preparato (o riusa quello che esiste già), al salvataggio.
    public static (int? LuogoId, int? SpecificoId) Create(PeopleStore store, PendingPlace pending)
        => store.EnsurePlace(pending.Levels, pending.Latitude, pending.Longitude) is int luogoId
            ? ToArchiveIds(store, pending.Place, pending.Derived, luogoId, pending)
            : (null, null);

    // Senza scrivere: il luogo che il salvataggio userebbe, se esiste già tutto, e il luogo esistente più vicino della
    // catena (per aprire lì il selettore).
    public static (int? LuogoId, int? SpecificoId, int? Nearest) FindExisting(PeopleStore store, PendingPlace pending)
    {
        var steps = Steps(store, pending);
        int? nearest = null;
        foreach (var step in steps)
        {
            if (step.ExistingId is not int id)
            {
                return (null, null, nearest);
            }

            if (step.Kind != PendingStepKind.Specific)
            {
                nearest = id;
            }
        }

        return steps.Count == 0 ? (null, null, null)
            : steps[^1].Kind == PendingStepKind.Specific ? (null, steps[^1].ExistingId, nearest)
            : (steps[^1].ExistingId, null, nearest);
    }

    // Le parti del luogo in attesa dallo Stato al luogo specifico, ciascuna con il luogo dell'archivio che il salvataggio
    // userebbe (null dalla prima che manca in poi): il selettore mostra quelle mancanti come luoghi nuovi.
    public static IReadOnlyList<PendingStep> Steps(PeopleStore store, PendingPlace pending)
    {
        var steps = new List<PendingStep>();
        int? parentId = null;
        var found = true;
        foreach (var level in pending.Levels)
        {
            if (level.Assente || level.Nome.Trim().Length == 0)
            {
                continue;
            }

            var id = found ? store.FindPlace(parentId, level.Tipo, level.Nome) : null;
            found = id is not null;
            parentId = id;
            steps.Add(new PendingStep(level.Nome.Trim(), level.Tipo, PendingStepKind.Level, id));
        }

        var place = pending.Place;
        if (pending.Derived.HistoricName is { Length: > 0 } historicName)
        {
            var id = found && parentId is int luogoId ? store.FindHistoricPlace(historicName, HistoricType(place), luogoId, place.WikidataId) : null;
            found = id is not null;
            parentId = id;
            steps.Add(new PendingStep(historicName, HistoricType(place), PendingStepKind.Historic, id));
        }

        if (pending.Specific is { Length: > 0 } specificName)
        {
            var id = found && parentId is int containerId ? store.FindSpecificPlace(specificName, containerId, pending.SpecificWikidataId) : null;
            steps.Add(new PendingStep(specificName, pending.SpecificType, PendingStepKind.Specific, id, pending.SpecificCondition == CondizioniLuogo.Distrutto));
        }

        return steps;
    }

    private static string HistoricType(HistoricalPlaceRecord place)
        => place.TypeLabel.Length > 0 ? place.TypeLabel : SpecificPlaceTypes.Describe(place.TypeIds);

    // Senza finestre: il luogo si crea solo se la gerarchia è certa, altrimenti resta da scegliere nella scheda.
    public static (int? LuogoId, int? SpecificoId) ResolveIfConfident(PeopleStore store, HistoricalPlaceRecord place)
    {
        var derived = store.DerivePlace(place);
        return derived.Confident && store.EnsurePlace(derived.Levels, null, null) is int luogoId
            ? ToArchiveIds(store, place, derived, luogoId)
            : (null, null);
    }

    // Con un luogo in attesa vale il luogo specifico scritto o tolto nella scheda al posto di quello letto dall'import.
    private static (int? LuogoId, int? SpecificoId) ToArchiveIds(PeopleStore store, HistoricalPlaceRecord place, DerivedPlace derived, int luogoId, PendingPlace? pending = null)
    {
        var containerId = luogoId;
        var specificName = string.Empty;
        var specificType = string.Empty;
        if (derived.HistoricName is { Length: > 0 } historicName)
        {
            containerId = store.EnsureHistoricPlace(historicName, derived.LatinName, HistoricType(place), luogoId, place.WikidataId, place.Latitudine, place.Longitudine,
                formerName: place.HistoricBuildingName);
            store.MergePlaceHistoricNames(containerId, place.HistoricalNames, place.WikidataId);
            // L'edificio che dà il nome alla località (Terme di Cotilia) è un luogo specifico dentro il luogo storico.
            (specificName, specificType) = (place.HistoricBuildingName.Trim(), place.HistoricBuildingType);
        }
        else if (derived.SpecificName is { Length: > 0 } name)
        {
            (specificName, specificType) = (name.Trim(), place.TypeLabel.Length > 0 ? place.TypeLabel : SpecificPlaceTypes.Describe(place.TypeIds));
        }
        else if (store.GetPlace(luogoId) is { } luogo && HistoricalPlaceNames.SameName(luogo.Nome, place.CurrentName))
        {
            // I nomi storici passano al luogo solo se è proprio quello importato (non uno scelto al suo posto nell'editor).
            store.MergePlaceHistoricNames(luogoId, place.HistoricalNames, place.WikidataId);
        }

        if (pending?.SpecificRemoved == true)
        {
            return (containerId, null);
        }

        if (pending?.SpecificDraft is { } draft)
        {
            // Un luogo specifico con lo stesso nome (o lo stesso codice Wikidata) che c'è già si riusa così com'è.
            return (null, store.FindSpecificPlace(draft.Nome, containerId, draft.WikidataId.Trim())
                ?? store.SaveSpecificPlace(new LuogoSpecifico
                {
                    Nome = draft.Nome,
                    Tipo = draft.Tipo,
                    LuogoId = containerId,
                    Indirizzo = draft.Indirizzo,
                    Condizione = draft.Condizione,
                    DataDistruzione = draft.DataDistruzione,
                    Latitudine = draft.Latitudine,
                    Longitudine = draft.Longitudine,
                    Note = draft.Note,
                    WikidataId = draft.WikidataId,
                }));
        }

        return specificName.Length > 0
            ? (null, store.EnsureSpecificPlace(specificName, containerId, place.WikidataId, place.Latitudine, place.Longitudine, specificType, place.SpecificCondition))
            : (containerId, null);
    }
}

internal enum PendingStepKind
{
    Level,
    Historic,
    Specific,
}

// Una parte di un luogo in attesa: un livello amministrativo, il luogo storico o il luogo specifico.
internal sealed record PendingStep(string Nome, string Tipo, PendingStepKind Kind, int? ExistingId, bool Distrutto = false);
