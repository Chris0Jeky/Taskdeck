/**
 * Inbox surface — Italian. Glossary as in `./home.ts`.
 *
 * "Nib" and "Composer" are Taskdeck's own names for the two capture
 * affordances (ADR-0054 §3) and stay in English.
 */
export default {
  eyebrow: 'Inbox · superficie di cattura · {count} in coda',
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
  },
}
