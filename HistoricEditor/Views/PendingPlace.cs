using System.Windows;

namespace HistoricEditor;

// Luogo letto da un import e tenuto in memoria (2026-11-15): si scrive nell'archivio solo quando si salva la scheda,
// l'evento o l'opera (ImportedPlaceResolver.Create), così annullando l'import non resta nulla. Contiene anche quanto
// confermato nella finestra dei luoghi (livelli e coordinate) e le correzioni fatte prima del salvataggio: i livelli
// rivisti dal selettore e il luogo specifico scritto, corretto o tolto nella scheda.
public sealed class PendingPlace
{
    internal PendingPlace(HistoricalPlaceRecord place, DerivedPlace derived, IReadOnlyList<GeoLevel> levels, double? latitude, double? longitude)
    {
        Place = place;
        Derived = derived;
        Levels = levels;
        Latitude = latitude;
        Longitude = longitude;
    }

    internal HistoricalPlaceRecord Place { get; }

    internal DerivedPlace Derived { get; }

    internal IReadOnlyList<GeoLevel> Levels { get; private set; }

    internal double? Latitude { get; private set; }

    internal double? Longitude { get; private set; }

    // Luogo specifico scritto o corretto prima del salvataggio (Id e LuogoId a 0): sostituisce quello letto dall'import.
    internal LuogoSpecifico? SpecificDraft { get; private set; }

    // Il luogo specifico letto dall'import è stato tolto: resta solo il luogo che lo conteneva.
    internal bool SpecificRemoved { get; private set; }

    internal bool IsHistoric => Derived.HistoricName is { Length: > 0 };

    internal void SetLevels(IReadOnlyList<GeoLevel> levels, double? latitude, double? longitude)
    {
        Levels = levels;
        Latitude = latitude;
        Longitude = longitude;
    }

    // Null toglie il luogo specifico.
    internal void SetSpecific(LuogoSpecifico? draft)
    {
        SpecificDraft = draft;
        SpecificRemoved = draft is null;
    }

    // Parti del luogo dal più piccolo allo Stato, come nelle schede salvate: «Regio V Picenum, Italia», con il luogo
    // storico segnato.
    public IReadOnlyList<(string Nome, bool Storico)> Parts
    {
        get
        {
            var parts = new List<(string Nome, bool Storico)>();
            if (Derived.HistoricName is { Length: > 0 } historic)
            {
                parts.Add((historic, true));
            }

            parts.AddRange(Levels.Where(l => !l.Assente && l.Nome.Trim().Length > 0).Reverse().Select(l => (l.Nome.Trim(), false)));
            return parts;
        }
    }

    // Luogo specifico letto dall'import: un luogo non amministrativo (castello, monastero…). Per un luogo storico nessuno:
    // i suoi resti si creano a parte, nel luogo attuale (2026-11-16).
    internal string ImportedSpecific => IsHistoric ? string.Empty : Derived.SpecificName?.Trim() ?? string.Empty;

    internal string ImportedSpecificType => IsHistoric
        ? Place.HistoricBuildingType
        : Place.TypeLabel.Length > 0 ? Place.TypeLabel : SpecificPlaceTypes.Describe(Place.TypeIds);

    public string Specific => SpecificRemoved ? string.Empty : SpecificDraft?.Nome.Trim() ?? ImportedSpecific;

    internal string SpecificType => SpecificDraft?.Tipo.Trim() ?? (Specific.Length > 0 ? ImportedSpecificType : string.Empty);

    internal string SpecificWikidataId => SpecificDraft?.WikidataId.Trim() ?? Place.WikidataId;

    internal string SpecificCondition => SpecificDraft?.Condizione ?? Place.SpecificCondition;

    public PlaceDescription Description => new(string.Join(", ", Parts.Select(p => p.Nome)), Specific);

    // Per il confronto delle modifiche non salvate: cambia con ogni correzione, anche delle sole coordinate.
    internal string ChangeKey => string.Join("|",
        Description.DisplayText,
        string.Join(";", Levels.Select(l => $"{l.Tipo}={l.Nome}{(l.Assente ? "-" : string.Empty)}")),
        Latitude, Longitude,
        SpecificDraft is { } d ? $"{d.Tipo}|{d.Indirizzo}|{d.Condizione}|{d.DataDistruzione}|{d.Latitudine}|{d.Longitudine}|{d.Note}|{d.WikidataId}" : string.Empty);

    // Proposta per la finestra del luogo specifico: la correzione già fatta o i dati letti dall'import.
    internal LuogoSpecifico SpecificProposal()
    {
        if (SpecificDraft is { } draft)
        {
            return Copy(draft);
        }

        var name = SpecificRemoved ? string.Empty : ImportedSpecific;
        return name.Length == 0
            ? new LuogoSpecifico()
            : new LuogoSpecifico
            {
                Nome = name,
                Tipo = ImportedSpecificType,
                Condizione = CondizioniLuogo.Tutte.Contains(Place.SpecificCondition) ? Place.SpecificCondition : CondizioniLuogo.Esistente,
                Latitudine = Place.Latitudine,
                Longitudine = Place.Longitudine,
                WikidataId = Place.WikidataId,
            };
    }

    private static LuogoSpecifico Copy(LuogoSpecifico s) => new()
    {
        Nome = s.Nome,
        Tipo = s.Tipo,
        Indirizzo = s.Indirizzo,
        Condizione = s.Condizione,
        DataDistruzione = s.DataDistruzione,
        Latitudine = s.Latitudine,
        Longitudine = s.Longitudine,
        Note = s.Note,
        WikidataId = s.WikidataId,
    };
}

// Correzioni di un luogo in attesa, dalla scheda o dal selettore dei luoghi: non scrivono nell'archivio.
internal static class PendingPlaceEditing
{
    // Riapre la finestra dei livelli (la stessa della conferma dell'import) per cambiare dove sta il luogo nuovo.
    public static bool EditLevels(PeopleStore store, PendingPlace pending, Window owner)
    {
        var notice = pending.Derived.HistoricName is { Length: > 0 } historic
            ? $"«{historic}» non esiste più e sarà salvato come luogo storico. Indicare il luogo attuale che lo contiene: il comune, oppure la provincia, la regione o soltanto lo Stato (i livelli più bassi si possono lasciare vuoti). Il luogo sarà creato al salvataggio."
            : $"Nuovo luogo «{pending.Description.Classic}»: correggere i livelli. Il luogo sarà creato al salvataggio.";
        var editor = new PlacesEditorWindow(pending.Levels, pending.Latitude, pending.Longitude, notice, store, allowPartial: pending.IsHistoric) { Owner = owner };
        if (editor.ShowDialog() != true)
        {
            return false;
        }

        pending.SetLevels(editor.ResultLevels, editor.ResultLatitude, editor.ResultLongitude);
        return true;
    }

    // Aggiunge o corregge il luogo specifico, che resta in memoria e si crea dentro il luogo nuovo al salvataggio.
    public static bool EditSpecific(PeopleStore store, PendingPlace pending, Window owner)
    {
        var editor = new SpecificPlaceEditorWindow(store, pending.SpecificProposal(), pending.Description.Classic) { Owner = owner };
        if (editor.ShowDialog() != true || editor.ResultDraft is not { } draft)
        {
            return false;
        }

        pending.SetSpecific(draft);
        return true;
    }
}
