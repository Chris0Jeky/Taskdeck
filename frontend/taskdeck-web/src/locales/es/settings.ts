/**
 * Appearance / preferences surface — Spanish. Glossary as in `./home.ts`.
 *
 * "Paper", "Paper Night", "Off", "Legacy" and "Obsidian" are theme names and
 * stay in English (ADR-0054 §3); only the qualifiers are translated.
 */
export default {
  appearance: {
    eyebrow: 'Ajustes',
    title: 'Apariencia',
    subtitle:
      'Elige cómo se ve Taskdeck. Paper es el tema canónico; Off mantiene la shell original Legacy (Obsidian).',
    themeLabel: 'Tema',
    modes: {
      off: {
        label: 'Off (Legacy / Obsidian)',
        hint: 'La shell Obsidian original. Elegirla devuelve toda la interfaz a Legacy, no solo los colores.',
      },
      paper: {
        label: 'Paper (claro)',
        hint: 'El tema Paper canónico — papel crema, tinta y un único acento ember.',
      },
      paperNight: {
        label: 'Paper Night (oscuro)',
        hint: 'Paper de noche — la misma disposición en una paleta de poca luz.',
      },
      auto: {
        label: 'Auto (según el sistema)',
        hint: 'Sigue la preferencia de claro/oscuro de tu sistema operativo y se actualiza en cuanto cambia.',
      },
    },
  },
  language: {
    label: 'Idioma',
    hint: 'Taskdeck se traduce superficie a superficie. Lo que aún no está traducido se queda en inglés. Las opciones marcadas como "Traducción automática" aún no han sido revisadas por un hablante nativo.',
    machineTranslated: 'Traducción automática',
  },
}
