/**
 * Inbox surface — Italian. Glossary as in `./home.ts`.
 *
 * "Nib" and "Composer" are Taskdeck's own names for the two capture
 * affordances (ADR-0054 §3) and stay in English.
 *
 * `eyebrow` porta DUE conteggi con etichette distinte (#1974): "da smistare" è
 * la coda vera e propria — la stessa definizione del badge nella barra
 * laterale — mentre "catturati" è il totale e non va mai chiamato coda.
 * "Smistare" resta il verbo per triage, come in `./home.ts`.
 *
 * `eyebrow` è un messaggio plurale scelto su `{total}`: al singolare il
 * participio concorda ("1 catturato", non "1 catturati"). "Da smistare" è
 * invariabile e non richiede una forma propria.
 */
export default {
  eyebrow:
    'Inbox · superficie di cattura · {pending} da smistare · {total} catturato | Inbox · superficie di cattura · {pending} da smistare · {total} catturati',
  title: {
    lead: 'Cosa hai in mente,',
    emphasis: 'in breve?',
  },
  lede: 'Lascia qui il pensiero. Resta intatto finché non lo smisti tu. Niente arriva alla bacheca senza la tua approvazione.',
  history: {
    eyebrow: 'Archivio · cronologia catture · sola lettura',
    title: 'Cronologia delle catture archiviate',
    lede: 'Consulta le catture conservate per questa bacheca archiviata. Ripristina la bacheca prima di creare, modificare o smistare il lavoro.',
    tableTitle: 'Catture archiviate',
    empty: 'Non sono state trovate catture conservate per questa bacheca archiviata.',
    detail: {
      open: 'Mostra la cattura conservata completa',
      close: 'Nascondi la cattura conservata completa',
      title: 'Cattura conservata',
      loading: 'Caricamento della cattura conservata…',
      error: 'Non è stato possibile caricare la cattura conservata.',
      captured: 'Catturata',
      processed: 'Elaborata',
      board: 'Bacheca',
      triageRun: 'Esecuzione di smistamento',
      promptVersion: 'Versione del prompt',
      proposalLink: 'Apri il registro della decisione',
      noProposal: 'Da questa cattura non è stato creato alcun registro della decisione.',
      none: 'Non registrato',
    },
  },
  // Avviso di smistamento degradato (#2202). È una cautela su un successo, non
  // un errore. `reason` riporta testualmente la notifica del server. Il testo
  // non afferma MAI quale motore abbia prodotto il risultato: una delle
  // notifiche del server (recupero dopo un arresto anomalo) dichiara essa
  // stessa che l’autore è incerto (revisione della PR #2224; #2212).
  degraded: {
    label: 'Smistata senza una lettura del modello confermata',
    lead: 'Taskdeck non può confermare che il risultato sia stato prodotto dal modello. Il server ha riportato lo smistamento così:',
    reason: 'Segnalato: {reason}',
    reviewProposal: 'Se questa proposta è stata prodotta dall’estrattore deterministico offline, è un’ipotesi basata su schemi di testo e non una lettura del modello, e non porta collegamenti alle prove. Leggila con attenzione prima di applicarla.',
    reviewTriaged: 'Lo smistamento si è concluso senza proporre nulla. Potrebbe essere l’estrattore deterministico offline che non ha riconosciuto alcuno schema, non l’assenza di cose da fare: rileggi tu la cattura.',
    reviewConverted: 'Questa cattura è già stata applicata a una bacheca. Verifica le modifiche risultanti rispetto al testo della cattura, perché il risultato potrebbe non provenire da una lettura del modello.',
    action: 'Se il modello doveva essere eseguito, controlla le impostazioni del provider LLM.',
  },
  capture: {
    errorLead: 'Appunto non salvato. La bozza è ancora qui.',
    errorDetail: 'Dettagli: {reason}',
    errorDiagnosticsLabel: 'Diagnostica della richiesta',
    errorFallback: 'Riprova quando la connessione è disponibile.',
    metadataCompatibilityLead: 'Cattura salvata senza scadenza né etichette.',
    metadataCompatibilityDetail: 'Questa versione del server ha ignorato quei metadati. Non riprovare: la cattura è già nell’Inbox.',
  },
  scope: {
    board: 'Bacheca: {board}',
    boardAndColumn: 'Bacheca: {board} · Colonna: {column}',
    clear: 'Mostra tutte le catture',
  },
  empty: {
    scoped: 'Nessuna cattura in {scope}. Mostra tutte le catture per ripristinare l’Inbox completo.',
  },
  variantToggle: {
    label: 'Variante di cattura',
  },
  variant: {
    nib: 'Nib',
    composer: 'Composer',
  },
  boardPicker: {
    viewOnlyOption: '{name} · sola lettura',
    viewOnlyHint: 'Le bacheche in sola lettura richiedono un accesso in scrittura prima di poterci smistare qualcosa.',
  },
  triage: {
    boardPick: {
      loading: 'Caricamento delle bacheche…',
      loadFailed: 'Impossibile caricare le bacheche. Controlla la connessione e riprova.',
      retry: 'Riprova a caricare le bacheche',
      blocked: {
        noBoards: 'Ancora nessuna bacheca. Creane una e poi questa cattura potrà andarci.',
        noBoard: 'Scegli prima una bacheca. Chiedi all\'AI resta disattivato finché non ne selezioni una.',
        viewOnly: 'Quella bacheca è in sola lettura. Scegline una in cui puoi scrivere.',
      },
    },
    decision: {
      sending: 'Invio a Review…',
      keeping: 'Conservazione per dopo…',
      archiving: 'Archiviazione…',
      kept: 'Conservata per dopo. Chiedi all\'AI o archiviala quando vuoi.',
      archived: 'Archiviata. Non sono state create proposte né attività sulla bacheca.',
      nothingToPropose: 'Lo smistamento non ha trovato nulla da proporre — a Review non è arrivato nulla.',
      inReview: 'Inviata a Review — decidi lì.',
      applied: 'Applicata alla bacheca. Qui non resta altro da fare.',
      rejected: 'Rifiutata. Questa cattura non arriverà a Review.',
      failed: 'Analisi fallita, quindi nulla è arrivato a Review. Risolvi il problema, poi chiedi di nuovo all\'AI.',
    },
    tag: {
      state: 'Stato: {label}. Il punto in cui si trova ora questa cattura.',
      source: 'Origine: {label}. Come è arrivata questa cattura — non è uno stato.',
    },
    // Correzione del testo prima dello smistamento (GH-1951).
    //
    // `blocked.notEditable` dichiara il FATTO, non la causa: il server rifiuta
    // la modifica per più motivi e indicarne uno solo sarebbe un'ipotesi
    // presentata come spiegazione. "Ask AI", "Keep" e "Archive" restano in
    // inglese perché sono le etichette dei pulsanti su questa superficie.
    edit: {
      action: 'Modifica cattura',
      label: 'Testo della cattura',
      placeholder: 'Correggi il testo catturato…',
      hint: 'Sistema le parole prima che Ask AI trasformi tutto in una proposta. Il salvataggio cambia solo la cattura — da qui non arriva nulla a una bacheca.',
      loading: 'Caricamento del testo completo…',
      save: 'Salva modifiche',
      saving: 'Salvataggio…',
      cancel: 'Annulla',
      close: 'Chiudi',
      retry: 'Riprova',
      unknownReason: 'il server non ha indicato un motivo',
      loadFailed: 'Il testo completo della cattura non è stato caricato: {reason}',
      saveFailed: 'Le modifiche alla cattura non sono state salvate: {reason}',
      decisionBlocked: 'Concludi o annulla questa modifica prima di premere Ask AI, Keep o Archive.',
      metadata: {
        legend: 'Scadenza ed etichette',
        dueDate: 'Data di scadenza (facoltativa)',
        labels: 'Etichette (facoltative)',
        labelsPlaceholder: 'Scrivi il nome di un’etichetta esistente',
        addLabel: 'Aggiungi etichetta',
        removeLabel: 'Rimuovi {label}',
        hint: 'Aggiungi un nome di etichetta esistente alla volta con Enter. Rimuovi una voce per cancellarla, poi salva e premi di nuovo Ask AI per riprovare il triage. Le virgole restano parte del nome; qui non vengono create etichette.',
        unavailable: 'Questa API non ha restituito metadati modificabili. Salvare solo il testo manterrà la scadenza e le etichette già memorizzate.',
      },
      blocked: {
        notEditable: 'Il testo di questa cattura non è modificabile. Premi Ask AI, Keep o Archive così com\'è.',
        empty: 'Il testo non può essere vuoto. Scrivi qualcosa, oppure annulla per lasciare la cattura com\'era.',
        unchanged: 'Non è ancora cambiato nulla. Modifica il testo o i metadati, oppure annulla per lasciare la cattura com\'era.',
        editorOpen: 'Un\'altra cattura è aperta in modifica. Salva o annulla quella modifica: passare ora scarterebbe il testo scritto lì.',
        busyElsewhere: 'Un\'altra azione su una cattura si sta concludendo. Salva torna disponibile appena termina.',
      },
    },
  },
}
