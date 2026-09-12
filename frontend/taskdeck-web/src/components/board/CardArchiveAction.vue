<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import TdDialog from '../ui/TdDialog.vue'
import CardDetachList from './CardDetachList.vue'
import { cardsApi } from '../../api/cardsApi'
import { useBoardStore } from '../../store/boardStore'
import { useToastStore } from '../../store/toastStore'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import type { Card, CardDetachPreview } from '../../types/board'

const PREVIEW_FAILURE = 'Could not load every affected child. Refresh before archiving.'
const CHANGE_FAILURE = 'The card state could not be confirmed. Refresh before trying again.'

/*
 * `archived` is an optional override for `card.isArchived`. A host can hold the
 * card as a snapshot that never learns the card was archived — `CardModal`
 * keeps its editor open over a completed archive when a draft would otherwise
 * be lost (#2969) — and this control must never label or send an operation from
 * a state it knows is stale. Hosts that pass a live card omit it, and the
 * explicit `undefined` default is what keeps "omitted" distinguishable from
 * "false": Vue casts an absent Boolean prop to `false` unless a default is
 * declared, which would have told every existing caller its card is active.
 */
/*
 * `canWrite` is the same kind of optional override for this control's own board-permission
 * read (#3028). A host that has already resolved the caller's write permission server-side —
 * `CardModal`, whose card editor asks once for all four of its write gates — passes the
 * answer, so a board payload that omits the optional `canWrite` field no longer reads as "no"
 * here while the editor's type selector says yes. Hosts that have not resolved it omit the
 * prop and keep the payload-derived answer; the `undefined` default is what keeps "omitted"
 * distinguishable from "false", exactly as for `archived` above.
 */
const props = withDefaults(defineProps<{ card: Card; disabled?: boolean; archived?: boolean; canWrite?: boolean }>(), {
  archived: undefined,
  canWrite: undefined,
})
const emit = defineEmits<{ changed: []; refresh: []; 'permission-denied': [] }>()
const boardStore = useBoardStore()
const toast = useToastStore()
const preview = ref<CardDetachPreview | null>(null)
// Switching cards resets presentation, not submitted requests. Each card stays
// busy until its own request settles, while unrelated cards remain actionable.
const pendingCards = ref(new Set<string>())
const cardKey = computed(() => JSON.stringify([props.card.boardId, props.card.id]))
const busy = computed(() => pendingCards.value.has(cardKey.value))
const error = ref<string | null>(null)
// True once an in-dialog refresh has replaced a failed child list. It keeps the
// recovery control mounted across the error -> refreshed transition so keyboard
// focus is never dropped onto `document.body`.
const refreshed = ref(false)
const dialogRecoveryButton = ref<HTMLButtonElement | null>(null)
const pageRecoveryButton = ref<HTMLButtonElement | null>(null)
// Context ownership survives neither card switches nor unmount. Dialog ownership
// additionally ends on dismissal; closing a dialog does not cancel a submitted write.
let contextGeneration = 0
let confirmationGeneration = 0
let unmounted = false
watch([() => props.card.boardId, () => props.card.id], () => {
  contextGeneration++
  confirmationGeneration++
  preview.value = null
  error.value = null
  refreshed.value = false
}, { flush: 'sync' })
onBeforeUnmount(() => {
  unmounted = true
  contextGeneration++
  confirmationGeneration++
})
const archived = computed(() => props.archived ?? props.card.isArchived === true)
const confirming = computed(() => preview.value !== null)
const allowed = computed(() => props.canWrite ?? (boardStore.currentBoard?.id === props.card.boardId
  && boardStore.currentBoard.canWrite === true && !boardStore.currentBoard.isArchived))

// The control that owns the failure is the one that must receive focus: while the
// confirmation is open that is the in-dialog Refresh, so recovery stays inside the
// active `aria-modal` element and inside its Tab cycle. Never awaited from a `catch`
// — the `finally` that clears `busy` has to run first or the target is still
// `disabled` when `focus()` lands on it.
async function focusRecovery(context = contextGeneration, confirmation = confirmationGeneration) {
  await nextTick()
  if (context !== contextGeneration) return
  if (confirmation !== confirmationGeneration) {
    // Dismissal can leave focus on body because the pending write disabled the
    // opener. Rescue that dead end, but never reclaim a usable new focus target.
    const active = document.activeElement
    const hasUsableFocus = active instanceof HTMLElement
      && active !== document.body && active !== document.documentElement
      && active.isConnected && !active.matches(':disabled, [aria-disabled="true"]')
      && !active.closest('[inert]')
    if (confirming.value || hasUsableFocus) return
  }
  const target = confirming.value ? dialogRecoveryButton.value : pageRecoveryButton.value
  target?.focus()
}

async function requestChange() {
  if (archived.value) return change()
  if (!allowed.value || props.disabled || busy.value || error.value) return
  const context = contextGeneration
  const confirmation = confirmationGeneration
  const requestKey = cardKey.value
  pendingCards.value.add(requestKey)
  refreshed.value = false
  try {
    const fresh = await cardsApi.previewDetach(props.card.boardId, props.card.id)
    if (context !== contextGeneration || confirmation !== confirmationGeneration) return
    preview.value = fresh
  } catch (e) {
    if (context !== contextGeneration || confirmation !== confirmationGeneration) return
    error.value = getErrorDisplay(e, PREVIEW_FAILURE).message
    void focusRecovery(context, confirmation)
  } finally {
    pendingCards.value.delete(requestKey)
  }
}

