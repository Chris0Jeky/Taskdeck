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
      blocked: {
        noBoards: 'Ancora nessuna bacheca. Creane una e poi questa cattura potrà andarci.',
        noBoard: 'Scegli prima una bacheca. Accept on board resta disattivato finché non ne selezioni una.',
        viewOnly: 'Quella bacheca è in sola lettura. Scegline una in cui puoi scrivere.',
      },
    },
    decision: {
      sending: 'Invio a Review…',
      rejecting: 'Rifiuto in corso…',
      nothingToPropose: 'Lo smistamento non ha trovato nulla da proporre — a Review non è arrivato nulla.',
      inReview: 'Inviata a Review — decidi lì.',
      applied: 'Applicata alla bacheca. Qui non resta altro da fare.',
      rejected: 'Rifiutata. Questa cattura non arriverà a Review.',
      failed: 'Smistamento fallito, quindi nulla è arrivato a Review. Risolvi il problema, poi premi di nuovo Accept.',
    },
    tag: {
      state: 'Stato: {label}. Il punto in cui si trova ora questa cattura.',
      source: 'Origine: {label}. Come è arrivata questa cattura — non è uno stato.',
    },
    // Correzione del testo prima dello smistamento (GH-1951).
    //
    // `blocked.notEditable` dichiara il FATTO, non la causa: il server rifiuta
    // la modifica per più motivi e indicarne uno solo sarebbe un'ipotesi
    // presentata come spiegazione. "Accept" e "Reject" restano in inglese
    // perché sono le etichette dei pulsanti su questa superficie.
    edit: {
      action: 'Modifica testo',
      label: 'Testo della cattura',
      placeholder: 'Correggi il testo catturato…',
      hint: 'Sistema le parole prima che Accept trasformi tutto in una proposta. Il salvataggio cambia solo la cattura — da qui non arriva nulla a una bacheca.',
      loading: 'Caricamento del testo completo…',
      save: 'Salva testo',
      saving: 'Salvataggio…',
      cancel: 'Annulla',
      close: 'Chiudi',
      retry: 'Riprova',
      unknownReason: 'il server non ha indicato un motivo',
      loadFailed: 'Il testo completo della cattura non è stato caricato: {reason}',
      saveFailed: 'Il testo non è stato salvato: {reason}',
      decisionBlocked: 'Concludi o annulla questa modifica prima di premere Accept o Reject.',
      blocked: {
        notEditable: 'Il testo di questa cattura non è modificabile. Premi Accept o Reject così com\'è.',
        empty: 'Il testo non può essere vuoto. Scrivi qualcosa, oppure annulla per lasciare la cattura com\'era.',
        unchanged: 'Non è ancora cambiato nulla. Modifica il testo, oppure annulla per lasciare la cattura com\'era.',
      },
    },
  },
}
