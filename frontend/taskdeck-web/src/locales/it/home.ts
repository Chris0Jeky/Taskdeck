/**
 * Home surface — Italian.
 *
 * Glossary (kept consistent across every Italian surface):
 *   board    → bacheca
 *   capture  → appunto (the thing you jot down), not "cattura"
 *   triage   → smistare / smistamento
 *   review   → rivedere / revisione
 *   Inbox    → left as "Inbox" (a Taskdeck surface name, not an email folder)
 *
 * Day-boundary contract (#1768): never describe the counters as belonging to a
 * particular day — no "da ieri", no "riportati", no "durante la notte".
 */
export default {
  eyebrow: 'Area di lavoro · {period}',
  period: {
    morning: 'mattina',
    afternoon: 'pomeriggio',
    evening: 'sera',
  },
  greeting: {
    morning: 'Buongiorno',
    afternoon: 'Buon pomeriggio',
    evening: 'Buonasera',
    anonymous: 'Ciao',
  },
  loading: "Carico il riepilogo dell'area di lavoro...",
  error: "Non è stato possibile caricare il riepilogo dell'area di lavoro.",
  lede: {
    awaitingReview: '{count} da rivedere',
    awaitingTriage: '{count} da smistare',
  },
  queue: {
    label: 'In coda per te',
    title: 'II · In coda per te',
    tagProposed: 'PROPOSTO',
    tagTriage: 'DA SMISTARE',
    triageCard: 'Smista {count} appunto | Smista {count} appunti',
    triageCardMore: 'Smista un appunto',
    triageMeta: 'inbox · in attesa di una decisione',
  },
  firstBoard: {
    title: 'Dai forma alla tua prima bacheca.',
    body: 'Parti da zero oppure riusa un flusso già pronto. La guida crea la bacheca e ti porta subito dentro.',
    cta: 'Avvia la configurazione guidata',
  },
  empty: 'Niente in attesa. Bene.',
  milestones: {
    eyebrow: 'III · Il tuo primo ciclo',
    title: "Dal pensiero all'azione di cui ti fidi",
    completeTitle: 'Il tuo primo ciclo è completo',
    progress: '{completed}/{total} completate',
    stepComplete: 'Completata',
    stepIncomplete: 'Non completata',
    expand: 'Mostra le tappe',
    collapse: 'Nascondi le tappe',
    dismiss: 'Nascondi',
    note: 'Queste tappe restano in questa area di lavoro; non vengono inviate come dati di analisi.',
  },
  capture: {
    label: 'Cattura rapida',
    inputLabel: 'Annota un pensiero',
    placeholder: 'Annota un pensiero...',
  },
}