// In-dialog recovery. It re-reads the child list and the expected-state tokens the
// confirmation is built on; it deliberately does NOT emit `refresh`, because every
// parent treats that as "this surface is gone" (CardModal closes the card, the
// archive history remounts this component), which would destroy the dialog the
// recovery is required to stay inside. Nothing is retried here: a successful
// refresh only re-enables Confirm for an explicit second press.
async function refreshChildren() {
  if (busy.value || !confirming.value) return
  const context = contextGeneration
  const confirmation = confirmationGeneration
  const requestKey = cardKey.value
  pendingCards.value.add(requestKey)
  try {
    const fresh = await cardsApi.previewDetach(props.card.boardId, props.card.id)
    if (context !== contextGeneration || confirmation !== confirmationGeneration) return
    preview.value = fresh
    error.value = null
    refreshed.value = true
  } catch (e) {
    if (context !== contextGeneration || confirmation !== confirmationGeneration) return
    error.value = getErrorDisplay(e, PREVIEW_FAILURE).message
    void focusRecovery(context, confirmation)
  } finally {
    pendingCards.value.delete(requestKey)
  }
}

// Escape, the backdrop and Cancel all land here. It stays unconditional: a request
// that never settles must not be able to trap a keyboard user inside the modal.
function closeConfirmation() {
  confirmationGeneration++
  preview.value = null
  refreshed.value = false
  // An unresolved failure survives the close, so its alert and Refresh re-render on
  // the page. The opener is disabled while that failure stands and cannot take focus
  // back from TdDialog's restore, so point focus at the control that can act.
  if (error.value) void focusRecovery()
}

async function change() {
  if (!allowed.value || props.disabled || busy.value || error.value) return
  const context = contextGeneration
  const confirmation = confirmationGeneration
  const archive = !archived.value
  const requestKey = cardKey.value
  pendingCards.value.add(requestKey)
  try {
    await boardStore.setCardArchived(props.card.boardId, props.card.id, archive, preview.value?.expectedUpdatedAt ?? props.card.updatedAt, preview.value?.expectedChildrenFingerprint)
    if (context !== contextGeneration) return
    preview.value = null
    refreshed.value = false
    // Escape dismisses the confirmation, not the already-submitted operation.
    // Preserve its completion receipt for the same card without reopening the dialog.
    emit('changed')
  } catch (e) {
    if (context !== contextGeneration) {
      // A switched card must not inherit this failure. Keep a neutral receipt
      // without old-card content, parent events, or changes to the new editor.
      if (!unmounted) toast.error(`The ${archive ? 'archive' : 'restore'} requested for a previously viewed card could not be confirmed. Reopen that card and refresh its state before trying again.`)
      return
    }
    if ((e as { response?: { status?: number } } | null)?.response?.status === 403) emit('permission-denied')
    error.value = getErrorDisplay(e, CHANGE_FAILURE).message
    refreshed.value = false
    // After dismissal, rescue lost focus without stealing it from another control.
    void focusRecovery(context, confirmation)
  } finally {
    pendingCards.value.delete(requestKey)
  }
}
</script>

<template>
  <div class="my-3 space-y-2">
    <p v-if="!allowed" class="text-sm text-on-surface-variant">Archive and restore require an active board with edit access.</p>
    <p v-else-if="disabled" class="text-sm text-on-surface-variant">Save or discard your changes before archiving.</p>
    <button type="button" class="rounded border border-outline-variant/40 px-3 py-2 text-sm disabled:opacity-40"
      :disabled="!allowed || disabled || busy || !!error" @click="requestChange">
      {{ busy ? 'Saving…' : archived ? 'Restore card' : 'Archive card' }}
    </button>
    <!-- Page-level failure: only while no confirmation is open. A failure raised by
         the open confirmation is rendered inside it instead (see below), never
         underneath it where the active modal hides it from keyboard and
         screen-reader users. -->
    <div v-if="error && !confirming" role="alert" class="text-sm text-error">
      {{ error }}
      <button ref="pageRecoveryButton" type="button" class="ml-2 underline" @click="emit('refresh')">Refresh card state</button>
    </div>
    <TdDialog :open="confirming" title="Archive card?" description="Review every child detachment before confirming." @close="closeConfirmation">
      <CardDetachList v-if="preview" :preview="preview" />
      <div v-if="error || refreshed" class="text-sm" data-testid="archive-dialog-recovery">
        <p v-if="error" role="alert" class="text-error">{{ error }}</p>
        <p v-else role="status" class="text-on-surface-variant">Child list refreshed. Review it, then confirm the archive.</p>
        <button ref="dialogRecoveryButton" type="button" class="mt-2 underline disabled:opacity-40" :disabled="busy" @click="refreshChildren">
          Refresh child list
        </button>
      </div>
      <template #footer>
        <button type="button" class="px-3 py-2" :disabled="busy" @click="closeConfirmation">Cancel</button>
        <button type="button" class="px-3 py-2" :disabled="busy || !!error" @click="change">Confirm archive</button>
      </template>
    </TdDialog>
  </div>
</template>
