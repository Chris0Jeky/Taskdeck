/**
 * Review surface — Italian. Owning SFCs and the full semantic contract are in
 * `../en/review.ts`; only translation notes live here.
 *
 * Glossary: board → bacheca, proposal → proposta, capture → cattura,
 * to apply → applicare, to file away → archiviare, to defer → rimandare.
 * "Nib", "Composer", "Paper", "diff" and "JSON" stay in English (product
 * coinages and technical loanwords, ADR-0054 §3).
 *
 * Contract reminders that bind THIS file:
 *   - `age.*` are one- or two-character suffixes glued to a number. Days is
 *     `g` (giorni), not `d`.
 *   - `status.*` / `statusInline.*` are RENDERED labels only; the backend wire
 *     values (`PendingReview`, `Applied`, …) are compared in code and are not
 *     translated anywhere.
 *   - Everything the server sends — proposal summaries, board names, provenance
 *     rows, backend validation messages — arrives through `{placeholders}` and
 *     stays in whatever language the server produced.
 */
export default {
  queueRail: {
    eyebrow: 'Coda · {awaiting} in attesa · {stale} ferme',
    eyebrowScoped: 'Coda · {awaiting} in attesa in questa bacheca · {stale} ferme',
    filters: {
      label: 'Filtri della coda',
    },
    filter: {
      all: 'Tutte',
      mine: 'Mie',
      stale: 'Ferme',
    },
    riskNote:
      'Ordine di rischio: basso, medio, alto, critico. L’ordinamento cambia solo la presentazione; le azioni di revisione restano manuali.',
    fileAway: {
      cta: 'Archivia {count} concluse',
      label: 'Archivia {count} proposte concluse',
    },
    empty: 'Niente in questo filtro.',
    cadence: {
      heading: 'Questa settimana',
      applyRateLabel: 'Tasso di applicazione',
      applyRateEmpty: 'Ancora nessuna decisione',
    },
  },

  scope: {
    board: 'Bacheca: {board}',
    clear: 'Mostra tutte le bacheche',
  },

  historyMode: {
    notice: 'Cronologia delle decisioni archiviate · sola lettura. Ripristina la bacheca prima di approvare, rifiutare, applicare, modificare, rinviare o archiviare proposte.',
  },

  queueItem: {
    noSummary: '(nessun riepilogo)',
    confidence: 'conf {value}',
    reach: '{count} op | {count} op',
    who: {
      assistant: 'assistente',
      capture: 'cattura',
    },
  },

  age: {
    seconds: '{value}s',
    minutes: '{value}m',
    hours: '{value}h',
    days: '{value}g',
  },

  cadence: {
    ariaLabel: 'Attività dell’ultimo {count} giorno | Attività degli ultimi {count} giorni',
  },

  recent: {
    heading: 'Applicate di recente',
    empty: 'Oggi non è stato applicato niente.',
    noSummary: '(applicata)',
    age: '{age} fa',
    openLabel: 'Apri proposta applicata: {title}',
  },

  appliedRecord: {
    ariaLabel: 'Registro della decisione per la proposta applicata',
    tagstamp: 'APPLICATA \u00b7 SOLA LETTURA',
    eyebrow: 'Registro storico',
    heading: 'Registro della decisione applicata',
    lede:
      'Questa proposta ha gi\u00e0 modificato la bacheca. La decisione registrata e le operazioni effettive sono in sola lettura.',
    filingSummary: 'Registro storico \u00b7 solo archiviazione',
    historicalNotice: 'Registro storico applicato. Non sono disponibili altre azioni di revisione.',
    field: {
      outcome: 'Esito',
      decision: 'Decisione',
      decisionActor: 'Autore della decisione',
      decisionTime: 'Ora della decisione',
      appliedTime: 'Ora di applicazione',
    },
    value: {
      applied: 'Applicata',
      approved: 'Approvata',
      notRecorded: 'Non registrato',
    },
    operations: {
      heading: 'Operazioni applicate',
    },
  },

  main: {
    tagstamp: 'PROPOSTA · DIFF',
    ledeFallback:
      'In attesa di decisione. Rivedi la modifica, la provenienza e gli effetti collaterali qui sotto prima di applicare.',
    dial: {
      caption: 'CONF',
      above: 'Sopra la tua soglia di applicazione',
      below: 'Sotto la tua soglia di applicazione',
      threshold: '(impostata {value} · Impostazioni)',
    },
    approvedBanner: {
      title: 'Approvata — non ancora applicata alla bacheca.',
      body: 'Manca un passo: premi ⏎ (o “{action}”) per scriverla sulla bacheca. Finché non lo fai non cambia niente.',
    },
    decisionReceipt: {
      approved: {
        title: 'Approvata — non ancora applicata alla bacheca.',
        body: 'La revisione resta qui. Scegli {action} quando vuoi modificare la bacheca.',
      },
      applied: {
        title: 'Applicata alla bacheca.',
        body: 'Questa proposta resta ispezionabile qui; ritrovala in Applicate di recente.',
      },
      rejected: {
        title: 'Rifiutata.',
        body: 'Questa proposta non è stata applicata e resta ispezionabile qui.',
      },
      deferred: {
        title: 'Rinviata.',
        body: 'Questa proposta tornerà in Revisione al termine del rinvio.',
      },
    },
    keyHint: {
      fileAway: 'PREMI ⌫ PER ARCHIVIARE',
      confirmApply: 'PREMI ⏎ PER APPLICARE ALLA BACHECA',
      approve: 'PREMI ⏎ PER APPROVARE · ⌫ PER RIFIUTARE',
    },
    footer: 'REVISIONE · {serial} · LOCAL-FIRST · REGISTRO',
  },

  decisionRail: {
    toolbar: {
      decision: 'Azioni di decisione',
      filing: 'Azioni di archiviazione',
    },
    stamp: {
      decision: 'DECISIONE',
      settled: 'CONCLUSA',
    },
    summary: {
      none: 'Niente da decidere ora',
      operations:
        '{count} operazione · revisione esplicita · applicazione atomica | {count} operazioni · revisione esplicita · applicazione atomica',
    },
    step: {
      approve: 'Passo 1 di 2 · approvare non cambia la bacheca',
      execute: 'Passo 2 di 2 · questo la scrive sulla bacheca',
    },
    reject: 'Rifiuta',
    requestEdit: 'Chiedi modifica',
    defer: 'Rimanda',
    apply: {
      approve: 'Approva',
      execute: 'Applica alla bacheca',
      approveLabel: 'Approva la proposta — passo 1 di 2, non cambia ancora la bacheca',
      executeLabel:
        'Applica alla bacheca — passo 2 di 2, scrive questa modifica sulla bacheca',
    },
    fileAway: {
      label: 'Archivia',
      ariaLabel: 'Archivia la proposta',
    },
    editLock: {
      editing: 'Stai modificando questa proposta qui sotto: le decisioni riprendono quando salvi o annulli la modifica.',
      saving: 'Salvataggio della modifica: le decisioni riprendono quando è completato.',
      cancel: 'Annulla modifica',
    },
  },

  change: {
    title: 'La modifica',
    subTitle: '{count} operazione · {board} | {count} operazioni · {board}',
    beforeEyebrow: 'Prima · oggi',
    afterEyebrow: 'Dopo · all’applicazione',
    fieldsHeading: 'Modifiche per campo',
    tag: {
      new: '· nuovo',
      kept: '· invariato',
    },
    before: {
      titleFallback: 'Nessuna proposta selezionata',
      bodyFallback: 'Rivedi {count} operazioni della proposta prima di applicare.',
      meta: '{board} · {source}',
      sourceFallback: 'proposta',
    },
    after: {
      noParameterPreview: 'Nessuna anteprima dei parametri per questa operazione.',
      noPreviewTitle: 'Nessuna anteprima delle operazioni',
      noPreviewBody: 'La proposta non includeva i dettagli delle operazioni.',
    },
    fields: {
      operationsKey: 'operazioni',
      none: 'nessuna',
      notProvided: 'non fornito',
    },
  },

  provenance: {
    title: 'Provenienza',
    sub: 'Cosa è stato letto · cosa no · cosa è stato dedotto',
    empty: 'Provenienza non ancora disponibile per questa proposta.',
    footnote: {
      deterministic:
        'Provenienza registrata: {label} — questa proposta è stata prodotta dall’estrattore deterministico offline di Taskdeck.',
      mock: 'Provenienza registrata: {label} — questa proposta è stata prodotta dal provider mock integrato di Taskdeck, non da un modello reale.',
      provider:
        'Provenienza registrata: {label} — questa proposta è stata prodotta dal provider AI che hai configurato, quindi il testo di origine è stato inviato a quel provider.',
    },
    viewAll: 'Vedi tutte le fonti lette →',
  },

  provenanceDrawer: {
    ariaLabel: 'Dettagli di provenienza',
    title: 'Provenienza',
    close: 'Chiudi il pannello di provenienza',
    meta: {
      model: 'Modello',
      confidence: 'Confidenza',
      confidenceValue: '{value}%',
      latency: 'Latenza',
      latencyValue: '{value}ms',
      promptVersion: 'Versione del prompt',
    },
    weight: {
      primary: 'Fonti primarie',
      contextual: 'Contestuali',
      inferred: 'Dedotte',
      excluded: 'Escluse',
    },
    evidenceTitle: 'Collegamenti alle prove',
    evidenceSpan: 'caratteri {start}–{end}',
    viewTranscript: 'Vedi nella trascrizione',
    hideTranscript: 'Nascondi la trascrizione',
    copyJson: 'Copia JSON',
    copied: 'Copiato!',
    copyFailed: 'Copia non riuscita',
    report: 'Segnala suggerimento sbagliato',
  },

  transcript: {
    title: 'Nella trascrizione',
    close: 'Chiudi',
    speaker: 'Interlocutore: {name}',
    loading: 'Caricamento della trascrizione…',
    unresolved: 'Questo intervallo di prova non corrisponde più alla trascrizione salvata.',
    error: {
      notFound: 'Questa trascrizione non è più disponibile.',
      unauthorized: 'Non hai eseguito l’accesso per vedere questa trascrizione.',
      generic: 'Non è stato possibile caricare la trascrizione. Riprova.',
    },
  },

  sideEffects: {
    title: 'Effetti collaterali',
    sub: 'Cosa arriva · cosa no · cosa viene archiviato',
    empty: 'Nessun effetto collaterale dichiarato.',
    riskEyebrow: 'Considerazioni sull’applicazione',
    fallback: {
      summary: 'Dettagli sul rischio non disponibili',
      description: 'Rivedi gli effetti collaterali dichiarati prima di applicare.',
    },
  },

  conflicts: {
    title: 'Conflitti e avvisi',
    sub: {
      clear: 'Cosa ha notato il sistema · tutto a posto',
      counted:
        'Cosa ha notato il sistema · {count} minore | Cosa ha notato il sistema · {count} elementi',
    },
    empty: 'Niente da segnalare.',
    tone: {
      warn: 'AVVISO',
      ok: 'A POSTO',
      info: 'INFO',
    },
  },

  history: {
    title: 'Cronologia · questa scheda',
    sub: 'Ogni passaggio dalla creazione',
    empty: 'Nessuna cronologia registrata.',
    status: {
      pending: 'IN ATTESA',
      applied: 'APPLICATA',
      past: 'passato',
      unknown: 'SCONOSCIUTO',
    },
  },

  author: {
    heading: 'Autore',
    breakdownHeading: 'Dettaglio della confidenza',
    nameFallback: 'Proposta',
    name: '{actor} · proposta da {source}',
    confidence: '{value} di confidenza',
    actor: {
      assistant: 'Assistente',
      capture: 'Cattura',
    },
    component: {
      operationSafety: 'Sicurezza delle operazioni',
    },
  },

  whyNow: {
    heading: 'Perché ora',
    noProposal: 'Nessuna proposta selezionata.',
    fallback: 'Questa proposta è in attesa di revisione in base alla fonte catturata con essa.',
  },

  similarPast: {
    heading: 'Decisioni simili passate',
    empty: 'Nessuna decisione passata comparabile.',
    verdict: {
      applied: 'APPLICATA',
      rejected: 'RIFIUTATA',
    },
    rateLabel: 'Tasso di applicazione su simili:',
    rateValue: '{applied} su {total} ({percent}%)',
  },

  keys: {
    heading: 'Decidi con i tasti',
    spaceKey: 'spazio',
    enter: {
      approve: 'Approva la proposta · passo 1 di 2',
      execute: 'Applica alla bacheca · passo 2 di 2',
    },
    edit: 'Chiedi modifica · apre il Composer',
    reject: 'Rifiuta · con motivo facoltativo',
    defer: 'Rimanda di 1h',
    provenance: 'Mostra o nascondi il pannello di provenienza',
    preview: 'Anteprima del diff nel dettaglio della scheda',
  },

  revisionEditor: {
    stamp: 'MODIFICA PRIMA DI APPROVARE',
    regionLabel: 'Modifica questa proposta prima di approvarla',
    jsonError: 'Inserisci un JSON valido prima di salvare.',
    reasonLabel: 'Motivo della modifica',
    reasonPlaceholder: 'Perché stai modificando questa proposta?',
    cancel: 'Annulla',
    save: 'Salva revisione',
    badge: '{count} revisione | {count} revisioni',
  },

  technical: {
    summary: 'Dettagli tecnici',
    copy: 'Copia i dettagli tecnici',
    copied: 'Copiato',
    ariaLabel: 'Dettagli tecnici della proposta',
  },

  diff: {
    serial: '§ DIFF',
    title: 'Dettagli delle operazioni',
    hint: 'Premi Spazio per nascondere',
    loading: 'Caricamento del diff…',
    storedBanner:
      '{status} · sola lettura — mostra l’anteprima salvata dell’invio originale.',
    revised: {
      lead: 'Questa proposta è stata',
      emphasis: 'revisionata',
      storedTail:
        'dopo l’invio — l’anteprima salvata mostra le operazioni originali, non quelle revisionate.',
      fallbackTail:
        'dopo l’invio — le operazioni registrate mostrano l’invio originale, non quello revisionato.',
    },
    liveCaveat: {
      lead: 'Questa anteprima riflette la tua ultima',
      emphasis: 'modifica salvata',
      tail: '— le operazioni revisionate, non la proposta originale.',
    },
    invalid: {
      line: '{reason} — l’applicazione rifiuterà questa proposta.',
      noOperations: 'Questa proposta non contiene operazioni da applicare',
    },
    storedEmpty: 'Non è disponibile alcuna anteprima salvata per questa proposta.',
    empty: 'Nessuna modifica da visualizzare per questa proposta.',
    storedAriaLabel: 'Anteprima salvata della proposta',
    liveAriaLabel: 'Diff delle operazioni della proposta',
    recordedAriaLabel: 'Operazioni registrate della proposta',
  },

  applyDialog: {
    title: 'Applicare alla bacheca?',
    lede: 'Approvata. Sulla bacheca non è ancora stato scritto niente — questo è il passo che la applica.',
    noSummary: 'Questa proposta non ha un riepilogo.',
    revisionNote:
      'Questa proposta è stata modificata — verrà applicata l’ultima revisione salvata, non le operazioni originali.',
    contentsWillApply: 'Verrà applicato il contenuto approvato di questa proposta.',
    operationsWillApply: 'Verrà applicata {count} operazione. | Verranno applicate {count} operazioni.',
    cancel: 'Non ancora',
    confirm: 'Applica alla bacheca',
  },

  rejectDialog: {
    title: 'Rifiutare questa proposta?',
    lede: 'Rifiutandola la proposta si chiude. Sulla bacheca non cambia niente.',
    noSummary: 'Questa proposta non ha un riepilogo.',
    reasonOptionalLabel: 'Motivo (facoltativo)',
    reasonRequiredLabel: 'Motivo (obbligatorio)',
    reasonPlaceholder: 'Perché non si va avanti?',
    requiredNote: 'Le proposte a rischio alto o critico richiedono un motivo registrato.',
    cancel: 'Tienila',
    confirm: 'Rifiuta la proposta',
  },

  empty: {
    eyebrow: 'Coda · {count} in attesa',
    title: 'Niente in attesa. Bene.',
    body: 'Quando l’assistente avrà qualcosa da proporre comparirà qui per la revisione.',
    loading: 'Caricamento delle proposte…',
    scoped: {
      title: 'Nessuna proposta in {scope}.',
      body: 'Questo elenco di revisione è limitato alla bacheca attiva. Mostra tutte le bacheche per ripristinare la coda completa.',
    },
    filtered: {
      title: 'Nessun risultato in {filter}.',
      body: 'Cambia filtro per rivedere le proposte ancora in attesa altrove nella coda.',
    },
    unavailable: {
      eyebrow: 'Proposta richiesta',
      title: 'Questa proposta non e disponibile.',
      body: 'La proposta {id} non e piu disponibile per la revisione. Potrebbe essere stata applicata, archiviata o rimossa.',
      return: 'Torna alla revisione',
    },
  },

  summary: {
    pendingReview: {
      label: 'In attesa di revisione',
      helper: 'Modifiche in attesa di una decisione esplicita.',
    },
    readyToExecute: {
      label: 'Pronte da eseguire',
      helper: 'Proposte approvate che ora possono arrivare sulle bacheche.',
    },
    captureLinked: {
      label: 'Collegate a una cattura',
      helper: 'Elementi di revisione arrivati dal ciclo dell’Inbox.',
    },
    applied: {
      label: 'Applicate',
      helper: 'Proposte già eseguite con successo.',
    },
  },

  status: {
    pendingReview: 'In attesa di revisione',
    approved: 'Approvata',
    applied: 'Applicata',
    rejected: 'Rifiutata',
    failed: 'Non riuscita',
    expired: 'Scaduta',
    dismissed: 'Archiviata',
  },

  statusInline: {
    pendingReview: 'in attesa di decisione',
    approved: 'approvata',
    applied: 'applicata',
    rejected: 'rifiutata',
    failed: 'non riuscita',
    expired: 'scaduta',
    dismissed: 'archiviata',
  },

  headerMeta: '{time} · {status}',

  toast: {
    approved: 'Proposta approvata per l’applicazione alla bacheca',
    approveFailed: 'Approvazione della proposta non riuscita',
    rejected: 'Proposta rifiutata',
    rejectFailed: 'Rifiuto della proposta non riuscito',
    rejectReasonRequired:
      'Il motivo del rifiuto è obbligatorio per le proposte a rischio alto e critico',
    snoozed: 'Rimandata di 1 ora — tornerà nella tua coda.',
    snoozeFailed: 'Rinvio della proposta non riuscito',
    applied: 'Proposta applicata alla bacheca',
    applyFailed: 'Applicazione della proposta alla bacheca non riuscita',
    dismissed: 'Proposta archiviata',
    dismissedRefreshing: 'Proposta rimossa dalla vista. Aggiornamento...',
    dismissFailed: 'Archiviazione della proposta non riuscita',
    nothingToClear: 'Nessuna proposta completata da eliminare.',
    cleared: 'Eliminata {count} proposta completata. | Eliminate {count} proposte completate.',
    clearFailed: 'Eliminazione delle proposte non riuscita',
    diffFailed: 'Caricamento del diff della proposta non riuscito',
    loadProposalFailed: 'Caricamento della proposta non riuscito',
    loadProposalsFailed: 'Caricamento delle proposte non riuscito',
    noLongerAvailable: 'Questa proposta non è più disponibile per te.',
    feedbackRecorded: 'Feedback registrato per questo suggerimento.',
    feedbackFailed: 'Registrazione del feedback non riuscita',
    noProposalToReport: 'Nessuna proposta selezionata da segnalare.',
    provenanceToggleUnwired:
      'Il pulsante della provenienza non è ancora collegato; la provenienza è mostrata qui sotto.',
    revisionBusyFileAway: 'Salva o annulla la revisione prima di archiviare questa proposta.',
    revisionBusyApply: 'Salva o annulla la revisione prima di applicare questa proposta.',
    revisionBusyReject: 'Salva o annulla la revisione prima di rifiutare questa proposta.',
    revisionBusyDefer: 'Salva o annulla la revisione prima di rimandare questa proposta.',
    notDismissableYet: 'Questa proposta è ancora attiva e non può ancora essere archiviata.',
    bulkBusy: 'Aspetta che l’azione in corso finisca prima di archiviarne altre.',
    notApplyable:
      'Questa proposta non è più utilizzabile. Aggiorna la revisione per vedere lo stato attuale.',
    revisionStateUnknown:
      'La cronologia delle revisioni non è disponibile, quindi questa proposta non può essere verificata per l’applicazione. Riprova.',
    zeroOpApproved:
      'Questa proposta non contiene operazioni — applicarla alla bacheca verrà rifiutato.',
    zeroOpPending:
      'Questa proposta non contiene operazioni da applicare — l’applicazione la rifiuterà. Rifiutala o archiviala.',
    notRejectable:
      'Questa proposta non può più essere rifiutata. Aggiorna la revisione per vedere lo stato attuale.',
    notEditable: 'Questa proposta non può più essere modificata.',
    notDeferrable: 'Questa proposta non può più essere rimandata.',
  },
}
