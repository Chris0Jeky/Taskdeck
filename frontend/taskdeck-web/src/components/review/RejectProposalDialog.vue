<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import TdDialog from '../ui/TdDialog.vue'
import type { Proposal } from '../../types/automation'

/**
 * RejectProposalDialog — collects the optional rejection reason for the review
 * loop.
 *
 * GH-1969: replaces the native `window.prompt('Optional rejection reason:')`,
 * the last browser dialog in the decision flow and a sibling of the `confirm()`
 * #1818 removed from the apply path. A native prompt is unstyled by Paper and
 * Paper Night, is not translated, cannot be exercised by the dialog specs, and
 * is suppressed outright in some embedded and automation contexts — where the
 * reason would be silently lost. The reason is decision-ledger content, so it
 * gets the same `TdDialog` treatment (focus trap + shared escape stack, #1407)
 * as every other confirmation on this surface.
 *
 * The semantics are unchanged: the reason stays OPTIONAL for Low/Medium risk
 * (confirming with an empty box still rejects, and an all-whitespace reason is
 * still stored as no reason at all), and stays REQUIRED for High/Critical.
 *
 * FOCUS. Focus stays on TdDialog's container, as it does for every other dialog
 * in the app — the shared primitive does that deliberately, so a keystroke that
 * opened a dialog cannot carry through into its accept button. The reason field
 * is the first focusable element inside, so one Tab reaches it.
 */
const props = defineProps<{
  /** The proposal awaiting a rejection reason; null closes the dialog. */
  proposal: Proposal | null
  /** True while a decision call is in flight. */
  busy?: boolean
  /** High/Critical risk: the reason is mandatory rather than optional. */
  requiresReason?: boolean
}>()

const emit = defineEmits<{
  (event: 'confirm', reason: string): void
  (event: 'cancel'): void
}>()

const { t } = useI18n()

const open = computed(() => props.proposal !== null)
const reason = ref('')

/**
 * Clear on every transition rather than only on close: a reason typed for one
 * proposal must never appear pre-filled against the next one, whichever way the
 * dialog was left.
 */
watch(open, () => {
  reason.value = ''
})

const trimmedReason = computed(() => reason.value.trim())

/**
 * A required reason must be non-blank BEFORE the accept is offered — the old
 * prompt collected the empty string and then rejected it with an error toast,
 * which spent a round trip of the reviewer's attention to say what the button
 * can say by being unavailable.
 */
const canConfirm = computed(
  () => !props.busy && (!props.requiresReason || trimmedReason.value.length > 0),
)

const summary = computed(() => {
  const p = props.proposal
  if (!p) return ''
  const plain = p.presentation?.plainSummary?.trim()
  if (plain) return plain
  const raw = p.summary?.trim()
  return raw || t('review.rejectDialog.noSummary')
})

function onConfirm() {
  if (!canConfirm.value) return
  emit('confirm', trimmedReason.value)
}
</script>

<template>
  <!-- Backdrop dismissal is OFF: the body holds text the reviewer typed, and a
       stray click on the backdrop would throw it away with no undo. The two
       deliberate exits remain — Escape (via the shared escape stack) and the
       Cancel button. -->
  <TdDialog
    :open="open"
    :title="$t('review.rejectDialog.title')"
    :close-on-backdrop="false"
    @close="emit('cancel')"
  >
    <div class="td-reject-confirm" data-testid="reject-dialog">
      <p class="td-reject-confirm__lede">
        {{ $t('review.rejectDialog.lede') }}
      </p>
      <blockquote class="td-reject-confirm__summary" data-testid="reject-dialog-summary">
        {{ summary }}
      </blockquote>
      <label class="td-reject-confirm__label" for="reject-reason">
        {{
          requiresReason
            ? $t('review.rejectDialog.reasonRequiredLabel')
            : $t('review.rejectDialog.reasonOptionalLabel')
        }}
      </label>
      <textarea
        id="reject-reason"
        v-model="reason"
        class="td-reject-confirm__input"
        rows="3"
        :placeholder="$t('review.rejectDialog.reasonPlaceholder')"
        data-testid="reject-dialog-reason"
      />
      <p
        v-if="requiresReason"
        class="td-reject-confirm__meta"
        data-testid="reject-dialog-required-note"
      >
        {{ $t('review.rejectDialog.requiredNote') }}
      </p>
    </div>

    <template #footer>
      <button
        type="button"
        class="td-btn td-btn--secondary td-btn--sm"
        data-testid="reject-dialog-cancel"
        @click="emit('cancel')"
      >
        {{ $t('review.rejectDialog.cancel') }}
      </button>
      <button
        type="button"
        class="td-btn td-btn--danger td-btn--sm"
        data-testid="reject-dialog-accept"
        :disabled="!canConfirm"
        @click="onConfirm"
      >
        {{ $t('review.rejectDialog.confirm') }}
      </button>
    </template>
  </TdDialog>
</template>

<style scoped>
.td-reject-confirm {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-reject-confirm__lede {
  margin: 0;
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-reject-confirm__summary {
  margin: 0;
  padding: var(--td-space-3);
  border-inline-start: 3px solid var(--td-color-primary);
  background: var(--td-surface-container-high);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  color: var(--td-text-primary);
}

.td-reject-confirm__label {
  font-size: var(--td-font-xs);
  font-weight: 600;
  color: var(--td-text-secondary);
}

.td-reject-confirm__input {
  width: 100%;
  padding: var(--td-space-2);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-container-high);
  color: var(--td-text-primary);
  font: inherit;
  font-size: var(--td-font-sm);
  resize: vertical;
}

.td-reject-confirm__meta {
  margin: 0;
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}
</style>
