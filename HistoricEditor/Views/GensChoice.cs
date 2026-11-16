namespace HistoricEditor;

// Gens letta da un import e tenuta in memoria fino al salvataggio della scheda (2026-11-15): si vede e si corregge nella
// finestra «Gentes e rami» prima di essere creata. Removed: tolta dalla finestra, la scheda resta senza.
public sealed class PendingGens(ImportedGens data)
{
    public ImportedGens Data { get; set; } = data;

    public bool Removed { get; set; }
}

// Gens (ed eventuale ramo) scelta per una scheda: dell'archivio, per identificativo, oppure letta dall'import e da creare al
// salvataggio (Pending).
public sealed record GensChoice(int? GensId, int? RamoId, PendingGens? Pending)
{
    public static readonly GensChoice None = new(null, null, null);

    public static GensChoice FromImport(ImportedGens? imported)
        => imported is null || imported.Nome.Trim().Length == 0 ? None : new(null, null, new PendingGens(imported));

    public bool IsEmpty => GensId is null && (Pending is null || Pending.Removed);

    // Per il confronto delle modifiche non salvate.
    public string Key => Pending is { Removed: false } p ? $"import:{p.Data}" : $"{GensId}|{RamoId}";
}

internal static class GensChoices
{
    // Percorso con il latino («Gens Giulia (Gens Iulia) › Giulii Cesari (Iulii Caesares)») e nota: gens o ramo nuovi, da
    // creare al salvataggio, oppure forme da completare.
    public static (string Path, string Note, bool Missing) Describe(PeopleStore store, IReadOnlyList<GensRomana> gentes, GensChoice choice)
    {
        if (choice.IsEmpty)
        {
            return (string.Empty, string.Empty, false);
        }

        GensRomana? gens;
        RamoGens? ramo;
        var note = string.Empty;
        if (choice.Pending is { Removed: false } pending)
        {
            var (found, foundRamo) = store.FindImportedGens(pending.Data);
            if (found is not null && (pending.Data.Ramo is null || foundRamo is not null))
            {
                (gens, ramo) = (found, foundRamo);
            }
            else
            {
                (gens, ramo) = pending.Data.ToEntities();
                note = found is null ? "Nuova gens, sarà creata al salvataggio della scheda." : "Nuovo ramo, sarà creato al salvataggio della scheda.";
                return (GensLabels.PathWithLatin(gens, ramo), Join(note, Missing(gens)), Missing(gens).Length > 0);
            }
        }
        else
        {
            gens = gentes.FirstOrDefault(g => g.Id == choice.GensId);
            ramo = gens?.Rami.FirstOrDefault(r => r.Id == choice.RamoId);
            if (ramo is not null)
            {
                ramo.Gens ??= gens;
            }
        }

        if (gens is null)
        {
            return (string.Empty, string.Empty, false);
        }

        var missing = Missing(gens);
        return (GensLabels.PathWithLatin(gens, ramo), Join(note, missing), missing.Length > 0);
    }

    private static string Missing(GensRomana gens)
        => gens.DaCompletare is { Length: > 0 } fields ? $"Da completare in «Gentes e rami» ({gens.EtichettaLatina}): {fields}." : string.Empty;

    private static string Join(params string[] parts) => string.Join("\n", parts.Where(p => p.Length > 0));

    // Identificativi da salvare sulla scheda: la gens in attesa si crea (o si ritrova) adesso.
    public static (int? GensId, int? RamoId) Resolve(PeopleStore store, GensChoice choice)
    {
        if (choice.Pending is { } pending)
        {
            if (pending.Removed)
            {
                return (null, null);
            }

            var (gensId, ramoId) = store.EnsureImportedGens(pending.Data);
            return (gensId, ramoId);
        }

        return (choice.GensId, choice.RamoId);
    }
}
