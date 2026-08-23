/**
 * Appearance / preferences surface — Italian. Glossary as in `./home.ts`.
 *
 * "Paper", "Paper Night", "Off", "Legacy" and "Obsidian" are theme names and
 * stay in English (ADR-0054 §3); only the qualifiers are translated.
 */
export default {
  appearance: {
    eyebrow: 'Impostazioni',
    title: 'Aspetto',
    subtitle:
      "Scegli l'aspetto di Taskdeck. Paper è il tema canonico; Off mantiene la shell originale Legacy (Obsidian).",
    themeLabel: 'Tema',
    modes: {
      off: {
        label: 'Off (Legacy / Obsidian)',
        hint: "La shell Obsidian originale. Sceglierla riporta a Legacy tutta l'interfaccia, non solo i colori.",
      },
      paper: {
        label: 'Paper (chiaro)',
        hint: 'Il tema Paper canonico — carta crema, inchiostro e un solo accento ember.',
      },
      paperNight: {
        label: 'Paper Night (scuro)',
        hint: 'Paper dopo il tramonto — lo stesso impianto in una palette per luce bassa.',
      },
      auto: {
        label: 'Auto (come il sistema)',
        hint: 'Segue la preferenza chiaro/scuro del tuo sistema operativo e si aggiorna appena cambia.',
      },
    },
  },
  language: {
    label: 'Lingua',
    hint: 'Taskdeck viene tradotto una superficie alla volta. Ciò che non è ancora tradotto resta in inglese. Le opzioni contrassegnate come "Traduzione automatica" non sono ancora state riviste da un madrelingua.',
    machineTranslated: 'Traduzione automatica',
  },
}
