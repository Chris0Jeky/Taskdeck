/**
 * Appearance / preferences surface (`views/AppearanceSettingsView.vue`) —
 * English source catalog.
 *
 * "Paper", "Paper Night" and "Legacy / Obsidian" are theme names — Taskdeck's
 * own coinages (ADR-0054 §3) — and stay in English in every locale; only the
 * surrounding qualifiers ("Light", "match system") are translated.
 *
 * Language display names are NOT here: they are endonyms held in
 * `src/i18n/index.ts` (`LOCALE_LABELS`), so every language names itself in its
 * own language whatever the active locale.
 */
export default {
  appearance: {
    eyebrow: 'Settings',
    title: 'Appearance',
    subtitle:
      'Choose how Taskdeck looks. Paper is the canonical theme; Off keeps the original Legacy (Obsidian) shell.',
    themeLabel: 'Theme',
    modes: {
      off: {
        label: 'Off (Legacy / Obsidian)',
        hint: 'The original Obsidian shell. Choosing this returns the whole interface to Legacy, not just the colours.',
      },
      paper: {
        label: 'Paper (Light)',
        hint: 'The canonical Paper theme — cream paper, ink, and a single ember accent.',
      },
      paperNight: {
        label: 'Paper Night (Dark)',
        hint: 'Paper after dark — the same layout in a low-light palette.',
      },
      auto: {
        label: 'Auto (match system)',
        hint: 'Follows your operating system’s light/dark preference and updates live when it changes.',
      },
    },
  },
  language: {
    label: 'Language',
    hint: 'Taskdeck is being translated one surface at a time. Anything not translated yet stays in English.',
  },
}
