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
            ? ToArchiveIds(store, pending.Place, pending.Derived, luogoId)
            : (null, null);

    // Senza scrivere: il luogo che il salvataggio userebbe, se esiste già tutto, e il luogo esistente più vicino della
    // catena (per aprire lì il selettore).
    public static (int? LuogoId, int? SpecificoId, int? Nearest) FindExisting(PeopleStore store, PendingPlace pending)
    {
        int? parentId = null;
        foreach (var level in pending.Levels)
        {
            if (level.Assente || level.Nome.Trim().Length == 0)
            {
                continue;
            }

            if (store.FindPlace(parentId, level.Tipo, level.Nome) is not int id)
            {
                return (null, null, parentId);
            }

            parentId = id;
        }

        if (parentId is not int luogoId)
        {
            return (null, null, null);
        }

        var place = pending.Place;
        if (pending.Derived.HistoricName is { Length: > 0 } historicName)
        {
            if (store.FindHistoricPlace(historicName, HistoricType(place), luogoId, place.WikidataId) is not int historicId)
            {
                return (null, null, luogoId);
            }

            if (place.HistoricBuildingName.Trim() is { Length: > 0 } buildingName)
            {
                return store.FindSpecificPlace(buildingName, historicId, place.WikidataId) is int buildingId ? (null, buildingId, historicId) : (null, null, historicId);
            }

            return (historicId, null, historicId);
        }

        if (pending.Derived.SpecificName is { Length: > 0 } specificName)
        {
            return store.FindSpecificPlace(specificName, luogoId, place.WikidataId) is int specificId ? (null, specificId, luogoId) : (null, null, luogoId);
        }

        return (luogoId, null, luogoId);
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

    private static (int? LuogoId, int? SpecificoId) ToArchiveIds(PeopleStore store, HistoricalPlaceRecord place, DerivedPlace derived, int luogoId)
    {
        if (derived.HistoricName is { Length: > 0 } historicName)
        {
            var historicId = store.EnsureHistoricPlace(historicName, derived.LatinName, HistoricType(place), luogoId, place.WikidataId, place.Latitudine, place.Longitudine,
                formerName: place.HistoricBuildingName);
            store.MergePlaceHistoricNames(historicId, place.HistoricalNames, place.WikidataId);
            // L'edificio che dà il nome alla località (Terme di Cotilia) è un luogo specifico dentro il luogo storico.
            if (place.HistoricBuildingName.Trim() is { Length: > 0 } buildingName)
            {
                return (null, store.EnsureSpecificPlace(buildingName, historicId, place.WikidataId, place.Latitudine, place.Longitudine, place.HistoricBuildingType, place.SpecificCondition));
            }

            return (historicId, null);
        }

        if (derived.SpecificName is { Length: > 0 } specificName)
        {
            return (null, store.EnsureSpecificPlace(specificName, luogoId, place.WikidataId, place.Latitudine, place.Longitudine,
                place.TypeLabel.Length > 0 ? place.TypeLabel : SpecificPlaceTypes.Describe(place.TypeIds), place.SpecificCondition));
        }

        // I nomi storici passano al luogo solo se è proprio quello importato (non uno scelto al suo posto nell'editor).
        if (store.GetPlace(luogoId) is { } luogo && HistoricalPlaceNames.SameName(luogo.Nome, place.CurrentName))
        {
            store.MergePlaceHistoricNames(luogoId, place.HistoricalNames, place.WikidataId);
        }

        return (luogoId, null);
    }
}
