using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class HistoricRemainsLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Una volta sola (2026-11-17): le schede nate, morte o sepolte in un luogo storico i cui resti sono già
            // nell'archivio (Pompeo, morto a Pelusio) indicano anche i resti come luogo specifico. I resti si riconoscono
            // dal luogo che contiene il luogo storico, dallo stesso codice Wikidata e dalla nota «Resti di …» scritta
            // dall'import. Il testo del luogo prende il nome dei resti tra parentesi, come al salvataggio.
            migrationBuilder.Sql("""
                UPDATE People SET
                    BirthLuogoSpecificoId = (SELECT s.Id FROM Luoghi l JOIN LuoghiSpecifici s ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId
                        AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.BirthLuogoId ORDER BY s.Id LIMIT 1),
                    BirthPlace = CASE WHEN trim(coalesce(BirthPlace, '')) = '' THEN BirthPlace ELSE BirthPlace || ' (' || (SELECT s.Nome FROM Luoghi l JOIN LuoghiSpecifici s
                        ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.BirthLuogoId ORDER BY s.Id LIMIT 1) || ')' END
                WHERE BirthLuogoSpecificoId IS NULL AND EXISTS (SELECT 1 FROM Luoghi l JOIN LuoghiSpecifici s ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId
                    AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.BirthLuogoId AND l.Storico = 1 AND l.WikidataId <> '');
                """);

            migrationBuilder.Sql("""
                UPDATE People SET
                    DeathLuogoSpecificoId = (SELECT s.Id FROM Luoghi l JOIN LuoghiSpecifici s ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId
                        AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.DeathLuogoId ORDER BY s.Id LIMIT 1),
                    DeathPlace = CASE WHEN trim(coalesce(DeathPlace, '')) = '' THEN DeathPlace ELSE DeathPlace || ' (' || (SELECT s.Nome FROM Luoghi l JOIN LuoghiSpecifici s
                        ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.DeathLuogoId ORDER BY s.Id LIMIT 1) || ')' END
                WHERE DeathLuogoSpecificoId IS NULL AND EXISTS (SELECT 1 FROM Luoghi l JOIN LuoghiSpecifici s ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId
                    AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.DeathLuogoId AND l.Storico = 1 AND l.WikidataId <> '');
                """);

            migrationBuilder.Sql("""
                UPDATE People SET
                    BurialLuogoSpecificoId = (SELECT s.Id FROM Luoghi l JOIN LuoghiSpecifici s ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId
                        AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.BurialLuogoId ORDER BY s.Id LIMIT 1),
                    BurialPlace = CASE WHEN trim(coalesce(BurialPlace, '')) = '' THEN BurialPlace ELSE BurialPlace || ' (' || (SELECT s.Nome FROM Luoghi l JOIN LuoghiSpecifici s
                        ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.BurialLuogoId ORDER BY s.Id LIMIT 1) || ')' END
                WHERE BurialLuogoSpecificoId IS NULL AND EXISTS (SELECT 1 FROM Luoghi l JOIN LuoghiSpecifici s ON s.LuogoId = l.ParentId AND s.WikidataId = l.WikidataId
                    AND s.Note = 'Resti di ' || l.Nome || '.' WHERE l.Id = People.BurialLuogoId AND l.Storico = 1 AND l.WikidataId <> '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
