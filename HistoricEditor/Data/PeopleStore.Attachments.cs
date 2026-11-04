using System.IO;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

// Allegati (ritratti, documenti, immagini) delle schede: i file sono copiati nella cartella «Allegati»,
// le immagini compresse come quelle delle opere.
public sealed partial class PeopleStore
{
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".jfif"];

    public string AttachmentsDirectory => Path.Combine(baseDir, "Allegati");

    [GeneratedRegex("^[0-9a-f]{32}(\\.[A-Za-z0-9]{1,10})?$")]
    private static partial Regex OwnAttachmentName();

    public IReadOnlyList<Allegato> GetAttachments(string ambito, int schedaId)
        => [.. context.Allegati.AsNoTracking()
            .Where(a => a.Ambito == ambito && a.SchedaId == schedaId)
            .OrderBy(a => a.Ordine).ThenBy(a => a.Id)];

    public Allegato? GetAttachment(int id) => context.Allegati.AsNoTracking().FirstOrDefault(a => a.Id == id);

    public Allegato? GetPortrait(int personId)
        => context.Allegati.AsNoTracking().FirstOrDefault(a => a.Ambito == AmbitiAllegato.Persona && a.SchedaId == personId && a.Ritratto);

    public string AttachmentPath(Allegato allegato) => Path.Combine(AttachmentsDirectory, allegato.File);

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    // Copia il file nell'archivio. La prima immagine di una persona diventa il suo ritratto.
    public Allegato AddAttachment(string ambito, int schedaId, string sourcePath, string titolo = "")
    {
        if (!AmbitiAllegato.IsValid(ambito))
        {
            throw new InvalidOperationException($"Tipo di scheda non valido: «{ambito}».");
        }

        if (!RecordExists(ambito, schedaId))
        {
            throw new InvalidOperationException("La scheda non è più in archivio: salvala prima di aggiungere allegati.");
        }

        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException($"File non trovato: {sourcePath}");
        }

        var isImage = IsImageFile(sourcePath);
        var file = isImage ? ArtworkImages.Import(sourcePath, AttachmentsDirectory) : CopyDocument(sourcePath);
        try
        {
            var existing = context.Allegati.Where(a => a.Ambito == ambito && a.SchedaId == schedaId);
            var allegato = new Allegato
            {
                Ambito = ambito,
                SchedaId = schedaId,
                Titolo = titolo.Trim().Length > 0 ? titolo.Trim() : Path.GetFileNameWithoutExtension(sourcePath),
                File = file,
                NomeOriginale = Path.GetFileName(sourcePath),
                Immagine = isImage,
                Ritratto = isImage && ambito == AmbitiAllegato.Persona && !existing.Any(a => a.Ritratto),
                Ordine = existing.Any() ? existing.Max(a => a.Ordine) + 1 : 0,
                Aggiunto = DateTime.Now,
            };
            context.Allegati.Add(allegato);
            SaveOrRevert();
            return allegato;
        }
        catch
        {
            DeleteAttachmentFile(file);
            throw;
        }
    }

    public void UpdateAttachment(int id, string titolo, string descrizione, string data)
    {
        if (context.Allegati.Find(id) is not { } allegato)
        {
            return;
        }

        allegato.Titolo = titolo.Trim();
        allegato.Descrizione = descrizione.Trim();
        allegato.Data = data.Trim();
        SaveOrRevert();
    }

    // Sceglie l'immagine come ritratto della persona (togliendo il precedente); di nuovo sulla stessa lo toglie.
    public void TogglePortrait(int id)
    {
        if (context.Allegati.Find(id) is not { } allegato)
        {
            return;
        }

        if (allegato.Ambito != AmbitiAllegato.Persona || !allegato.Immagine)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("Solo un’immagine allegata a un personaggio può essere il ritratto.");
        }

        var value = !allegato.Ritratto;
        foreach (var other in context.Allegati.Where(a => a.Ambito == AmbitiAllegato.Persona && a.SchedaId == allegato.SchedaId && a.Ritratto))
        {
            other.Ritratto = false;
        }

        allegato.Ritratto = value;
        SaveOrRevert();
    }

    // Sposta l'allegato di una posizione (-1 su, +1 giù) nell'elenco della scheda.
    public void MoveAttachment(int id, int direction)
    {
        if (context.Allegati.Find(id) is not { } allegato)
        {
            return;
        }

        var list = context.Allegati.Where(a => a.Ambito == allegato.Ambito && a.SchedaId == allegato.SchedaId)
            .OrderBy(a => a.Ordine).ThenBy(a => a.Id).ToList();
        var index = list.IndexOf(allegato);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= list.Count)
        {
            context.ChangeTracker.Clear();
            return;
        }

        (list[index], list[target]) = (list[target], list[index]);
        for (var i = 0; i < list.Count; i++)
        {
            list[i].Ordine = i;
        }

        SaveOrRevert();
    }

    public void DeleteAttachment(int id)
    {
        if (context.Allegati.Find(id) is not { } allegato)
        {
            return;
        }

        var file = allegato.File;
        context.Allegati.Remove(allegato);
        SaveOrRevert();
        DeleteAttachmentFile(file);
    }

    // Rimuove (senza salvare) gli allegati delle schede eliminate; i file vanno cancellati dopo il salvataggio.
    private List<string> RemoveAttachmentRows(string ambito, IReadOnlyCollection<int> ids)
    {
        var rows = context.Allegati.Where(a => a.Ambito == ambito && ids.Contains(a.SchedaId)).ToList();
        context.Allegati.RemoveRange(rows);
        return [.. rows.Select(a => a.File)];
    }

    private void DeleteAttachmentFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            DeleteAttachmentFile(file);
        }
    }

    private void DeleteAttachmentFile(string? file)
    {
        if (string.IsNullOrWhiteSpace(file) || !OwnAttachmentName().IsMatch(file))
        {
            return;
        }

        if (file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            ArtworkImages.Delete(AttachmentsDirectory, file);
        }

        try
        {
            File.Delete(Path.Combine(AttachmentsDirectory, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File aperto in un altro programma: resta nella cartella.
        }
    }

    private string CopyDocument(string sourcePath)
    {
        Directory.CreateDirectory(AttachmentsDirectory);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!Regex.IsMatch(extension, "^\\.[a-z0-9]{1,10}$"))
        {
            extension = string.Empty;
        }

        var name = $"{Guid.NewGuid():N}{extension}";
        File.Copy(sourcePath, Path.Combine(AttachmentsDirectory, name));
        return name;
    }

    private bool RecordExists(string ambito, int id) => ambito switch
    {
        AmbitiAllegato.Persona => context.People.Any(p => p.Id == id),
        AmbitiAllegato.Evento => context.Eventi.Any(e => e.Id == id),
        AmbitiAllegato.Opera => context.Opere.Any(o => o.Id == id),
        AmbitiAllegato.Luogo => context.Luoghi.Any(l => l.Id == id),
        AmbitiAllegato.Dinastia => context.Dinastie.Any(d => d.Id == id),
        _ => false,
    };
}
