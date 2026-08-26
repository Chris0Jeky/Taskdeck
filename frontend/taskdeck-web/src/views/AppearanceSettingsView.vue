<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { usePaperThemeStore, type PaperMode } from '../store/paperThemeStore'
import { useLocaleStore } from '../store/localeStore'
import { LOCALE_LABELS, MACHINE_TRANSLATED_LOCALES, type SupportedLocale } from '../i18n'

const { t } = useI18n()
const paperTheme = usePaperThemeStore()
const localeStore = useLocaleStore()

interface ThemeOption {
  mode: PaperMode
  label: string
  hint: string
}

// Static key map rather than a key assembled from `mode`: `paper-night` is not
// a valid identifier segment, and runtime-built keys are invisible to grep and
// fail silently (fallback is silent by design, ADR-0054 §5).
const MODE_KEYS: Record<PaperMode, { label: string; hint: string }> = {
  off: {
    label: 'settings.appearance.modes.off.label',
    hint: 'settings.appearance.modes.off.hint',
  },
  paper: {
    label: 'settings.appearance.modes.paper.label',
    hint: 'settings.appearance.modes.paper.hint',
  },
  'paper-night': {
    label: 'settings.appearance.modes.paperNight.label',
    hint: 'settings.appearance.modes.paperNight.hint',
  },
  auto: {
    label: 'settings.appearance.modes.auto.label',
    hint: 'settings.appearance.modes.auto.hint',
  },
}

const MODE_ORDER: PaperMode[] = ['off', 'paper', 'paper-night', 'auto']

// Single source of truth for the four selectable modes. `off` is the Legacy
// (Obsidian) escape hatch: it removes the Paper body class so AppShell renders
// the classic `.td-*` shell, not just a light palette.
//
// A computed, not a module const: the labels are now translated, so they have
// to re-resolve when the language changes rather than freezing at import time.
const options = computed<ThemeOption[]>(() =>
  MODE_ORDER.map((mode) => ({
    mode,
    label: t(MODE_KEYS[mode].label),
    hint: t(MODE_KEYS[mode].hint),
  })),
)

const activeMode = computed(() => paperTheme.mode)
const activeHint = computed(
  () => options.value.find((option) => option.mode === activeMode.value)?.hint ?? '',
)

function selectMode(mode: PaperMode) {
  // Pure UI: the store persists to localStorage and re-applies the body class.
  paperTheme.setMode(mode)
}

// ── Language ─────────────────────────────────────────────────────────────
//
// Same shape as the theme control, and the same persistence contract: the
// store writes localStorage and re-applies to the runtime (ADR-0054 §7). The
// option labels are ENDONYMS from a constant, not catalog keys — a Spanish
// speaker scans a language list for "Español", whatever language the UI is in.

const languageOptions = computed(() =>
  localeStore.available.map((locale) => ({
    locale,
    label: LOCALE_LABELS[locale],
    // #1770 / walkthrough e-7 (2026-08-23): unreviewed machine translations are
    // disclosed in the picker until a native speaker signs them off.
    machineTranslated: MACHINE_TRANSLATED_LOCALES.includes(locale),
  })),
)

const activeLocale = computed(() => localeStore.locale)

function selectLocale(locale: SupportedLocale) {
  localeStore.setLocale(locale)
}
</script>

<template>
  <div class="paper-appearance">
    <header class="paper-appearance__hero">
      <span class="tk-eyebrow paper-appearance__eyebrow">{{ $t('settings.appearance.eyebrow') }}</span>
      <h1 class="tk-h1 paper-appearance__title">{{ $t('settings.appearance.title') }}</h1>
      <p class="tk-lede paper-appearance__subtitle">
        {{ $t('settings.appearance.subtitle') }}
      </p>
    </header>

    <section class="paper-appearance__panel">
      <div id="td-appearance-theme-label" class="tk-h3 paper-appearance__panel-title">
        {{ $t('settings.appearance.themeLabel') }}
      </div>
      <!--
        Single-select segmented control. Kept as <button> + aria-pressed to match
        the project-wide convention (PaperStyleGuideView, Today/Review rails, etc.
        all use aria-pressed; no role="radiogroup" exists anywhere in the app). The
        group is labelled by the visible "Theme" heading via aria-labelledby.
      -->
      <div class="paper-appearance__segments" role="group" aria-labelledby="td-appearance-theme-label">
        <button
          v-for="option in options"
          :key="option.mode"
          type="button"
          class="paper-appearance__segment"
          :class="{ 'paper-appearance__segment--active': activeMode === option.mode }"
          :data-mode="option.mode"
          :aria-pressed="activeMode === option.mode"
          @click="selectMode(option.mode)"
        >
          {{ option.label }}
        </button>
      </div>
      <p class="paper-appearance__hint">{{ activeHint }}</p>
    </section>

    <!--
      Language. Same segmented-control idiom and the same aria-pressed
      convention as Theme above (no role="radiogroup" exists anywhere in this
      app). Switching applies immediately — the store writes localStorage and
      pushes the locale into the i18n runtime, so this page re-renders in the
      new language without a reload.
    -->
    <section class="paper-appearance__panel" data-testid="appearance-language">
      <div id="td-appearance-language-label" class="tk-h3 paper-appearance__panel-title">
        {{ $t('settings.language.label') }}
      </div>
      <div
        class="paper-appearance__segments"
        role="group"
        aria-labelledby="td-appearance-language-label"
      >
        <button
          v-for="option in languageOptions"
          :key="option.locale"
          type="button"
          class="paper-appearance__segment"
          :class="{ 'paper-appearance__segment--active': activeLocale === option.locale }"
          :data-locale="option.locale"
          :aria-pressed="activeLocale === option.locale"
          @click="selectLocale(option.locale)"
        >
          <!-- `lang` sits on the endonym span, not the button: the endonym is in
               the option's own language, while the machine-translated note below
               it is in the ACTIVE locale like the rest of the page. -->
          <span class="paper-appearance__segment-name" :lang="option.locale">{{
            option.label
          }}</span>
          <span
            v-if="option.machineTranslated"
            class="paper-appearance__segment-note"
            data-testid="mt-badge"
          >
            {{ $t('settings.language.machineTranslated') }}
          </span>
        </button>
      </div>
      <p class="paper-appearance__hint">{{ $t('settings.language.hint') }}</p>
    </section>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — AppearanceSettingsView ──
 *
 * Styled against the Paper token system (--paper, --ink, --ember families),
 * matching every other /workspace/settings/* view after #1779. This page was
 * previously held back in the Legacy/Obsidian `--td-*` palette on purpose; that
 * exception was retired by the #1779 ruling — the theme-control page wears the
 * same Paper chrome as its neighbours.
 *
 * There are no theme-preview swatches on this surface to exempt: the segmented
 * control is text-only (each mode is named, not colour-sampled), so nothing here
 * has to render a specific theme's palette to do its job. If a real preview
 * swatch is ever added it must render its own theme's colours literally and stay
 * outside this Paper-token styling.
 *
 * Tokens live under `.paper` / `.paper-night` in paper-tokens.css and are NOT
 * defined at :root, so once the user picks Off and the Legacy shell renders this
 * page, every var() resolves to its literal fallback. The substrate line on the
 * root — `background: var(--paper, #f3eee5)` painted alongside `color:
 * var(--ink, #1a1814)` — is what keeps the text legible in Legacy: without it
 * this page's own <h1> would land on AppShell's Obsidian `--td-surface-base`
 * (#131313) at ~1.05:1 the moment Off is selected. It is a no-op under `.paper`
 * / `.paper-night`, where `.td-shell--paper .td-content` already paints
 * `var(--paper)`.
 * Paper typography (the `tk-*` classes) is scoped as `.paper .tk-*` /
 * `.paper-night .tk-*` and intentionally does NOT render in Legacy mode — only
 * legibility is preserved there, not the Paper type ladder.
 */
