/**
 * Inbox surface — Spanish. Glossary as in `./home.ts`.
 *
 * "Nib" and "Composer" are Taskdeck's own names for the two capture
 * affordances (ADR-0054 §3) and stay in English.
 *
 * `title.lead` opens the question, so it carries the opening "¿".
 */
export default {
  eyebrow: 'Inbox · superficie de captura · {count} en cola',
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
}
