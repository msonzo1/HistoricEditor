using System.IO;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Opere d'arte e relative immagini compresse.
public sealed partial class PeopleStore
{
    // Cartella delle immagini compresse delle opere, accanto all'archivio.
    public string ImagesDirectory => Path.Combine(baseDir, "Immagini");

    public IReadOnlyList<Opera> GetArtworks()
        => [.. context.Opere.AsNoTracking().Include(o => o.Persone).AsEnumerable()
            .OrderBy(o => o.Titolo, StringComparer.CurrentCultureIgnoreCase)];

    public Opera? GetArtwork(int id)
        => context.Opere.AsNoTracking().Include(o => o.Persone).FirstOrDefault(o => o.Id == id);

    public IReadOnlyList<Opera> GetArtworksForPerson(int personId)
        => [.. GetArtworks().Where(o => o.Persone.Any(p => p.PersonaId == personId))];

    public IReadOnlyList<string> GetArtworkTypes()
        => [.. context.Opere.AsNoTracking().Select(o => o.Tipo).Distinct().AsEnumerable()
            .Concat(OperaRuoli.TipiSuggeriti)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)];

    // Crea o aggiorna un'opera sostituendo le persone collegate; se l'immagine cambia, quella precedente viene eliminata.
    public int SaveArtwork(Opera data)
    {
        var titolo = (data.Titolo ?? string.Empty).Trim();
        if (titolo.Length == 0)
        {
            throw new InvalidOperationException("Il titolo dell’opera è obbligatorio.");
        }

        if (data.Persone.FirstOrDefault(p => !OperaRuoli.IsValid(p.Ruolo)) is { } invalid)
        {
            throw new InvalidOperationException($"Ruolo non valido: «{invalid.Ruolo}».");
        }

        var persone = data.Persone
            .Select(p => new OperaPersona { PersonaId = p.PersonaId, Ruolo = p.Ruolo.Trim() })
            .DistinctBy(p => (p.PersonaId, p.Ruolo))
            .ToList();
        var ids = persone.Select(p => p.PersonaId).Distinct().ToList();
        if (context.People.Count(p => ids.Contains(p.Id)) != ids.Count)
        {
            throw new InvalidOperationException("Una delle persone collegate non è più in archivio.");
        }

        var entity = data.Id == 0
            ? new Opera()
            : context.Opere.Include(o => o.Persone).FirstOrDefault(o => o.Id == data.Id)
                ?? throw new InvalidOperationException("Opera non trovata.");
        var previousImage = entity.Immagine;
        entity.Titolo = titolo;
        entity.Tipo = (data.Tipo ?? string.Empty).Trim();
        entity.Data = (data.Data ?? string.Empty).Trim();
        entity.Tecnica = (data.Tecnica ?? string.Empty).Trim();
        entity.Dimensioni = (data.Dimensioni ?? string.Empty).Trim();
        entity.Note = data.Note ?? string.Empty;
        entity.Fonti = (data.Fonti ?? string.Empty).Trim();
        entity.Immagine = (data.Immagine ?? string.Empty).Trim();
        entity.WikidataId = (data.WikidataId ?? string.Empty).Trim().ToUpperInvariant();
        (entity.LuogoId, entity.LuogoSpecificoId) = ResolveEventPlace(data.LuogoId, data.LuogoSpecificoId, "La collocazione dell’opera non è più in archivio.");

        entity.Persone.RemoveAll(p => !persone.Any(n => n.PersonaId == p.PersonaId && n.Ruolo == p.Ruolo));
        foreach (var persona in persone.Where(n => !entity.Persone.Any(p => p.PersonaId == n.PersonaId && p.Ruolo == n.Ruolo)))
        {
            entity.Persone.Add(persona);
        }

        if (data.Id == 0)
        {
            context.Opere.Add(entity);
        }

        SaveOrRevert();
        if (!string.Equals(previousImage, entity.Immagine, StringComparison.OrdinalIgnoreCase))
        {
            ArtworkImages.Delete(ImagesDirectory, previousImage);
        }

        return entity.Id;
    }

    public void DeleteArtwork(int id)
    {
        if (context.Opere.Find(id) is not { } entity)
        {
            return;
        }

        var image = entity.Immagine;
        var files = RemoveAttachmentRows(AmbitiAllegato.Opera, [id]);
        context.Opere.Remove(entity);
        SaveOrRevert();
        ArtworkImages.Delete(ImagesDirectory, image);
        DeleteAttachmentFiles(files);
    }

    // Elimina le immagini scelte ma mai salvate in un'opera.
    public int RemoveUnusedArtworkImages()
        => ArtworkImages.RemoveOrphans(ImagesDirectory, [.. context.Opere.AsNoTracking().Select(o => o.Immagine)]);
}
