/**
 * Home surface — Spanish.
 *
 * Glossary (kept consistent across every Spanish surface):
 *   board    → tablero
 *   capture  → nota (the thing you jot down), not "captura"
 *   triage   → clasificar / clasificación
 *   review   → revisar / revisión
 *   Inbox    → left as "Inbox" (a Taskdeck surface name, not an email folder)
 *
 * Day-boundary contract (#1768): never describe the counters as belonging to a
 * particular day — no "de ayer", no "arrastradas", no "de anoche".
 */
export default {
  eyebrow: 'Espacio de trabajo · {period}',
  period: {
    morning: 'mañana',
    afternoon: 'tarde',
    evening: 'noche',
  },
  greeting: {
    morning: 'Buenos días',
    afternoon: 'Buenas tardes',
    evening: 'Buenas noches',
    anonymous: 'Hola',
  },
  loading: 'Cargando el resumen de tu espacio de trabajo...',
  error: 'No se pudo cargar el resumen del espacio de trabajo.',
  lede: {
    awaitingReview: '{count} por revisar',
    awaitingTriage: '{count} por clasificar',
  },
  queue: {
    label: 'En cola para ti',
    title: 'II · En cola para ti',
    tagProposed: 'PROPUESTO',
    tagTriage: 'CLASIFICAR',
    triageCard: 'Clasifica {count} nota | Clasifica {count} notas',
    triageCardMore: 'Clasifica una nota',
    triageMeta: 'inbox · pendiente de decisión',
  },
  firstBoard: {
    title: 'Dale forma a tu primer tablero útil.',
    body: 'Empieza en blanco o reutiliza un flujo ya hecho. La guía crea el tablero y te lleva directo a él.',
    cta: 'Iniciar la configuración guiada',
  },
  empty: 'Nada pendiente. Bien.',
  milestones: {
    eyebrow: 'III · Tu primer ciclo',
    title: 'Del pensamiento a la acción en la que confías',
    completeTitle: 'Tu primer ciclo está completo',
    progress: '{completed}/{total} completados',
    stepComplete: 'Completado',
    stepIncomplete: 'Sin completar',
    expand: 'Mostrar los hitos',
    collapse: 'Ocultar los hitos',
    dismiss: 'Ocultar',
    note: 'Estos hitos se quedan en este espacio de trabajo; no se envían como datos de analítica.',
  },
  capture: {
    label: 'Captura rápida',
    inputLabel: 'Anota una idea',
    placeholder: 'Anota una idea...',
  },
}
