namespace HistoricEditor;

// Resti di un luogo storico (2026-11-16): un luogo specifico che esiste ancora (Sito archeologico di Pelusio, in rovina)
// sta nel luogo attuale che contiene il luogo storico, non dentro di esso. Restano nel luogo storico solo i luoghi
// specifici scomparsi (distrutti).
public sealed partial class PeopleStore
{
    // A ogni caricamento, senza effetti se non c'è niente da fare: i resti salvati prima dentro un luogo storico passano al
    // luogo che lo contiene, con la nota «Resti di …» se non ne hanno una. Le schede e gli eventi che li indicavano passano
    // al luogo storico (il personaggio è nato o morto nella città, non nel sito); le opere restano nel sito, che per loro è
    // la collocazione.
    private void MoveRemainsOutOfHistoricPlaces()
    {
        var historic = context.Luoghi.Where(l => l.Storico && l.ParentId != null).ToDictionary(l => l.Id);
        if (historic.Count == 0)
        {
            return;
        }

        var historicIds = historic.Keys.ToList();
        var remains = context.LuoghiSpecifici
            .Where(s => historicIds.Contains(s.LuogoId) && s.Condizione != CondizioniLuogo.Distrutto)
            .ToList();
        if (remains.Count == 0)
        {
            context.ChangeTracker.Clear();
            return;
        }

        var ids = remains.Select(s => s.Id).ToList();
        var historicOf = remains.ToDictionary(s => s.Id, s => s.LuogoId);
        foreach (var specific in remains)
        {
            var place = historic[specific.LuogoId];
            specific.LuogoId = place.ParentId!.Value;
            if (string.IsNullOrWhiteSpace(specific.Note))
            {
                specific.Note = $"Resti di {place.Nome}.";
            }
        }

        foreach (var person in context.People.Where(p => (p.BirthLuogoSpecificoId != null && ids.Contains(p.BirthLuogoSpecificoId.Value))
            || (p.DeathLuogoSpecificoId != null && ids.Contains(p.DeathLuogoSpecificoId.Value))
            || (p.BurialLuogoSpecificoId != null && ids.Contains(p.BurialLuogoSpecificoId.Value))))
        {
            if (person.BirthLuogoSpecificoId is int birth && historicOf.TryGetValue(birth, out var birthPlace))
            {
                (person.BirthLuogoId, person.BirthLuogoSpecificoId) = (birthPlace, null);
            }

            if (person.DeathLuogoSpecificoId is int death && historicOf.TryGetValue(death, out var deathPlace))
            {
                (person.DeathLuogoId, person.DeathLuogoSpecificoId) = (deathPlace, null);
            }

            if (person.BurialLuogoSpecificoId is int burial && historicOf.TryGetValue(burial, out var burialPlace))
            {
                (person.BurialLuogoId, person.BurialLuogoSpecificoId) = (burialPlace, null);
            }
        }

        foreach (var evento in context.Eventi.Where(e => e.LuogoSpecificoId != null && ids.Contains(e.LuogoSpecificoId.Value)))
        {
            (evento.LuogoId, evento.LuogoSpecificoId) = (historicOf[evento.LuogoSpecificoId!.Value], null);
        }

        context.SaveChanges();
        SyncPlaceTexts();
        SaveOrRevert();
    }
}
