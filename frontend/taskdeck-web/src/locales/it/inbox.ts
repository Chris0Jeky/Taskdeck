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
  // Mostrato AL POSTO di `eyebrow` durante la sostituzione dell'ambito (#2501):
  // i conteggi apparterrebbero all'ambito appena lasciato. Nessun plurale: non
  // c'è alcun numero con cui concordare. E nessuna parola sul caricamento: la
  // tabella possiede stato di caricamento, errore e riprova.
  eyebrowUncounted: 'Inbox · superficie di cattura',
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
      openFor: 'Mostra la cattura conservata completa per {capture}',
      closeFor: 'Nascondi la cattura conservata completa per {capture}',
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
    // GH-2142 -- traduzione automatica (machine-translated).
    sessionExpiredReason: 'La sessione è scaduta prima che questa cattura venisse salvata.',
    draftRestoredLead: 'Bozza ripristinata.',
    draftRestoredDetail:
      'Il nuovo accesso ha interrotto questa cattura, quindi nulla è arrivato nell’Inbox. Inviala quando vuoi.',
    draftRestoredTruncated: 'Una parte di questa bozza era troppo lunga, quindi non è stata ripristinata per intero.',
    draftRestoredDiscard: 'Elimina questa bozza',
    errorDiagnosticsLabel: 'Diagnostica della richiesta',
    errorFallback: 'Riprova quando la connessione è disponibile.',
    metadataCompatibilityLead: 'Cattura salvata senza scadenza né etichette.',
    metadataCompatibilityDetail: 'Questa versione del server ha ignorato quei metadati. Non riprovare: la cattura è già nell’Inbox.',
    source: {
      legend: 'Origine',
      typed: 'Nota scritta',
      transcript: 'Trascrizione',
      transcriptNote:
        'Le catture di trascrizione vengono inviate all’assistente configurato per estrarre le attività. Le note scritte no.',
      tooLong: 'Questa trascrizione è troppo lunga. La lunghezza massima è di {max} caratteri.',
    },
  },
  // Etichette dei campi del Composer (#1871). "Testo" per `body`: è il testo
  // della cattura, come in `triage.edit.label`, e "Corpo" in italiano richiama
  // il corpo di un messaggio, non un appunto.
  //
  // Ogni nome accessibile (`*Aria`) inizia con l'etichetta visibile del campo e
  // poi dice cosa fa il controllo (WCAG 2.5.3, il modello della PR #2675). Se
  // si riscrive un'etichetta visibile va riscritto anche il suo nome
  // accessibile: `PaperCaptureComposer.spec.ts` verifica la relazione, non solo
  // le stringhe, e la verifica anche in italiano, quindi romperla qui fa
  // fallire il test.
  //
  // I segnaposto restano segnaposto: un suggerimento sulla FORMA del valore,
  // mai il nome del campo, che è quello che danno l'etichetta visibile e il
  // nome accessibile. `bodyPlaceholder` è una frase e ha l'iniziale maiuscola
  // per scelta; `labelsPlaceholder` è un frammento che prosegue il campo e resta
  // in minuscolo.
  composer: {
    eyebrow: 'Cattura · Bozza',
    meta: 'solo locale · salva nell’Inbox',
    footerBefore: 'Le catture arrivano nell’',
    footerInbox: 'Inbox',
    footerAfter: '. Collegarle a una bacheca crea una proposta, non una scheda.',
    submit: 'Cattura',
    bodyLabel: 'Testo',
    bodyAria: 'Testo: scrivi il contenuto di questa cattura',
    bodyPlaceholder: 'Il pensiero, in parole semplici…',
    labelsLabel: 'Etichette',
    labelsAria: 'Etichette: scrivi un’etichetta e premi Enter per aggiungerla',
    labelsPlaceholder: 'aggiungi e premi Enter',
    dueLabel: 'Scadenza (facoltativa)',
    dueAria: 'Scadenza (facoltativa): scegli quando scade questa cattura',
    attachmentsUnavailable: 'Gli allegati non vengono ancora salvati con le catture.',
  },
  nib: {
    eyebrow: 'Cattura rapida · {shortcut}',
    destinationWithBoard: 'Questa cattura arriva nell’Inbox, collegata a {board}, per il triage.',
    destinationWithoutBoard: 'Questa cattura arriva nell’Inbox senza bacheca, per il triage.',
    selectedBoard: 'la bacheca selezionata',
    submit: 'Cattura',
  },
  // `boardAndColumn` rimosso con #1984 (constatazione 2): l'elenco Inbox viene
  // richiesto per bacheca e senza colonna, quindi nominare una colonna qui
  // dichiarava un filtro mai applicato.
  scope: {
    board: 'Bacheca: {board}',
    clear: 'Mostra tutte le catture',
  },
  empty: {
    scoped: 'Nessuna cattura in {scope}. Mostra tutte le catture per ripristinare l’Inbox completo.',
  },
  // Aggiunto alla riga del conteggio durante un aggiornamento nello STESSO
  // ambito, con le righe ancora visibili e utilizzabili (#2501). Minuscolo:
  // segue un separatore "·".
  refreshing: 'aggiornamento…',
  variantToggle: {
    label: 'Variante di cattura',
  },
  variant: {
    nib: 'Nib',
    composer: 'Composer',
  },
  boardPicker: {
    // `label` sta sopra entrambi i selettori di bacheca. Entrambi i nomi
    // accessibili portano l'etichetta visibile per prima (WCAG 2.5.3) e si
    // distinguono solo per quello che dicono dopo.
    //
    // `composerAria` non può dire che la cattura ARRIVI sulla bacheca scelta:
    // ogni cattura arriva nell'Inbox, e la bacheca la COLLEGA soltanto perché il
    // triage possa proporre su di essa; niente arriva alla bacheca senza
    // approvare ed eseguire (ADR-0003). È quello che dicono il piè di pagina del
    // Composer e `nib.destination*`, quindi questo nome dice "collegare ... per
    // il triage" e quello della riga conserva "dove va questa cattura", che è il
    // compito di quel selettore.
    label: 'Bacheca',
    composerAria: 'Bacheca: scegli a quale bacheca collegare questa cattura per il triage',
    triageAria: 'Bacheca: scegli dove va questa cattura',
    noBoardOption: 'Nessuna bacheca · arriva nell’Inbox',
    selectPlaceholder: 'Seleziona una bacheca…',
    viewOnlyOption: '{name} · sola lettura',
    viewOnlyHint: 'Le bacheche in sola lettura richiedono un accesso in scrittura prima di poterci smistare qualcosa.',
  },
  triage: {
    // Nome della regione dell'elenco catture: dice cosa contiene la regione,
    // non quali catture, così resta stabile anche in sola lettura.
    tableAria: 'Elementi catturati',
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
    // Dove resta una correzione non salvata la cui cattura esce dall'elenco
    // (#1999, punto 3): un cambio del filtro bacheca, un aggiornamento che non
    // restituisce più la riga, oppure il passaggio allo storico in sola
    // lettura. `{capture}` è l'estratto della riga stessa.
    //
    // `kept` e `discarded` sono ricevute di un momento. `held`, `blocked` e
    // `heldUneditable` sono frasi valide finché restano a schermo, quindi
    // ognuna finisce dicendo che cosa può fare chi legge.
    //
    // `kept` dice "questo elenco" di proposito: la correzione vive nella
    // tabella finché la tabella esiste, e prometterla dopo un ricaricamento
    // sarebbe una promessa che questo meccanismo non può mantenere.
    draft: {
      kept: 'La correzione non salvata di “{capture}” non è andata persa. Resta conservata finché rimani su questo elenco Inbox e torna con quella cattura quando ricompare. Non è stato salvato nulla.',
      held: 'La correzione non salvata di “{capture}” è ancora conservata. Premi Modifica cattura su quella riga per riprenderla.',
      blocked: 'La correzione non salvata di “{capture}” è ancora conservata. Un\'altra cattura è aperta in modifica: concludi quella, poi premi Modifica cattura su questa riga per riprenderla.',
      heldUneditable: 'La correzione non salvata di “{capture}” è ancora conservata. Questo elenco non modifica una cattura che è {status}, quindi la correzione resta qui finché quella cattura non torna modificabile.',
      restored: 'La correzione non salvata di “{capture}” è di nuovo nell\'editor, sopra la cattura così com\'è ora. Salvala o annulla come sempre.',
      discarded: 'La correzione non salvata di “{capture}” è stata scartata: la cattura ora è {status} e il suo testo non è più modificabile. Non è stato salvato nulla.',
      dismiss: 'Chiudi queste note',
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
