/**
 * Board detail surface — Italian. Glossary as in `./home.ts`, plus:
 *   card    → scheda (as in `./review.ts`)
 *   column  → colonna
 *   archive → archivio / archiviare
 *
 * "Archiviare" is deliberately never rendered as "eliminare": the action hides
 * the board and is reversible, and the English says so too.
 */
export default {
  actions: {
    settings: 'Impostazioni bacheca',
    // Presentazione delle schede della bacheca. La modalità nasconde l'estratto
    // e la riga dei dettagli; non elimina né modifica nulla, quindi il testo
    // nomina ciò che si smette di vedere. Il nome accessibile inizia con
    // l'etichetta visibile (WCAG 2.5.3).
    titlesOnly: 'Solo titoli',
    titlesOnlyAria: 'Solo titoli: nasconde gli estratti e i dettagli delle schede',
    // Larghezza delle colonne. L'etichetta visibile è breve perché la riga non
    // concede altro; il nome accessibile dice che cosa si sta dimensionando. I
    // nomi delle impostazioni concordano con "colonna": è la colonna a essere
    // stretta o ampia. Il valore salvato resta il `value` in inglese.
    width: 'Larghezza',
    widthAria: 'Larghezza della colonna',
    widthNarrow: 'Stretta',
    widthStandard: 'Standard',
    widthWide: 'Ampia',
    // Spaziatura della bacheca. La modalità compatta riduce spazi e margini;
    // non nasconde né modifica nulla, quindi il nome accessibile dice che cosa
    // si riduce e a quale scopo, e inizia con l'etichetta visibile (WCAG 2.5.3).
    compactDensity: 'Densità compatta',
    compactDensityAria: 'Densità compatta: riduce le spaziature della bacheca per mostrare più schede',
  },
  card: {
    add: '+ scheda',
    addAria: 'Aggiungi una scheda a {column}',
    inputLabel: 'Titolo della nuova scheda',
    placeholder: 'Titolo della scheda',
    submit: 'Aggiungi',
    cancel: 'Annulla',
    error: 'Non è stato possibile aggiungere la scheda. Riprova.',
    capture: '+ appunto',
    // Nessuna colonna nel nome accessibile (#1984, constatazione 2): il
    // controllo di ogni colonna apre l'Inbox della bacheca e nient'altro, quindi
    // nomi diversi per colonna annuncerebbero una distinzione inesistente.
    // `addAria` mantiene `{column}` perché quel controllo cambia davvero.
    captureAria: 'Prendi un appunto nell’Inbox di questa bacheca',
  },
  column: {
    settings: 'Impostazioni colonna',
    settingsAria: 'Impostazioni della colonna {column}',
    moveLeft: 'Sposta la colonna a sinistra',
    moveRight: 'Sposta la colonna a destra',
    collapseAria: 'Comprimi la colonna {column}',
    expandAria: 'Espandi la colonna {column}',
    add: '+ colonna',
    addAria: 'Aggiungi una colonna a questa bacheca',
    addInputLabel: 'Nome della nuova colonna',
    addPlaceholder: 'Nome della colonna',
    addSubmit: 'Aggiungi',
    addCancel: 'Annulla',
    addError: 'Non è stato possibile creare la colonna. Riprova.',
  },
  columnDialog: {
    eyebrow: 'Colonna',
    title: 'Impostazioni colonna',
    close: 'Chiudi le impostazioni della colonna',
    nameLabel: 'Nome della colonna',
    namePlaceholder: 'Da fare',
    wipToggle: 'Imposta un limite WIP',
    wipLabel: 'Numero massimo di schede',
    wipHint:
      "Le schede oltre il limite segnalano l'intestazione della colonna. Lascialo disattivato per nessun limite.",
    save: 'Salva le modifiche',
    cancel: 'Annulla',
    delete: 'Elimina la colonna',
    deleteBlocked: 'Prima sposta o elimina le schede in questa colonna.',
    deleteConfirm: 'Eliminare "{name}" e le sue impostazioni? Non si può annullare.',
    deleteConfirmAction: 'Sì, eliminala',
    deleteConfirmCancel: 'Tienila',
    saveError: 'Non è stato possibile salvare la colonna. Riprova.',
    deleteError: 'Non è stato possibile eliminare la colonna. Riprova.',
  },
  boardDialog: {
    eyebrow: 'Bacheca',
    title: 'Impostazioni bacheca',
    close: 'Chiudi le impostazioni della bacheca',
    nameLabel: 'Nome della bacheca',
    namePlaceholder: 'La mia bacheca',
    descriptionLabel: 'Descrizione',
    descriptionPlaceholder: 'A cosa serve questa bacheca?',
    save: 'Salva le modifiche',
    cancel: 'Annulla',
    lifecycle: 'Ciclo di vita',
    stateActive: 'Attiva',
    stateArchived: 'Archiviata',
    archiveHint:
      "L'archiviazione nasconde questa bacheca dagli elenchi. Non si elimina nulla — puoi ripristinarla da Area di lavoro → Archivio.",
    restoreHint:
      'Questa bacheca è archiviata. Ripristinala per rimetterla negli elenchi delle bacheche attive.',
    archive: "Sposta nell'archivio",
    archiveConfirm: 'Spostare "{name}" nell\'archivio? Potrai ripristinarla più tardi.',
    archiveConfirmHistory:
      'Le catture e la cronologia delle decisioni restano salvate. Puoi consultarle da Area di lavoro → Archivio; non appariranno in Inbox o Revisione senza filtri finché la bacheca è archiviata.',
    archiveConfirmAction: 'Sì, archiviala',
    archiveConfirmCancel: 'Lasciala qui',
    restore: 'Ripristina la bacheca',
    saveError: 'Non è stato possibile salvare la bacheca. Riprova.',
    restoreError: 'Non è stato possibile ripristinare la bacheca. Riprova.',
  },
}
