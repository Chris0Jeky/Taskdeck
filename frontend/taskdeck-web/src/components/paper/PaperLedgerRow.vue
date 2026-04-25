<script setup lang="ts">
import PaperIcon from './PaperIcon.vue'
import PaperStatusPill from './PaperStatusPill.vue'
import type { PaperStatusKind } from './PaperStatusPill.vue'

/**
 * PaperLedgerRow — single row of the ledger UI used across review surfaces.
 * Mirrors the JSX `LedgerRow` in `paper/components.jsx`: serial / title /
 * meta / status / chevron, all separated by a hairline soft rule.  Emits
 * `open` so the parent can decide where the click leads.
 */
const props = withDefaults(
  defineProps<{
    idx: string | number
    title: string
    meta?: string
    status?: { kind: PaperStatusKind; label: string }
    /** Render as a link instead of a div if you want native focus. */
    interactive?: boolean
  }>(),
  { interactive: true },
)

const emit = defineEmits<{
  (event: 'open'): void
}>()

function onClick() {
  if (!props.interactive) return
  emit('open')
}

function onKeydown(e: KeyboardEvent) {
  if (!props.interactive) return
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    emit('open')
  }
}
</script>

<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- dynamic role="button" + tabindex when interactive; rule cannot follow the binding -->
  <div
    class="paper-ledger-row"
    :role="interactive ? 'button' : undefined"
    :tabindex="interactive ? 0 : undefined"
    @click="onClick"
    @keydown="onKeydown"
  >
    <span class="tk-serial paper-ledger-row__idx">{{ idx }}</span>
    <span class="paper-ledger-row__title">{{ title }}</span>
    <span class="tk-meta paper-ledger-row__meta">{{ meta ?? '' }}</span>
    <span class="paper-ledger-row__status">
      <PaperStatusPill v-if="status" :kind="status.kind">{{ status.label }}</PaperStatusPill>
      <span v-else class="paper-ledger-row__placeholder">—</span>
    </span>
    <span class="paper-ledger-row__chev"><PaperIcon name="chevronRight" /></span>
  </div>
</template>

<style scoped>
.paper-ledger-row {
  display: grid;
  grid-template-columns: 44px 1fr 200px 120px 24px;
  align-items: center;
  padding: 10px 14px;
  border-bottom: 1px solid var(--line-soft);
  font-family: var(--sans);
  font-size: 13px;
  color: var(--ink);
  background: transparent;
  transition: background 140ms cubic-bezier(0.2, 0.65, 0.25, 1);
}
.paper-ledger-row[role='button'] {
  cursor: pointer;
}
.paper-ledger-row[role='button']:hover,
.paper-ledger-row[role='button']:focus-visible {
  background: var(--paper-2);
}
.paper-ledger-row__title {
  color: var(--ink);
}
.paper-ledger-row__meta {
  color: var(--mute);
}
.paper-ledger-row__placeholder {
  color: var(--faint);
}
</style>
