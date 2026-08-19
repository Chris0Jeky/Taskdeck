<script setup lang="ts">
import { computed } from 'vue'
import { usePaperThemeStore, type PaperMode } from '../store/paperThemeStore'

const paperTheme = usePaperThemeStore()

interface ThemeOption {
  mode: PaperMode
  label: string
  hint: string
}

// Single source of truth for the four selectable modes. `off` is the Legacy
// (Obsidian) escape hatch: it removes the Paper body class so AppShell renders
// the classic `.td-*` shell, not just a light palette.
const options: ThemeOption[] = [
  {
    mode: 'off',
    label: 'Off (Legacy / Obsidian)',
    hint: 'The original Obsidian shell. Choosing this returns the whole interface to Legacy, not just the colours.',
  },
  {
    mode: 'paper',
    label: 'Paper (Light)',
    hint: 'The canonical Paper theme — cream paper, ink, and a single ember accent.',
  },
  {
    mode: 'paper-night',
    label: 'Paper Night (Dark)',
    hint: 'Paper after dark — the same layout in a low-light palette.',
  },
  {
    mode: 'auto',
    label: 'Auto (match system)',
    hint: 'Follows your operating system’s light/dark preference and updates live when it changes.',
  },
]

const activeMode = computed(() => paperTheme.mode)
const activeHint = computed(
  () => options.find((option) => option.mode === activeMode.value)?.hint ?? '',
)

function selectMode(mode: PaperMode) {
  // Pure UI: the store persists to localStorage and re-applies the body class.
  paperTheme.setMode(mode)
}
</script>

<template>
  <div class="paper-appearance">
    <header class="paper-appearance__hero">
      <span class="tk-eyebrow paper-appearance__eyebrow">Settings</span>
      <h1 class="tk-h1 paper-appearance__title">Appearance</h1>
      <p class="tk-lede paper-appearance__subtitle">
        Choose how Taskdeck looks. Paper is the canonical theme; Off keeps the original Legacy (Obsidian) shell.
      </p>
    </header>

    <section class="paper-appearance__panel">
      <div id="td-appearance-theme-label" class="tk-h3 paper-appearance__panel-title">Theme</div>
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
 * Tokens are defined under `.paper` / `.paper-night`, so var() fallbacks keep the
 * surface legible when the user picks Off and the Legacy shell renders it.
 */
.paper-appearance {
  display: flex;
  flex-direction: column;
  gap: var(--s-5, 20px);
  max-width: 640px;
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-appearance__hero {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-appearance__eyebrow {
  color: var(--mute, #6c6557);
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
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease),
    border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-appearance__segment:hover {
  background: var(--paper-2, #ebe5d8);
  border-color: var(--ink-2, #3a352d);
}

.paper-appearance__segment:focus-visible {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-appearance__segment--active {
  background: var(--ember, #a8421f);
  border-color: var(--ember, #a8421f);
  color: var(--td-on-ember, #fefaf6);
  font-weight: 600;
}

.paper-appearance__hint {
  margin: 0;
  min-height: 1.25rem;
  color: var(--mute, #6c6557);
  font-size: var(--t-sm, 12px);
}
</style>
