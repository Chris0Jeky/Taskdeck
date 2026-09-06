/**
 * Boards list surface — Italian. Glossary as in `./home.ts`.
 *
 * `card.created` receives an already-formatted date; "Creata" agrees with
 * "bacheca" (feminine).
 *
 * `error.timeout` and `error.cancelled` are the board store's boundary copy and
 * can appear on any board screen, not only this list — see `../en/boards.ts`.
 */
export default {
  eyebrow: 'Area di lavoro',
  title: 'Le mie bacheche',
  newBoard: '+ Nuova bacheca',
  create: {
    title: 'Crea una nuova bacheca',
    nameLabel: 'Nome della bacheca',
    namePlaceholder: 'Nome della bacheca',
    submit: 'Crea',
    cancel: 'Annulla',
  },
  loading: 'Carico le bacheche...',
  error: {
    retry: 'Riprova a caricare le bacheche',
    timeout:
      'La richiesta ha richiesto troppo tempo ed è stata interrotta. Controlla la connessione e riprova.',
    cancelled: 'La richiesta è stata interrotta prima di concludersi. Riprova.',
  },
  empty: {
    title: 'Nessuna bacheca',
    hint: 'Inizia creando una nuova bacheca.',
    cta: '+ Crea bacheca',
  },
  card: {
    openLabel: 'Apri la bacheca: {name}',
    noDescription: 'Nessuna descrizione',
    created: 'Creata il {date}',
  },
}
