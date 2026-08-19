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
  /**
   * How many saved revisions the surface knows this proposal has, or
   * null/undefined when the surface does not track revision state (Legacy).
   * Load-bearing for the scope line below — NOT decoration.
   */
  revisionCount?: number | null
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

/**
 * #1830 round 2: the proposal's ORIGINAL operations are not necessarily what
 * gets applied. A saved revision is materialized server-side at execute time
 * (#1235, preview == apply), which is exactly why PaperReviewView.onApply lets
 * an Approved proposal with ZERO original operations through when it has a
 * revision. Saying "0 operations will be applied" there is materially wrong on
 * the one surface this dialog exists to make honest.
 */
const hasRevisions = computed(() => (props.revisionCount ?? 0) > 0)

/**
 * The count is only trustworthy when the applied content IS the original
 * operations list: no revision replaces it, and either the list is non-empty
 * or the surface authoritatively told us the revision count is 0. An unknown
 * revision count with an empty list (the Legacy surface, which does not track
 * revisions) gets copy without a number rather than a possibly-wrong "0".
 */
const showOperationCount = computed(
  () =>
    !hasRevisions.value &&
    (operationCount.value > 0 || typeof props.revisionCount === 'number'),
)

/**
 * No number is claimed for the revision case: the only revision data the client
 * holds is the raw `revisedPayload` JSON, and re-deriving a count from it would
 * be a client-side guess at what the backend will materialize. Honest copy
 * without a number beats a number we cannot stand behind.
 */
const operationLabel = computed(() => {
  if (!showOperationCount.value) {
    return 'The approved contents of this proposal will be applied.'
  }
  const n = operationCount.value
  return `${n} ${n === 1 ? 'operation' : 'operations'} will be applied.`
})
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
      <p v-if="hasRevisions" class="td-apply-confirm__meta" data-testid="apply-confirm-revision">
        This proposal was edited — its latest saved revision is what will be applied, not the
        original operations.
      </p>
      <p v-else class="td-apply-confirm__meta" data-testid="apply-confirm-operations">
        {{ operationLabel }}
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
