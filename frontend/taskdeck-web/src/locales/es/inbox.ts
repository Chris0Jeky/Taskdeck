/**
 * Inbox surface — Spanish. Glossary as in `./home.ts`.
 *
 * "Nib" and "Composer" are Taskdeck's own names for the two capture
 * affordances (ADR-0054 §3) and stay in English.
 *
 * `title.lead` opens the question, so it carries the opening "¿".
 *
 * `eyebrow` lleva DOS recuentos con etiquetas distintas (#1974): "por
 * clasificar" es la cola de verdad — la misma definición que el badge de la
 * barra lateral y que `home.status.awaitingTriage` — y "capturadas" es el
 * total, que nunca se presenta como cola.
 *
 * `eyebrow` es un mensaje plural elegido por `{total}`: en singular el
 * participio concuerda ("1 capturada", no "1 capturadas"). "Por clasificar" es
 * invariable y no necesita forma propia.
 */
export default {
  eyebrow:
    'Inbox · superficie de captura · {pending} por clasificar · {total} capturada | Inbox · superficie de captura · {pending} por clasificar · {total} capturadas',
  title: {
    lead: '¿Qué tienes en mente,',
    emphasis: 'en dos palabras?',
  },
  lede: 'Suelta la idea. Se queda aquí, intacta, hasta que tú la clasifiques. Nada llega al tablero sin tu aprobación.',
  variantToggle: {
    label: 'Variante de captura',
  },
  variant: {
    nib: 'Nib',
    composer: 'Composer',
  },
  boardPicker: {
    viewOnlyOption: '{name} · solo lectura',
    viewOnlyHint: 'Los tableros de solo lectura necesitan acceso de escritura antes de poder clasificar nada en ellos.',
  },
  triage: {
    boardPick: {
      blocked: {
        noBoards: 'Todavía no hay tableros. Crea uno y esta captura podrá ir ahí.',
        noBoard: 'Elige primero un tablero. Accept on board sigue desactivado hasta que selecciones uno.',
        viewOnly: 'Ese tablero es de solo lectura. Elige uno en el que puedas escribir.',
      },
    },
    decision: {
      sending: 'Enviando a Review…',
      rejecting: 'Rechazando…',
      nothingToPropose: 'La clasificación no encontró nada que proponer — a Review no llegó nada.',
      inReview: 'Enviada a Review — decide allí.',
      applied: 'Aplicada al tablero. Aquí no queda nada por hacer.',
      rejected: 'Rechazada. Esta captura no llegará a Review.',
      failed: 'La clasificación falló, así que nada llegó a Review. Corrige el problema y pulsa Accept de nuevo.',
    },
    tag: {
      state: 'Estado: {label}. Dónde está ahora mismo esta captura.',
      source: 'Origen: {label}. Cómo llegó esta captura — no es un estado.',
    },
  },
}