.paper-appearance {
  display: flex;
  flex-direction: column;
  gap: var(--s-5, 20px);
  max-width: 640px;
  font-family: var(--sans, system-ui, sans-serif);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-appearance__hero {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-appearance__eyebrow {
  color: var(--mute, #635c4e);
}

.paper-appearance__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

.paper-appearance__subtitle {
  margin: 0;
  color: var(--ink-2, #3a352d);
}

/* ── Panel ── */

.paper-appearance__panel {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  padding: var(--s-5, 20px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-appearance__panel-title {
  font-size: var(--t-lg, 18px);
  color: var(--ink-deep, #0a0908);
}

/* ── Segmented control ── */

.paper-appearance__segments {
  /* Grid (not flex-wrap) so segments keep equal widths and wrap cleanly
     instead of stretching a lone item to full width on the next row. */
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: var(--s-2, 8px);
}

.paper-appearance__segment {
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  color: var(--ink-2, #3a352d);
  cursor: pointer;
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-appearance__segment-name {
  display: block;
}

/* Unreviewed machine-translation disclosure (#1770) — quiet, but present. */
.paper-appearance__segment-note {
  display: block;
  margin-top: 2px;
  font-size: var(--t-xs, 11px);
  color: var(--mute, #7a7264);
}

/* On the active (ember) segment the muted ink is ~1.09:1 against the ember
   fill — invisible. Inherit the segment's own on-ember pair instead; the
   slight opacity keeps it visually subordinate to the endonym while staying
   comfortably above 4.5:1. */
.paper-appearance__segment--active .paper-appearance__segment-note {
  color: var(--td-on-ember, #fefaf6);
  opacity: 0.9;
}

.paper-appearance__segment:not(.paper-appearance__segment--active):not(:disabled):hover {
  background: var(--paper-2, #ebe5d8);
  border-color: var(--ink-2, #3a352d);
}

.paper-appearance__segment:not(.paper-appearance__segment--active):not(:disabled):active {
  background: var(--paper-2, #ebe5d8);
  border-color: var(--ember, #a8421f);
  transform: translateY(1px);
}

.paper-appearance__segment--active {
  background: var(--ember, #a8421f);
  border-color: var(--ember, #a8421f);
  color: var(--td-on-ember, #fefaf6);
  font-weight: 600;
}

/* Active is a complete foreground/background pair. Re-state the pair for
   pointer states so a more-specific pseudo-class can never replace only one
   side of it, which was the contrast defect tracked by #2083. */
.paper-appearance__segment--active:not(:disabled):hover,
.paper-appearance__segment--active:not(:disabled):active {
  background: var(--ember, #a8421f);
  border-color: var(--ember, #a8421f);
  color: var(--td-on-ember, #fefaf6);
}

.paper-appearance__segment--active:not(:disabled):active {
  transform: translateY(1px);
}

.paper-appearance__segment:focus-visible {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 3px var(--ember-bloom, #a8421f1a);
}

.paper-appearance__segment:disabled {
  background: var(--paper-2, #ebe5d8);
  border-color: var(--line, #d8d0bf);
  color: var(--mute, #635c4e);
  cursor: default;
  transform: none;
}

.paper-appearance__segment:disabled .paper-appearance__segment-note {
  color: inherit;
  opacity: 1;
}

.paper-appearance__hint {
  margin: 0;
  min-height: 1.25rem;
  color: var(--mute, #635c4e);
  font-size: var(--t-sm, 12px);
}
</style>
