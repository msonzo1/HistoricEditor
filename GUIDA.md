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

### Gens e rami

Gens e rami non sono dinastie: si registrano nella finestra «Gentes…» e si scelgono nella scheda con «Dinastia e gens…». Di ognuno si scrivono le forme latine così come sono, perché i plurali non seguono sempre una regola:

| | Latino | Italiano (facoltativo) |
|---|---|---|
| Gens: nome | Iulia | Giulia |
| Gens: nomen | Iulius | |
| Gens: plurale | Iulii | Giulii |
| Ramo: cognomen (facoltativo) | Caesar | |
| Ramo: plurale | Caesares | Cesari |

Le etichette non si scrivono: si compongono da queste forme. La gens è «Gens Iulia» («Gens Giulia» in italiano), il ramo è il plurale della gens seguito da quello del ramo, «Iulii Caesares» («Giulii Cesari»). Dove manca l'italiano si usa il latino. Nomen e plurale della gens sono attesi: se mancano, la finestra li segnala, ma si salva lo stesso. Quando il latino è diverso, compare accanto all'italiano: «Gens Giulia (Gens Iulia) › Giulii Cesari (Iulii Caesares)».

Per le dinastie il nome latino resta facoltativo: per esempio la dinastia Giulio-Claudia è la Domus Iulio-Claudia.

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

  Dalle etichette uniformate l'import ricava le forme da salvare (Iulia e Iulii per la gens, Caesares per il ramo); il nomen viene dal nomen della persona, senza i segni delle vocali lunghe (Iūlius → Iulius). Una gens nuova si crea solo al salvataggio della scheda e prima si può correggere nella finestra «Gentes…»; di una gens già registrata si completano solo i campi vuoti.
- **Nomi nel tempo.** Wikidata non li distingue. L'import li legge dalla voce italiana di Wikipedia: il campo «nome completo» dell'infobox e l'introduzione.
