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
    captureAria: 'Prendi un appunto in Inbox per {column}',
  },
  column: {
    settings: 'Impostazioni colonna',
    settingsAria: 'Impostazioni della colonna {column}',
    moveLeft: 'Sposta la colonna a sinistra',
    moveRight: 'Sposta la colonna a destra',
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
    archiveConfirmAction: 'Sì, archiviala',
    archiveConfirmCancel: 'Lasciala qui',
    restore: 'Ripristina la bacheca',
    saveError: 'Non è stato possibile salvare la bacheca. Riprova.',
    archiveError: 'Non è stato possibile archiviare la bacheca. Riprova.',
    restoreError: 'Non è stato possibile ripristinare la bacheca. Riprova.',
  },
}
