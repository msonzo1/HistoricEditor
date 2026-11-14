# Guida

Spiegazioni per compilare e controllare le schede. Il [README](README.md) descrive le funzioni dell'applicazione, [DECISIONI.md](DECISIONI.md) il perché delle scelte.

## Il nome romano

### Le parti del nome

Nel modulo di un nome romano ogni riga è una parte, nell'ordine in cui si legge. Ogni riga ha la forma latina e quella italiana.

| Tipo | Che cos'è | Esempio |
|---|---|---|
| Praenomen | Nome personale, scelto fra pochi nomi; nelle epigrafi è abbreviato | **Gaius** (C.) Iulius Caesar |
| Nomen | Nome della gens, la stirpe di appartenenza | Gaius **Iulius** Caesar |
| Filiazione | «Figlio di»: il praenomen del padre al genitivo | M. Tullius **M. f.** Cicero (Marci filius); Imperator Caesar **Divi filius** Augustus |
| Tribù | Tribù di voto del cittadino | M. Tullius M. f. **Cor.** Cicero (tribù Cornelia) |
| Cognomen | Soprannome diventato nome di famiglia o di ramo; possono essercene più d'uno | Gaius Iulius **Caesar** |
| Nomen d'origine | Aggettivo in -ianus ricavato dal nomen di nascita di un adottato (vedi sotto) | Gaius Iulius Caesar **Octavianus**; Publius Cornelius Scipio **Aemilianus** |
| Agnomen | Soprannome aggiunto per un merito o una vittoria | Publius Cornelius Scipio **Africanus**; Traianus **Germanicus** |
| Titolo | Titoli entrati nel nome degli imperatori | **Imperator Caesar** Vespasianus **Augustus** |

Filiazione e tribù stanno nel punto in cui si leggono. Compaiono solo nella **forma epigrafica**, che abbrevia praenomen, titoli, filiazione e tribù: «Marcus Tullius Cicero» si scrive «M. Tullius M. f. Cor. Cicero». Non compaiono invece nel nome latino né nel nome italiano mostrati.

### L'adozione

Chi veniva adottato prendeva il nome completo del padre adottivo. Per consuetudine aggiungeva il proprio vecchio nomen trasformato in aggettivo con il suffisso **-ianus**:

- Publio Cornelio Scipione Emiliano, nato nella gens Emilia (Aemilius), adottato da un Cornelio Scipione: Publius Cornelius Scipio **Aemilianus**;
- Gaio Ottavio, nato nella gens Ottavia (Octavius), adottato da Cesare: Gaius Iulius Caesar **Octavianus**.

Questo aggettivo è il **nomen d'origine**: non è un agnomen né un cognomen. Si usa di rado, e nella scheda ha un tipo di riga proprio.

Il personaggio ha due gens, ciascuna con i suoi rami, e nella scheda stanno in due campi separati («Dinastia e gens…»):

- **gens di nascita**: Augusto, gens Ottavia, ramo degli Octavii Rufi;
- **gens d'adozione**: Augusto, gens Giulia, ramo degli Iulii Caesares.

Nell'elenco delle Persone il personaggio compare sotto la gens di nascita.

### Il nome latino delle gens

Ogni gens e ogni ramo ha, oltre al nome italiano, il **nome latino**, che si scrive nell'archivio delle dinastie:

| Nome italiano | Nome latino |
|---|---|
| gens Giulia | gens Iulia |
| gens Ottavia | gens Octavia |
| Iulii Caesares | Iulii Caesares |

Per le gens il nome latino è atteso. Se manca, la scheda e l'archivio lo segnalano, ma si salvano lo stesso. Per le altre dinastie è facoltativo: per esempio la dinastia Giulio-Claudia è la Domus Iulio-Claudia. Quando è noto, compare accanto al nome italiano: «gens Giulia (gens Iulia)».

Con più gens nel campo (la gens e i suoi rami), il pulsante «Gens… ▾» apre un menu da cui si sceglie quale modificare nell'archivio.

### I nomi nel tempo

Un personaggio può aver portato nomi diversi nelle varie fasi della vita. Ogni nome ha un periodo e un motivo:

- **Nome alla nascita**: Gaius Octavius.
- **Adozione** o **Adozione testamentaria**, quando l'adozione è disposta nel testamento. Cesare adottò Ottavio nel suo testamento (44 a.C.), e l'adozione fu ratificata nel 43 a.C.
- **Assunzione del potere**: Imperator Caesar Divi filius Augustus, dal 27 a.C.
- **Convenzione storiografica**: un nome dato dagli storici moderni che la persona non usò mai.

Il caso tipico è Augusto. Dopo l'adozione si sarebbe dovuto chiamare Gaius Iulius Caesar Octavianus, ma non usò mai «Octavianus»: gli ricordava le origini modeste e lo distingueva da Cesare, mentre lui voleva presentarsi come «Cesare», suo erede. A chiamarlo Ottaviano erano gli avversari e gli osservatori, come Cicerone in alcune lettere. «Ottaviano» per gli anni 44–27 a.C. è una convenzione moderna, comoda per non confonderlo con Giulio Cesare.

Uno dei nomi è il **principale** (★): dà il nome latino della scheda.

### Quello che arriva da Wikidata

I dati di Wikidata sui nomi romani sono spesso confusi o incoerenti. L'import li interpreta così, ma la scheda va sempre controllata:

- **Parti abbandonate.** Le parti con «causa della fine» o data di fine appartengono a un nome precedente. Marco Aurelio era nato Marcus Annius Catilius Severus, e Annius e Catilius non fanno parte del suo nome attuale.
- **Parti contestate.** Le parti segnate come «contestate» si scartano: Vespasiano non è «Sabinus».
- **Agnomen al posto del cognomen.** Se il cognomen manca, un agnomen ne prende il posto: Vespasianus è il cognomen di Vespasiano.
- **«Agnomen» che sono nomina d'origine.** Wikidata mette «Octavianus» fra gli agnomina, con la nota «adoptive suffix nomina». L'import lo tratta come nomen d'origine.
- **Gens e rami presi per adozione.** Una gens o una famiglia con la causa «adozione» va nella gens d'adozione. Per Augusto, gli Iulii Caesares non sono la gens di nascita.
- **Gens di nascita sconosciuta.** Se Wikidata non la indica (per Marco Aurelio è «valore sconosciuto»), l'import la ricava dal nomen di nascita: Annius dà la gens Annia. In mancanza di questo la ricava dal nomen d'origine: Octavianus dà la gens Ottavia.
- **Nomi latini delle gens.** Le etichette latine sono incoerenti. La gens Giulia è «Iulii», la gens Ottavia «gens Octavia», in inglese «Julia gens»; il ramo dei Cesari è «Julii Caesares»; la Dinastia flavia ha addirittura «Gens Flavia». L'import le uniforma:
  - una gens diventa «gens Iulia»;
  - un ramo resta al plurale, «Iulii Caesares», e la J diventa sempre I;
  - per le altre dinastie si usa solo un'etichetta latina che non sia una «gens …».

  Un ramo senza nome italiano prende quello latino.
- **Nomi nel tempo.** Wikidata non li distingue. L'import li legge dalla voce italiana di Wikipedia: il campo «nome completo» dell'infobox e l'introduzione.
