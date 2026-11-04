namespace HistoricEditor;

// Stato dell'import da Wikipedia: passo corrente su quelli totali e avanzamento all'interno del passo (0-1).
public sealed record ImportProgress(int Step, int TotalSteps, string Message, double StepFraction = 0, string Detail = "")
{
    public double Percent => TotalSteps <= 0
        ? 0
        : Math.Clamp((Step - 1 + Math.Clamp(StepFraction, 0, 1)) / TotalSteps * 100, 0, 100);

    public string Text => Detail.Length > 0
        ? $"Passo {Step} di {TotalSteps} · {Message} ({Detail})"
        : $"Passo {Step} di {TotalSteps} · {Message}";
}

// Numera i passi dell'import; con offset > 0 lascia spazio ai passi preliminari (es. la ricerca del reimport).
internal sealed class ImportProgressReporter(IProgress<ImportProgress>? target, int offset = 0, int importSteps = ImportProgressReporter.ImportSteps)
{
    // Passi di ImportAsync: pagina, Wikidata, edizioni, gerarchia luoghi, dettagli luoghi, nomi luoghi, famiglie e titoli, composizione.
    public const int ImportSteps = 8;

    private int step;
    private string message = string.Empty;

    public int TotalSteps => importSteps + offset;

    public void Step(int importStep, string text)
    {
        step = importStep + offset;
        message = text;
        target?.Report(new ImportProgress(step, TotalSteps, message));
    }

    public void Sub(double fraction, string detail)
        => target?.Report(new ImportProgress(step, TotalSteps, message, fraction, detail));
}
