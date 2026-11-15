namespace HistoricEditor;

// Luogo letto da un import e tenuto in memoria (2026-11-15): si scrive nell'archivio solo quando si salva la scheda,
// l'evento o l'opera (ImportedPlaceResolver.Create), così annullando l'import non resta nulla. Contiene anche quanto
// confermato nella finestra dei luoghi (livelli e coordinate).
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

    internal IReadOnlyList<GeoLevel> Levels { get; }

    internal double? Latitude { get; }

    internal double? Longitude { get; }

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

    // Luogo specifico: l'edificio che resta di una località antica o un luogo non amministrativo (castello, monastero…).
    public string Specific => Place.HistoricBuildingName.Trim() is { Length: > 0 } building && Derived.HistoricName is { Length: > 0 }
        ? building
        : Derived.HistoricName is { Length: > 0 } ? string.Empty : Derived.SpecificName?.Trim() ?? string.Empty;

    public PlaceDescription Description => new(string.Join(", ", Parts.Select(p => p.Nome)), Specific);
}
