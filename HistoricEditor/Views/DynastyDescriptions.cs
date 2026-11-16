namespace HistoricEditor;

// Famiglie scritte in un campo, mostrate come percorso nella gerarchia delle dinastie registrate:
// «gens Flavia; Flavii Sabini» → «gens Flavia › Flavii Sabini», «Asburgo di Spagna» → «Casa d'Asburgo › Asburgo di Spagna».
internal static class DynastyDescriptions
{
    // Percorso (le famiglie indipendenti separate da « · ») e nota sulle famiglie non ancora registrate.
    public static (string Path, string Note) Summarize(IReadOnlyList<Dinastia> dynasties, string text, bool gens)
    {
        var names = PeopleGrouping.FamilyNames(text);
        if (names.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        Dinastia? Find(string name) => dynasties.FirstOrDefault(d => PeopleGrouping.FamilyComparer.Equals(d.Nome.Trim(), name));

        // Antenati registrati di ciascuna famiglia, dalla radice.
        List<string> Ancestors(Dinastia dynasty)
        {
            var result = new List<string>();
            var visited = new HashSet<int> { dynasty.Id };
            var current = dynasty;
            while (current.ParentId is int p && visited.Add(p) && dynasties.FirstOrDefault(d => d.Id == p) is { } parent)
            {
                result.Insert(0, parent.NomeCompleto);
                current = parent;
            }

            return result;
        }

        // Ogni famiglia registrata con il suo nome latino: «gens Giulia (gens Iulia) › Iulii Caesares».
        var paths = names.Select(n => Find(n) is { } d ? [.. Ancestors(d), d.NomeCompleto] : new List<string> { n }).ToList();
        // Una famiglia già compresa nel percorso di un'altra indicata (la gens del ramo) non si ripete.
        var shown = paths
            .Where(p => !paths.Any(other => !ReferenceEquals(other, p) && other.Count > p.Count
                && PeopleGrouping.FamilyComparer.Equals(other[p.Count - 1], p[^1])))
            .Select(p => string.Join(" › ", p))
            .Distinct(PeopleGrouping.FamilyComparer);

        var missing = names.Where(n => Find(n) is null).ToList();
        var note = missing.Count == 0
            ? string.Empty
            : (gens ? (missing.Count == 1 ? "Nuova gens" : "Nuove gens") : (missing.Count == 1 ? "Nuova dinastia" : "Nuove dinastie"))
                + (names.Count == 1 ? ", da registrare al salvataggio" : $", da registrare al salvataggio: {string.Join(", ", missing)}");
        return (string.Join(" · ", shown), note);
    }

    // Suggerimento sotto un campo: il percorso solo se aggiunge una gerarchia o un nome latino, poi la nota sulle famiglie
    // nuove e quella sulle gens senza nome latino.
    public static string Hint(IReadOnlyList<Dinastia> dynasties, string text, bool gens)
    {
        var (path, note) = Summarize(dynasties, text, gens);
        var latin = gens ? MissingLatin(dynasties, text) : string.Empty;
        return string.Join("\n", new[] { path.Contains('›') || path.Contains('(') ? path : string.Empty, note, latin }.Where(s => s.Length > 0));
    }

    // Gens registrate (con i loro superiori) a cui manca il nome latino, che per le gens è atteso (2026-11-14).
    public static string MissingLatin(IReadOnlyList<Dinastia> dynasties, string text)
    {
        var missing = new List<string>();
        foreach (var name in PeopleGrouping.FamilyNames(text))
        {
            var visited = new HashSet<int>();
            for (var d = Dinastia.Find(dynasties, name); d is not null && visited.Add(d.Id); d = d.ParentId is int p ? dynasties.FirstOrDefault(x => x.Id == p) : null)
            {
                if (d.IsGens && d.NomeLatino.Trim().Length == 0 && !missing.Contains(d.Nome.Trim(), PeopleGrouping.FamilyComparer))
                {
                    missing.Add(d.Nome.Trim());
                }
            }
        }

        return missing.Count == 0 ? string.Empty : $"Nome latino da completare nell'archivio delle dinastie: {string.Join(", ", missing)}";
    }
}

// Pulsanti «Dinastia…» e «Gens…» (2026-11-14): con più nomi nel campo un menu sceglie quale aprire nell'archivio, con il
// nome latino accanto («gens Giulia (gens Iulia)»); con un nome solo (o nessuno) l'archivio si apre subito.
internal static class DynastyMenu
{
    // Matita come gli altri pulsanti accanto ai campi (2026-11-17); con più dinastie nel campo il clic apre il menu per
    // scegliere quale modificare, e il suggerimento lo dice.
    public static void UpdateButton(System.Windows.Controls.Button button, string fieldText)
    {
        button.ToolTip = ButtonToolTip(fieldText);
        System.Windows.Automation.AutomationProperties.SetName(button,
            PeopleGrouping.FamilyNames(fieldText).Count > 1 ? "Modifica una delle dinastie" : "Modifica la dinastia");
    }

    public static string ButtonToolTip(string fieldText)
        => PeopleGrouping.FamilyNames(fieldText).Count > 1
            ? "Modifica una delle dinastie nell'archivio (nome latino, ramo di, Wikidata, note): si sceglie dal menu"
            : "Modifica la dinastia nell'archivio: nome latino, ramo di, Wikidata, note";

    public static void Choose(System.Windows.Controls.Button button, IReadOnlyList<Dinastia> dynasties, string fieldText, Action<string?> open)
    {
        var names = PeopleGrouping.FamilyNames(fieldText);
        if (names.Count <= 1)
        {
            open(names.FirstOrDefault());
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        foreach (var name in names)
        {
            // Il testo non ha tasti di scelta rapida: un trattino basso nel nome resta com'è.
            var item = new System.Windows.Controls.MenuItem { Header = new System.Windows.Controls.TextBlock { Text = Dinastia.Find(dynasties, name)?.NomeCompleto ?? name } };
            item.Click += (_, _) => open(name);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
