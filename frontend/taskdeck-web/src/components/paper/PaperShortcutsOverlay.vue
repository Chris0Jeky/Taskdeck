<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import PaperKbd from './PaperKbd.vue'

/**
 * PaperShortcutsOverlay — paper-card modal listing the full keyboard map.
 * Three-column reference card mirroring `ShortcutsSurface` in
 * `design_handoff_taskdeck_paper/paper/surface-misc.jsx`.
 *
 * AppShell owns the `?` toggle.  This overlay closes on Escape, backdrop, or
 * the close button.
 *
 * Three groups: Navigate / Capture & Review / Boards.  Each row is a kbd pill
 * paired with a short label, separated by a hairline soft-rule.
 */
const props = defineProps<{
  visible: boolean
}>()

const emit = defineEmits<{
  close: []
}>()

type ShortcutRow = {
  kbd: string
  label: string
  /** Optional mono-style note ("anywhere", "global", "during review"). */
  note?: string
}
type ShortcutGroup = { title: string; rows: ShortcutRow[] }

const groups: ShortcutGroup[] = [
  {
    title: 'Navigate',
    rows: [
      { kbd: 'H', label: 'Home', note: 'workspace' },
      { kbd: 'T', label: 'Today' },
      { kbd: 'B', label: 'Boards' },
      { kbd: 'I', label: 'Inbox' },
      { kbd: 'R', label: 'Review' },
      { kbd: '⌘K', label: 'Command palette', note: 'anywhere' },
      { kbd: 'G T', label: 'Go to Today' },
    ],
  },
  {
    title: 'Capture & Review',
    rows: [
      { kbd: 'Ctrl/Cmd+Shift+C', label: 'Quick capture', note: 'anywhere' },
      { kbd: '⏎', label: 'Apply / commit decision' },
      { kbd: '⌫', label: 'Reject / dismiss' },
      { kbd: 'E', label: 'Request edit' },
      { kbd: 'P', label: 'Provenance pane', note: 'during review' },
    ],
  },
  {
    title: 'Boards',
    rows: [
      { kbd: 'C', label: 'Capture here' },
      { kbd: 'A', label: 'Ask assistant' },
      { kbd: 'F', label: 'Filter' },
      { kbd: 'L', label: 'Labels' },
      { kbd: '1–9', label: 'Jump to column' },
      { kbd: 'J / K', label: 'Move between cards' },
      { kbd: 'O', label: 'Open card' },
    ],
  },
]

function handleGlobalKeydown(event: KeyboardEvent) {
  // Escape always closes when open, even from inside the overlay.
  if (props.visible && event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  }
}

onMounted(() => {
  window.addEventListener('keydown', handleGlobalKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleGlobalKeydown)
})

function onBackdropClick() {
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions, vuejs-accessibility/click-events-have-key-events -- modal backdrop with dialog role; Escape closes via the global keydown listener; click-to-close is standard modal UX -->
    <div
      v-if="visible"
      class="paper-shortcuts-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="paper-shortcuts-title"
      @click.self="onBackdropClick"
    >
      <div class="paper-shortcuts-overlay card-lift" data-paper-shortcuts>
        <header class="paper-shortcuts-overlay__header">
          <div>
            <div class="tk-eyebrow">Help · keyboard map</div>
            <h2 id="paper-shortcuts-title" class="tk-h2 paper-shortcuts-overlay__title">
              The full <em>keystroke ledger</em>
            </h2>
          </div>
          <button
            type="button"
            class="paper-shortcuts-overlay__close"
            aria-label="Close keyboard shortcuts"
            @click="emit('close')"
          >
            <PaperKbd>esc</PaperKbd>
          </button>
        </header>

        <div class="paper-shortcuts-overlay__grid">
          <section
            v-for="group in groups"
            :key="group.title"
            class="paper-shortcuts-overlay__group"
            :data-group="group.title"
          >
            <div class="tk-eyebrow paper-shortcuts-overlay__group-title">{{ group.title }}</div>
            <ul class="paper-shortcuts-overlay__rows">
              <li
                v-for="row in group.rows"
                :key="`${group.title}-${row.kbd}`"
                class="paper-shortcuts-overlay__row"
              >
                <span class="paper-shortcuts-overlay__row-kbd">
                  <PaperKbd>{{ row.kbd }}</PaperKbd>
                </span>
                <span class="paper-shortcuts-overlay__row-label">{{ row.label }}</span>
                <span v-if="row.note" class="paper-shortcuts-overlay__row-note">{{ row.note }}</span>
              </li>
            </ul>
          </section>
        </div>

        <footer class="paper-shortcuts-overlay__footer">
          <span class="tk-meta">Bindings are remappable · Settings → Keyboard</span>
          <span class="tk-meta">
            Press <PaperKbd>?</PaperKbd> at any time
          </span>
        </footer>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.paper-shortcuts-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(26, 24, 20, 0.2);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 60px;
  z-index: 60;
}

.paper-shortcuts-overlay {
  width: min(820px, calc(100vw - 32px));
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: 4px;
  font-family: var(--sans);
  color: var(--ink);
  overflow: hidden;
}

.paper-shortcuts-overlay__header {
  padding: 18px 24px;
  border-bottom: 1px solid var(--line);
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.paper-shortcuts-overlay__title {
  margin: 4px 0 0;
}

.paper-shortcuts-overlay__close {
  background: transparent;
  border: none;
  padding: 0;
  cursor: pointer;
}

.paper-shortcuts-overlay__grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.paper-shortcuts-overlay__group {
  padding: 16px 24px;
  border-right: 1px solid var(--line-soft);
}

.paper-shortcuts-overlay__group:last-child {
  border-right: none;
}

.paper-shortcuts-overlay__group-title {
  margin-bottom: 8px;
}

.paper-shortcuts-overlay__rows {
  list-style: none;
  margin: 0;
  padding: 0;
}

.paper-shortcuts-overlay__row {
  display: grid;
  grid-template-columns: 56px 1fr auto;
  align-items: center;
  gap: 10px;
  padding: 5px 0;
  border-bottom: 1px dashed var(--line-soft);
  font-size: 13px;
  color: var(--ink);
}

.paper-shortcuts-overlay__row:last-child {
  border-bottom: none;
}

.paper-shortcuts-overlay__row-label {
  color: var(--ink);
}

.paper-shortcuts-overlay__row-note {
  font-family: var(--mono);
  font-size: 10.5px;
  color: var(--mute);
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.paper-shortcuts-overlay__footer {
  padding: 10px 24px;
  border-top: 1px solid var(--line);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

@media (max-width: 720px) {
  .paper-shortcuts-overlay__grid {
    grid-template-columns: 1fr;
  }
  .paper-shortcuts-overlay__group {
    border-right: none;
    border-bottom: 1px solid var(--line-soft);
  }
}
</style>
