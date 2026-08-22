<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import TdDialog from '../ui/TdDialog.vue'
import { isInteractiveTarget } from '../../composables/useReviewKeymap'
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

const { t } = useI18n()

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
  return raw || t('review.applyDialog.noSummary')
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
    return t('review.applyDialog.contentsWillApply')
  }
  const n = operationCount.value
  return t('review.applyDialog.operationsWillApply', { count: n }, n)
})

// --- Enter-to-confirm (GH-1983) ------------------------------------------
//
// THE PROBLEM. Since GH-1942 this dialog opens BY ITSELF the moment approve
// returns. `TdDialog` focuses its container, not a button — deliberately — so a
// keyboard reviewer who pressed ⏎ to approve finds ⏎ does nothing here and must
// Tab first, on the one dialog they did not open by hand.
//
// WHY NOT FOCUS THE ACCEPT BUTTON. Focusing it is the menu-pattern norm, but it
// re-opens the keyboard half of the hazard GH-1942 closed for the pointer. That
// PR turned backdrop dismissal OFF because the dialog appears under a pointer
// that is still travelling, so the habitual second click lands on it. The
// keyboard equivalent is a held ⏎: with the accept focused, the key's auto-repeat
// would confirm an execute the reviewer never separately decided on. An arming
// delay was rejected too — any delay short enough not to feel broken is also
// short enough for a key held a beat too long to clear it, and it makes whether
// the board gets written a race with the reviewer's reflexes.
//
// WHAT THIS DOES. Container focus stays. The dialog binds ⏎ itself, with three
// guards, so the accept is reachable from the keyboard without ever being
// reachable by ACCIDENT:
//   1. `event.repeat` is ignored. Auto-repeat is how a HELD key presents, and
//      the press that approved is the only one that can still be down when this
//      opens — so this is the exact keyboard analogue of the backdrop guard.
//   2. It fires at most once per open. A second ⏎ against an already-consumed
//      dialog cannot re-dispatch.
//   3. Keystrokes from a control inside the dialog are left alone, so ⏎ on
//      "Not yet" still cancels rather than confirming.
//
// ADR-0003 is untouched: this is a second, deliberate human keystroke reaching
// the same accept the button reaches. Nothing auto-applies.
const bodyRef = ref<HTMLElement | null>(null)
let dialogEl: HTMLElement | null = null
let enterArmed = false

function onDialogKeydown(event: KeyboardEvent) {
  if (event.key !== 'Enter') return
  // Guard 1 — the approving press, still held.
  if (event.repeat) return
  if (event.isComposing) return
  if (props.busy) return
  // Guard 3 — a focused button owns its own Enter.
  if (isInteractiveTarget(event.target)) return
  // Guard 2 — one confirm per open.
  if (!enterArmed) return
  enterArmed = false
  event.preventDefault()
  // The dialog consumed this keystroke, so nothing behind it may also act on
  // it. Confirming closes the dialog synchronously (`confirmExecuteProposal`
  // clears the pending id before awaiting), which drops the review keymap's
  // `executeConfirmProposal === null` guard while this same event is still
  // propagating toward the window. MEASURED: end to end it is caught anyway,
  // because `handleExecuteProposal` sets the busy lock synchronously before its
  // first await and the keymap is `!busy`-gated too — so this stops a real
  // second dispatch only if that ordering ever changes. It is here to make the
  // dialog's containment its own property rather than a consequence of another
  // component's internals.
  event.stopPropagation()
  emit('confirm')
}

function detachDialogKeydown() {
  dialogEl?.removeEventListener('keydown', onDialogKeydown)
  dialogEl = null
  enterArmed = false
}

/**
 * Bound to the body element rather than to `open`, so the listener is attached
 * exactly when the dialog's DOM exists — no ordering assumption about when
 * TdDialog renders relative to this component's own watchers.
 */
watch(bodyRef, (el) => {
  detachDialogKeydown()
  if (!el) return
  dialogEl = el.closest<HTMLElement>('.td-dialog')
  if (!dialogEl) return
  enterArmed = true
  dialogEl.addEventListener('keydown', onDialogKeydown)
})

onUnmounted(detachDialogKeydown)
</script>

<template>
  <!-- Backdrop dismissal is OFF for this dialog, deliberately (GH-1942).
       It now opens BY ITSELF the instant approve returns, under a pointer that
       is still travelling: the habitual second click on the rail's primary
       button — the very habit GH-1942 exists to serve — lands on the backdrop
       that just appeared beneath the cursor and would throw the step away. The
       two deliberate exits remain: Escape and the "Not yet" button. -->
  <TdDialog
    :open="open"
    :title="$t('review.applyDialog.title')"
    :close-on-backdrop="false"
    @close="emit('cancel')"
  >
    <div ref="bodyRef" class="td-apply-confirm" data-testid="apply-confirm-dialog">
      <p class="td-apply-confirm__lede">
        {{ $t('review.applyDialog.lede') }}
      </p>
      <blockquote class="td-apply-confirm__summary" data-testid="apply-confirm-summary">
        {{ summary }}
      </blockquote>
      <p v-if="hasRevisions" class="td-apply-confirm__meta" data-testid="apply-confirm-revision">
        {{ $t('review.applyDialog.revisionNote') }}
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
        {{ $t('review.applyDialog.cancel') }}
      </button>
      <button
        type="button"
        class="td-btn td-btn--primary td-btn--sm"
        data-testid="apply-confirm-accept"
        :disabled="busy"
        @click="emit('confirm')"
      >
        {{ $t('review.applyDialog.confirm') }}
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
