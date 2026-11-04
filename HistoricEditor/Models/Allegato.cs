namespace HistoricEditor;

// Schede a cui si può allegare un file.
public static class AmbitiAllegato
{
    public const string Persona = "Persona";
    public const string Evento = "Evento";
    public const string Opera = "Opera";
    public const string Luogo = "Luogo";
    public const string Dinastia = "Dinastia";

    public static readonly IReadOnlyList<string> Tutti = [Persona, Evento, Opera, Luogo, Dinastia];

    public static bool IsValid(string ambito) => Tutti.Contains(ambito);
}

// File copiato nell'archivio (cartella «Allegati»); le immagini sono salvate compresse.
public sealed class Allegato
{
    public int Id { get; set; }
    public string Ambito { get; set; } = string.Empty;
    public int SchedaId { get; set; }
    public string Titolo { get; set; } = string.Empty;
    public string Descrizione { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;

    // Nome del file nella cartella degli allegati.
    public string File { get; set; } = string.Empty;
    public string NomeOriginale { get; set; } = string.Empty;
    public bool Immagine { get; set; }

    // Immagine mostrata come ritratto nella scheda della persona (al massimo una).
    public bool Ritratto { get; set; }
    public int Ordine { get; set; }
    public DateTime Aggiunto { get; set; } = DateTime.Now;
}
