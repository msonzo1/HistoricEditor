using System.Windows;

namespace HistoricEditor;

// Trasforma un luogo letto da Wikidata in un luogo dell'archivio. Se la gerarchia è incerta la propone
// nell'editor dei luoghi; i luoghi non amministrativi (castelli, monasteri, campi di battaglia…) diventano luoghi specifici.
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
            var notice = derived.SpecificName is null
                ? $"{role}: la gerarchia di «{place.CurrentName}» è incompleta o incerta. Verificala prima di salvare."
                : $"{role}: «{place.CurrentName}» non è un’unità amministrativa e sarà salvato come luogo specifico. Verifica il luogo che lo contiene.";
            var editor = new PlacesEditorWindow(derived.Levels, notice: notice, store: store) { Owner = owner };
            classicId = editor.ShowDialog() == true
                ? store.EnsurePlace(editor.ResultLevels, editor.ResultLatitude, editor.ResultLongitude)
                : null;
        }

        if (classicId is not int luogoId)
        {
            return (null, null);
        }

        return ToArchiveIds(store, place, derived.SpecificName, luogoId);
    }

    // Senza finestre: il luogo si crea solo se la gerarchia è certa, altrimenti resta da scegliere nella scheda.
    public static (int? LuogoId, int? SpecificoId) ResolveIfConfident(PeopleStore store, HistoricalPlaceRecord place)
    {
        var derived = store.DerivePlace(place);
        return derived.Confident && store.EnsurePlace(derived.Levels, null, null) is int luogoId
            ? ToArchiveIds(store, place, derived.SpecificName, luogoId)
            : (null, null);
    }

    private static (int? LuogoId, int? SpecificoId) ToArchiveIds(PeopleStore store, HistoricalPlaceRecord place, string? specificName, int luogoId)
    {
        if (specificName is { Length: > 0 })
        {
            return (null, store.EnsureSpecificPlace(specificName, luogoId, place.WikidataId, place.Latitudine, place.Longitudine,
                place.TypeLabel.Length > 0 ? place.TypeLabel : SpecificPlaceTypes.Describe(place.TypeIds)));
        }

        // I nomi storici passano al luogo solo se è proprio quello importato (non uno scelto al suo posto nell'editor).
        if (store.GetPlace(luogoId) is { } luogo && HistoricalPlaceNames.SameName(luogo.Nome, place.CurrentName))
        {
            store.MergePlaceHistoricNames(luogoId, place.HistoricalNames, place.WikidataId);
        }

        return (luogoId, null);
    }
}
