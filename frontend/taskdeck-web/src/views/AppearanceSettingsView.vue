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
  <div class="td-appearance-settings">
    <h1 class="td-page-title">Appearance</h1>
    <p class="td-description">
      Choose how Taskdeck looks. Paper is the canonical theme; Off keeps the original Legacy (Obsidian) shell.
    </p>

    <section class="td-panel">
      <div class="td-section-title">Theme</div>
      <div class="td-theme-segments" role="group" aria-label="Theme">
        <button
          v-for="option in options"
          :key="option.mode"
          type="button"
          class="td-theme-segment"
          :class="{ 'td-theme-segment--active': activeMode === option.mode }"
          :aria-pressed="activeMode === option.mode"
          @click="selectMode(option.mode)"
        >
          {{ option.label }}
        </button>
      </div>
      <p class="td-theme-hint">{{ activeHint }}</p>
    </section>
  </div>
</template>

<style scoped>
.td-appearance-settings {
  max-width: 640px;
}

.td-description {
  color: var(--td-text-secondary);
  margin-bottom: var(--td-space-4);
}

.td-panel {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-5);
}

.td-section-title {
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-theme-segments {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

.td-theme-segment {
  flex: 1 1 auto;
  min-width: 140px;
  padding: var(--td-space-2) var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 1px solid var(--td-border-default);
  background: var(--td-surface-primary);
  color: var(--td-text-secondary);
  cursor: pointer;
  font: inherit;
}

.td-theme-segment:hover {
  border-color: var(--td-color-primary);
}

.td-theme-segment--active {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
  border-color: var(--td-color-primary);
  font-weight: 600;
}

.td-theme-hint {
  color: var(--td-text-secondary);
  margin: 0;
  min-height: 1.25rem;
}
</style>
