namespace HistoricEditor;

// Spiegazioni delle «i» accanto ai campi (stile InfoIcon): compaiono passando sopra la «i» o portandoci il focus con Tab.
public static class InfoTexts
{
    public static string Calendari { get; } =
        "Indica con quale calendario è scritta la data. Conta solo per le date con giorno e mese: "
        + "tra i due calendari la stessa giornata cade in date diverse (10 giorni nel Cinquecento, 13 dal 1900).\n\n"
        + "• Non indicato: la data resta com’è, senza conversioni. Va bene quasi sempre, ad esempio per le date di Wikipedia.\n"
        + "• Giuliano: introdotto da Giulio Cesare nel 46 a.C., usato in Europa fino al 1582 e in alcuni paesi più a lungo "
        + "(Inghilterra fino al 1752, Russia fino al 1918).\n"
        + "• Gregoriano: il calendario di oggi, di papa Gregorio XIII: al 4 ottobre 1582 seguì il 15 ottobre. "
        + "Negli Stati italiani entrò subito in vigore.";

    public static string StiliAnno { get; } =
        "Fino al Settecento in molte città l’anno non iniziava il 1° gennaio: lo stesso giorno poteva avere un anno diverso da quello di oggi.\n\n"
        + string.Join("\n", HistoricalCalendar.Stili.Select(s => $"• {s.Etichetta}: {s.Descrizione}"));

    // Per i moduli di persone ed eventi, dove l'anno si scrive sempre come oggi.
    public static string CalendariEStili { get; } =
        Calendari
        + "\n\nStile dell’anno: nelle schede l’anno si scrive sempre come oggi, con inizio il 1° gennaio. "
        + "Per una data presa da un documento originale scritto in un altro stile (es. un atto fiorentino del 1525) "
        + "usa «Convertitore…»: calcola l’anno moderno e lo riporta nella scheda.";

    public static string NomeRomano { get; } =
        "I Romani avevano un nome in più parti (tria nomina):\n\n"
        + "• Prenome (praenomen): il nome personale (Gaio, Marco…).\n"
        + "• Nome gentilizio (nomen): il nome della gens, la famiglia allargata (Giulio per la gens Giulia). Le donne lo portavano al femminile (Giulia).\n"
        + "• Cognome (cognomen): distingueva i rami della gens o era un soprannome ereditario (Cesare).\n"
        + "• Agnome (agnomen): soprannome personale, spesso onorifico (Africano).\n\n"
        + "Le parti si scrivono nella lingua scelta in alto («Lingua visualizzata»): in italiano Gaio Giulio Cesare, in inglese Gaius Julius Caesar. "
        + "La tendina «Nome in latino», uguale per tutte le lingue, contiene le forme latine, la filiazione e la tribù, "
        + "che compaiono nelle epigrafi: «C. Iulius C. f. Fab. Caesar» = Gaio Giulio Cesare, figlio di Gaio, della tribù Fabia.\n\n"
        + "Con «Automatica» la scheda diventa romana per chi è nato (o, senza nascita, morto) tra il 753 a.C. e il 476, "
        + "o se ha già un nome romano. «Standard» serve per un personaggio dell’epoca che non era romano (es. un greco), «Romana» per forzarla.\n\n"
        + "Il nome italiano resta quello degli elenchi; il nome latino compare sotto il titolo della scheda, nella scheda di lettura "
        + "e nell’elenco con «Nomi latini» (Ctrl+L). La gens si registra tra le dinastie, con i suoi rami.";
}
