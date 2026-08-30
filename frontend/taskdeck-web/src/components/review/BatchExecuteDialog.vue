<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import TdDialog from '../ui/TdDialog.vue'
import type { BatchExecuteReceiptRow } from '../../composables/useBatchExecuteProposals'

const props = defineProps<{
  open: boolean
  count: number
  busy?: boolean
  receipts: BatchExecuteReceiptRow[]
}>()

const emit = defineEmits<{
  (event: 'confirm'): void
  (event: 'close'): void
}>()

/**
 * Two phases in one dialog: confirm the apply, then read what the server actually did per proposal.
 * The receipts are the ONLY record of a partial outcome, so they replace the confirmation body
 * rather than disappearing behind a toast.
 */
const showingReceipts = computed(() => props.receipts.length > 0)

const appliedCount = computed(() => props.receipts.filter((r) => r.outcome === 'Applied').length)
const skippedCount = computed(() => props.receipts.filter((r) => r.outcome === 'Skipped').length)
const failedCount = computed(() => props.receipts.filter((r) => r.outcome === 'Failed').length)

const dialogAnchor = ref<HTMLElement | null>(null)
const doneButton = ref<HTMLButtonElement | null>(null)
let dialogEl: HTMLElement | null = null
let enterKeyHeld = false
let activeKeyupListening = false

function eventBelongsTo(button: HTMLButtonElement | null, event: KeyboardEvent): boolean {
  return event.target instanceof Node && button?.contains(event.target) === true
}

function consumeEnter(event: KeyboardEvent) {
  event.preventDefault()
  event.stopPropagation()
}

function onConfirmKeydown(event: KeyboardEvent) {
  if (event.key !== 'Enter' || event.isComposing) return
  // The first deliberate press may confirm. A repeat from that same physical
  // press must not submit again while the async result is still in flight.
  if (enterKeyHeld || event.repeat) {
    consumeEnter(event)
    return
  }
  enterKeyHeld = true
}

function onDialogKeydown(event: KeyboardEvent) {
  if (event.key !== 'Enter' || event.isComposing) return
  if (eventBelongsTo(doneButton.value, event) && (enterKeyHeld || event.repeat)) {
    // Confirm is replaced by focused Done when receipts arrive. Keep the held
    // key from activating that new button until the browser sends a real keyup.
    consumeEnter(event)
  }
}

function onActiveDialogKeyup(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.isComposing) enterKeyHeld = false
}

function detachDialogKeyListeners() {
  dialogEl?.removeEventListener('keydown', onDialogKeydown)
  if (activeKeyupListening) {
    window.removeEventListener('keyup', onActiveDialogKeyup, true)
    activeKeyupListening = false
  }
  dialogEl = null
  enterKeyHeld = false
}

// The status node exists in both dialog phases, so its closest dialog remains
// stable while Confirm is replaced by the receipt view and Done button. Keyup
// is captured at window because disabling Confirm can move focus to body before
// the physical Enter release. The global listener lives only with this open
// dialog anchor and is detached before any replacement is attached.
watch([() => props.open, dialogAnchor], ([isOpen, anchor]) => {
  detachDialogKeyListeners()
  if (!isOpen || !anchor) return
  dialogEl = anchor.closest<HTMLElement>('.td-dialog')
  if (!dialogEl) return
  dialogEl.addEventListener('keydown', onDialogKeydown)
  window.addEventListener('keyup', onActiveDialogKeyup, true)
  activeKeyupListening = true
})

onUnmounted(detachDialogKeyListeners)

// The confirmation button is removed when the async result arrives. Restore
// focus only for that real phase transition; later prop refreshes must not
// interrupt the reviewer or repeat an already-consumed receipt announcement.
watch(
  () => props.receipts.length,
  async (receiptCount, previousReceiptCount) => {
    if (previousReceiptCount !== 0 || receiptCount === 0) return
    await nextTick()
    doneButton.value?.focus()
  },
  { flush: 'post' },
)
</script>

