<script setup lang="ts">
import { computed } from 'vue'
import TdDialog from '../ui/TdDialog.vue'
import type { Proposal } from '../../types/automation'

/**
 * ApplyToBoardDialog — the phase-2 (execute) confirmation for the review loop.
 *
 * #1818: replaces the native `confirm('Apply this approved proposal to the
 * board now?')`. A native confirm cannot carry the proposal summary, is not
 * themed by either review surface, and is invisible to component specs. This
 * uses the app's own `TdDialog` idiom (focus trap + escape stack, #1407) and
 * states plainly that this is the step that finally touches the board.
 *
 * It changes NOTHING about the two-phase invariant (ADR-0003): approve and
 * execute are still two explicit API calls in that order, and execute still
 * carries its Idempotency-Key. Only the confirmation surface changed.
 */
const props = defineProps<{
  /** The proposal awaiting confirmation; null closes the dialog. */
  proposal: Proposal | null
  /** True while the execute call is in flight. */
  busy?: boolean
}>()

const emit = defineEmits<{
  (event: 'confirm'): void
  (event: 'cancel'): void
}>()

const open = computed(() => props.proposal !== null)

/**
 * Prefer the human-readable presentation summary the review surfaces already
 * show; fall back to the raw proposal summary so the dialog is never empty.
 */
const summary = computed(() => {
  const p = props.proposal
  if (!p) return ''
  const plain = p.presentation?.plainSummary?.trim()
  if (plain) return plain
  const raw = p.summary?.trim()
  return raw || 'This proposal has no summary.'
})

const operationCount = computed(() => props.proposal?.operations?.length ?? 0)

const operationLabel = computed(
  () => `${operationCount.value} ${operationCount.value === 1 ? 'operation' : 'operations'}`,
)
</script>

<template>
  <TdDialog
    :open="open"
    title="Apply to the board?"
    :close-on-backdrop="!busy"
    @close="emit('cancel')"
  >
    <div class="td-apply-confirm" data-testid="apply-confirm-dialog">
      <p class="td-apply-confirm__lede">
        This is the second and final step: it executes the approved proposal on your board.
        Nothing has been written to the board yet.
      </p>
      <blockquote class="td-apply-confirm__summary" data-testid="apply-confirm-summary">
        {{ summary }}
      </blockquote>
      <p class="td-apply-confirm__meta" data-testid="apply-confirm-operations">
        {{ operationLabel }} will be applied.
      </p>
    </div>

    <template #footer>
      <button
        type="button"
        class="td-btn td-btn--secondary td-btn--sm"
        data-testid="apply-confirm-cancel"
        @click="emit('cancel')"
      >
        Cancel
      </button>
      <button
        type="button"
        class="td-btn td-btn--primary td-btn--sm"
        data-testid="apply-confirm-accept"
        :disabled="busy"
        @click="emit('confirm')"
      >
        Apply to board
      </button>
    </template>
  </TdDialog>
</template>

<style scoped>
.td-apply-confirm {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-apply-confirm__lede {
  margin: 0;
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-apply-confirm__summary {
  margin: 0;
  padding: var(--td-space-3);
  border-inline-start: 3px solid var(--td-color-primary);
  background: var(--td-surface-container-high);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  color: var(--td-text-primary);
}

.td-apply-confirm__meta {
  margin: 0;
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}
</style>
