using System.Windows;

namespace HistoricEditor;

// Trasforma un luogo letto da Wikidata in un luogo dell'archivio. Se la gerarchia è incerta la propone
// nell'editor dei luoghi; i luoghi non amministrativi (castelli, monasteri, campi di battaglia…) diventano luoghi specifici,
// le località antiche o scomparse (Falacrine, Aquae Cutiliae) luoghi storici contenuti nel comune attuale.
internal static class ImportedPlaceResolver
{
    public static (int? LuogoId, int? SpecificoId) Resolve(PeopleStore store, HistoricalPlaceRecord place, string role, Window owner)
    {
        var derived = store.DerivePlace(place);
        int? classicId;
        if (derived.Confident)
        {
            classicId = store.EnsurePlace(derived.Levels, null, null);
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
            classicId = editor.ShowDialog() == true
                ? store.EnsurePlace(editor.ResultLevels, editor.ResultLatitude, editor.ResultLongitude)
                : null;
        }

        if (classicId is not int luogoId)
        {
            return (null, null);
        }

        return ToArchiveIds(store, place, derived, luogoId);
    }

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
            var tipo = place.TypeLabel.Length > 0 ? place.TypeLabel : SpecificPlaceTypes.Describe(place.TypeIds);
            var historicId = store.EnsureHistoricPlace(historicName, derived.LatinName, tipo, luogoId, place.WikidataId, place.Latitudine, place.Longitudine,
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