<template>
  <TdDialog
    :open="open"
    :title="showingReceipts
      ? $t('review.batchExecute.dialog.receiptsTitle')
      : $t('review.batchExecute.dialog.title')"
    :description="showingReceipts
      ? $t('review.batchExecute.dialog.receiptsDescription', { count: receipts.length }, receipts.length)
      : $t('review.batchExecute.dialog.description', { count }, count)"
    :close-on-backdrop="false"
    @close="emit('close')"
  >
    <p
      ref="dialogAnchor"
      class="tk-meta batch-execute-receipt-summary"
      :class="{ 'batch-execute-receipt-summary--empty': !showingReceipts }"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      data-testid="batch-execute-receipt-summary"
    >{{ showingReceipts
      ? $t('review.batchExecute.dialog.receiptSummary', {
        applied: appliedCount,
        skipped: skippedCount,
        failed: failedCount,
      })
      : '' }}</p>

    <div v-if="!showingReceipts" data-testid="batch-execute-dialog">
      <p>{{ $t('review.batchExecute.dialog.body', { count }, count) }}</p>
      <p class="tk-meta" data-testid="batch-execute-partial-warning">
        {{ $t('review.batchExecute.dialog.partialWarning') }}
      </p>
    </div>

    <div v-else data-testid="batch-execute-receipts">
      <ul class="batch-execute-receipts__list">
        <li
          v-for="receipt in receipts"
          :key="receipt.proposalId"
          class="batch-execute-receipts__row"
          :class="`batch-execute-receipts__row--${receipt.outcome.toLowerCase()}`"
          :data-testid="`batch-execute-receipt-${receipt.proposalId}`"
        >
          <span class="batch-execute-receipts__outcome">
            {{ $t(`review.batchExecute.outcome.${receipt.outcome}`) }}
          </span>
          <span class="batch-execute-receipts__title">{{ receipt.title }}</span>
          <span
            v-if="receipt.outcome === 'Failed'"
            class="batch-execute-receipts__reason tk-meta"
            :data-testid="`batch-execute-reason-${receipt.proposalId}`"
          >{{ receipt.errorMessage || receipt.errorCode || $t('review.batchExecute.unknownReason') }}</span>
        </li>
      </ul>
    </div>

    <template #footer>
      <button
        v-if="!showingReceipts"
        type="button"
        class="td-btn td-btn--secondary td-btn--sm"
        data-testid="batch-execute-cancel"
        :disabled="busy"
        @click="emit('close')"
      >
        {{ $t('review.batchExecute.dialog.cancel') }}
      </button>
      <button
        v-if="!showingReceipts"
        type="button"
        class="td-btn td-btn--primary td-btn--sm"
        data-testid="batch-execute-confirm"
        :disabled="busy || count === 0"
        @keydown.enter="onConfirmKeydown"
        @click="emit('confirm')"
      >
        {{ $t('review.batchExecute.dialog.confirm', { count }, count) }}
      </button>
      <button
        v-else
        ref="doneButton"
        type="button"
        class="td-btn td-btn--primary td-btn--sm"
        data-testid="batch-execute-done"
        @click="emit('close')"
      >
        {{ $t('review.batchExecute.dialog.done') }}
      </button>
    </template>
  </TdDialog>
</template>

<style scoped>
.batch-execute-receipt-summary--empty {
  margin: 0;
}
.batch-execute-receipts__list {
  list-style: none;
  margin: 10px 0 0;
  padding: 0;
  max-height: 280px;
  overflow-y: auto;
}
.batch-execute-receipts__row {
  display: grid;
  grid-template-columns: 76px minmax(0, 1fr);
  gap: 4px 10px;
  padding: 7px 0;
  border-bottom: 1px solid var(--line-soft);
  align-items: baseline;
}
.batch-execute-receipts__outcome {
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--mute);
}
.batch-execute-receipts__row--applied .batch-execute-receipts__outcome {
  color: var(--ink);
  font-weight: 650;
}
.batch-execute-receipts__row--failed .batch-execute-receipts__outcome {
  color: var(--danger, #a3342b);
  font-weight: 650;
}
.batch-execute-receipts__title {
  min-width: 0;
  overflow-wrap: anywhere;
}
.batch-execute-receipts__reason {
  grid-column: 2;
  display: block;
}
</style>
