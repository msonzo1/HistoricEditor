namespace HistoricEditor;

public static class GeographySeeder
{
    public static readonly string[] Regioni =
    [
        "Abruzzo", "Basilicata", "Calabria", "Campania", "Emilia-Romagna",
        "Friuli-Venezia Giulia", "Lazio", "Liguria", "Lombardia", "Marche",
        "Molise", "Piemonte", "Puglia", "Sardegna", "Sicilia",
        "Toscana", "Trentino-Alto Adige", "Umbria", "Valle d'Aosta", "Veneto",
    ];

    public static bool IsRegione(string nome)
        => Regioni.Any(r => string.Equals(r, nome, StringComparison.OrdinalIgnoreCase));

    public static void Seed(AppDbContext context)
    {
        var italia = context.Luoghi.FirstOrDefault(l => l.ParentId == null && l.Tipo == "Stato" && l.Nome == "Italia");
        if (italia is null)
        {
            italia = new Luogo { Nome = "Italia", Tipo = "Stato" };
            context.Luoghi.Add(italia);
            context.SaveChanges();
        }

        var existing = context.Luoghi
            .Where(l => l.ParentId == italia.Id && l.Tipo == "Regione")
            .Select(l => l.Nome)
            .ToHashSet();

        var toAdd = Regioni
            .Where(nome => !existing.Contains(nome))
            .Select(nome => new Luogo { Nome = nome, Tipo = "Regione", ParentId = italia.Id })
            .ToList();

        if (toAdd.Count > 0)
        {
            context.Luoghi.AddRange(toAdd);
            context.SaveChanges();
        }
    }
}
