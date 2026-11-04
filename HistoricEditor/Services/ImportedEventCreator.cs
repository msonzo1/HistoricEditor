using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public sealed record EventImportSummary(int Created, int Linked, int Skipped);

// Importa gli eventi indicati da Wikidata su una scheda. Gli altri partecipanti vengono collegati
// solo se hanno già una scheda: non si creano nuove schede.
public static class ImportedEventCreator
{
    public static async Task<EventImportSummary> ImportForPersonAsync(
        WikipediaImportService service,
        PeopleStore store,
        int personId,
        IEnumerable<string> eventIds,
        IProgress<(double Fraction, string Text)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int created = 0, linked = 0, skipped = 0;
        var ids = eventIds.Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var qid = ids[i];
            if (store.FindEventIdByWikidataId(qid) is int existing)
            {
                try
                {
                    linked += store.AddEventParticipant(existing, personId, "partecipante") ? 1 : 0;
                }
                catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
                {
                    skipped++;
                }

                continue;
            }

            progress?.Report(((double)i / ids.Count, $"Evento {i + 1} di {ids.Count}: lettura da Wikipedia e Wikidata…"));
            var import = await service.ImportEventByIdAsync(qid, null, cancellationToken);
            if (import is null)
            {
                skipped++;
                continue;
            }

            if (Save(store, import, personId) is null)
            {
                skipped++;
            }
            else
            {
                created++;
            }
        }

        return new EventImportSummary(created, linked, skipped);
    }

    // Crea l'evento con il personaggio e i partecipanti già in archivio; null se i dati non sono validi.
    public static int? Save(PeopleStore store, WikipediaEventImport import, int personId)
    {
        var evento = BuildEvent(import, store.FindPeopleByWikidataIds(import.Partecipanti.Select(p => p.WikidataId)), personId);
        if (import.Luogo is { } place && !string.IsNullOrWhiteSpace(place.CurrentName))
        {
            try
            {
                (evento.LuogoId, evento.LuogoSpecificoId) = ImportedPlaceResolver.ResolveIfConfident(store, place);
            }
            catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
            {
            }
        }

        try
        {
            return store.SaveEvent(evento);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            return null;
        }
    }

    internal static Evento BuildEvent(WikipediaEventImport import, IReadOnlyDictionary<string, int> archive, int personId)
    {
        var evento = new Evento
        {
            Titolo = import.Titolo,
            Tipo = import.Tipo,
            DataQualificatore = import.DataQualificatore,
            Data1 = import.Data1,
            Data2 = import.Data2,
            Calendario = import.Calendario,
            Stile = HistoricalCalendar.StileComune,
            Descrizione = import.Descrizione,
            WikidataId = import.WikidataId,
        };

        foreach (var participant in import.Partecipanti)
        {
            if (archive.TryGetValue(participant.WikidataId, out var id) && evento.Partecipanti.All(p => p.PersonaId != id))
            {
                evento.Partecipanti.Add(new EventoPartecipante { PersonaId = id, Ruolo = participant.Ruolo });
            }
        }

        if (evento.Partecipanti.All(p => p.PersonaId != personId))
        {
            evento.Partecipanti.Add(new EventoPartecipante { PersonaId = personId, Ruolo = "partecipante" });
        }

        return evento;
    }
}
