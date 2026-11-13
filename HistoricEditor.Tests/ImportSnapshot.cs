using System.Globalization;
using System.IO;
using System.Text;
using HistoricEditor;

namespace HistoricEditor.Tests;

// Riepilogo testuale di un import, una informazione per riga, per confrontarlo tra una versione e l'altra del parser.
// Comprende anche la scheda come la compila il modulo (ImportedPersonCreator.BuildPerson) e il confronto tra le edizioni.
internal static class ImportSnapshot
{
    public static string Describe(WikipediaImportResult r)
    {
        var text = new StringBuilder();
        void Line(string label, object? value) => text.Append(label).Append(": ").Append(value).Append('\n');
        void Section(string title) => text.Append('\n').Append("## ").Append(title).Append('\n');

        var person = ImportedPersonCreator.BuildPerson(r);
        Section("Scheda");
        Line("Pagina", $"{r.ArticleTitle} ({r.ArticleUrl})");
        Line("Wikidata", r.WikidataId);
        Line("WikiTree", r.WikiTreeId);
        Line("Nome visualizzato", person.DisplayName);
        Line("Nome / Cognome / Suffisso / Soprannome", $"{r.Name} / {r.Surname} / {r.Suffix} / {r.Nickname}");
        Line("Sesso", r.Sesso);
        Line("Titolo principale", r.MainTitle);
        Line("Famiglia", r.Family);
        Line("Gens", r.Gens);
        Line("Nascita", $"{person.Birth} [calendario: {person.BirthCalendario}]");
        Line("Morte", $"{person.Death} [calendario: {person.DeathCalendario}]");
        Line("Edizioni non lette", string.Join(", ", r.UnreadEditions));

        Section("Nome romano");
        Line("Latino (praenomen / nomen / cognomen / agnomen)", $"{r.Praenomen} / {r.Nomen} / {r.Cognomen} / {r.Agnomen}");
        Line("Nome latino", r.NomeLatino);
        Line("Italiano (prenome / gentilizio / cognome / agnome)", $"{r.Prenome} / {r.Gentilizio} / {r.CognomeRomano} / {r.Agnome}");

        Section("Nomi nelle lingue");
        foreach (var n in r.LocalizedNames)
        {
            Line(n.Lingua, $"{n.Nome} | suffisso {n.Suffisso} | soprannome {n.Soprannome} | titolo {n.TitoloPrincipale} | parti {n.Prenome} / {n.Gentilizio} / {n.CognomeRomano} / {n.Agnome}");
        }

        Section("Dinastie");
        foreach (var d in r.Dynasties)
        {
            Line(d.WikidataId, $"{d.Nome}{(d.Gens ? " (gens)" : string.Empty)} ← {d.ParentNome} {d.ParentWikidataId}".TrimEnd());
        }

        Section("Date di nascita");
        Dates(r.BirthDates);
        Section("Date di morte");
        Dates(r.DeathDates);
        Section("Date scartate (una sola fonte)");
        Dates(r.DiscardedBirthDates.Concat(r.DiscardedDeathDates).ToList());
        Line("Date discordanti", DateDiscrepancy.DescribeAll(person.BirthDateEvidence, person.DeathDateEvidence).Replace(Environment.NewLine, " | "));

        Section("Luoghi");
        Places("nascita", r.BirthPlaces);
        Places("morte", r.DeathPlaces);
        Places("sepoltura", r.BurialPlaces);

        Section("Titoli");
        foreach (var t in r.Titles)
        {
            Line(t.Titolo, $"n. {t.Numerazione} | {t.Inizio} ({t.CausaInizio}) – {t.Fine} ({t.CausaFine}) | {t.WikidataId}"
                + $" | prima {t.Predecessore} {t.PredecessoreWikidataId} | dopo {t.Successore} {t.SuccessoreWikidataId}");
            foreach (var source in t.FontiDate)
            {
                Line("    fonte", $"{source.Fonte}: {source.Inizio} – {source.Fine}");
            }
        }

        Section("Introduzioni di Wikipedia");
        foreach (var l in r.WikipediaLeads)
        {
            Line(l.Language, $"{l.Title} | nato {l.BirthDay}/{l.BirthMonth}/{l.BirthYear} a {l.BirthPlace} [{l.BirthPlaceKey}]"
                + $" | morto {l.DeathDay}/{l.DeathMonth}/{l.DeathYear} a {l.DeathPlace} [{l.DeathPlaceKey}]");
        }

        var consensus = WikipediaConsensus.Evaluate(r.WikipediaLeads);
        Line("Concordanza", $"{consensus.Level}: {consensus.Summary}");
        foreach (var edition in consensus.Editions)
        {
            Line("    " + edition.LanguageName, $"{edition.Birth} – {edition.Death} | {edition.DifferencesText}");
        }

        Section("Parenti e relazioni");
        Line("Padre", string.Join(", ", r.FatherIds));
        Line("Madre", string.Join(", ", r.MotherIds));
        Line("Coniugi", string.Join(", ", r.SpouseIds));
        Line("Figli", string.Join(", ", r.ChildIds));
        Line("Relazioni", string.Join(", ", r.Relations.Select(x => $"{x.WikidataId} {x.Tipo}{(x.Attivo ? " (attivo)" : string.Empty)}")));
        Line("Eventi", string.Join(", ", r.EventIds));
        return text.ToString();

        void Dates(IEnumerable<HistoricalDateEvidence> dates)
        {
            foreach (var d in dates)
            {
                Line(d.Value, $"{d.Precision} | {d.Rank} | {d.Calendario} | {d.RawValue}"
                    + $" | qualificatori {string.Join(", ", d.Qualifiers.Select(q => $"{q.Key}={string.Join('/', q.Value)}"))}"
                    + $" | fonti {string.Join(", ", d.Sources.Select(s => s.WikidataId.Length > 0 ? s.WikidataId : s.Title))}");
            }
        }

        void Places(string role, IEnumerable<HistoricalPlaceRecord> places)
        {
            foreach (var p in places)
            {
                Line(role, $"{p.WikidataId} {p.CurrentName} | {p.CurrentCountry} | tipo {p.TypeLabel} | "
                    + string.Create(CultureInfo.InvariantCulture, $"{p.Latitudine}, {p.Longitudine}"));
                foreach (var division in p.AdministrativeDivisions)
                {
                    Line("    divisione", $"{division.Relation} {division.Name} {division.WikidataId}");
                }

                if (p.InfoboxDivisions.Count > 0)
                {
                    Line("    infobox", string.Join(" / ", p.InfoboxDivisions));
                }

                foreach (var name in p.HistoricalNames)
                {
                    Line("    nome storico", $"{name.Nome} ({name.Lingua}, {name.Dal} – {name.Al})");
                }

                var derived = DerivePlace(p);
                Line("    in archivio", string.Join(" > ", derived.Levels.Select(l => l.Assente ? $"({l.Tipo} assente)" : $"{l.Tipo} {l.Nome}"))
                    + (derived.SpecificName is { Length: > 0 } specific ? $" > luogo specifico {specific}{(p.SpecificCondition.Length > 0 ? $" ({p.SpecificCondition})" : string.Empty)}" : string.Empty)
                    + (derived.HistoricName is { Length: > 0 } historic
                        ? $" > luogo storico {historic}{(derived.LatinName.Length > 0 ? $" (latino: {derived.LatinName})" : string.Empty)}"
                            + (p.HistoricBuildingName.Length > 0 ? $" > luogo specifico {p.HistoricBuildingName} ({p.HistoricBuildingType}{(p.SpecificCondition.Length > 0 ? $", {p.SpecificCondition}" : string.Empty)})" : string.Empty)
                        : string.Empty)
                    + (derived.Confident ? " [automatico]" : " [da confermare]"));
            }
        }
    }

    // Luogo come lo ricava l'app dalla scheda importata, su un archivio vuoto.
    private static DerivedPlace DerivePlace(HistoricalPlaceRecord place)
    {
        var path = Path.Combine(Path.GetTempPath(), $"historic-snapshot-{Guid.NewGuid():N}.db");
        try
        {
            using var context = new AppDbContext(path);
            context.Database.EnsureCreated();
            return GeographyResolver.DeriveLevels(context, place);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Il file temporaneo verrà ripulito dal sistema.
            }
        }
    }
}
