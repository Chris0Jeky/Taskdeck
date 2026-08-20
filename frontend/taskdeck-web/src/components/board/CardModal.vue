<script setup lang="ts">
import { nextTick, onUnmounted, ref, watch } from 'vue'
import { useEscapeToClose } from '../../composables/useEscapeToClose'
import { useCardModal } from '../../composables/useCardModal'
import { useVisualViewport } from '../../composables/useVisualViewport'
import TdDialog from '../ui/TdDialog.vue'
import {
  CardModalHeader,
  CardModalForm,
  CardModalLabels,
  CardModalComments,
  CardModalMetadata,
  CardModalActions,
} from './card-modal'
import type { Card, Label } from '../../types/board'

const props = defineProps<{
  card: Card
  isOpen: boolean
  labels: Label[]
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'updated'): void
}>()

const dialogRef = ref<HTMLElement | null>(null)
let previouslyFocusedElement: HTMLElement | null = null

// `'layout'` fallback: `.card-modal-viewport` has no other height declaration,
// so without a VisualViewport API it must still receive the layout viewport.
const { style: visualViewportStyle } = useVisualViewport({ prefix: '--card-modal' })

const focusableSelector =
  'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'

function restoreFocus() {
  if (previouslyFocusedElement?.isConnected) {
    previouslyFocusedElement.focus()
  }
  previouslyFocusedElement = null
}

function focusInitialControl() {
  const dialog = dialogRef.value
  if (!dialog) return

  const closeButton = dialog.querySelector<HTMLElement>('[aria-label="Close card editor"]')
  const firstFocusable = dialog.querySelector<HTMLElement>(focusableSelector)
  const initialControl = closeButton ?? firstFocusable ?? dialog
  initialControl.focus()
}

function handleKeydown(event: KeyboardEvent) {
  if (event.key !== 'Tab' || !dialogRef.value) return

  const focusableElements = Array.from(
    dialogRef.value.querySelectorAll<HTMLElement>(focusableSelector),
  )

  if (focusableElements.length === 0) {
    event.preventDefault()
    return
  }

  const first = focusableElements[0]!
  const last = focusableElements[focusableElements.length - 1]!

  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
  }
}

watch(
  () => props.isOpen,
  async (isOpen, wasOpen) => {
    if (isOpen) {
      if (!wasOpen) {
        previouslyFocusedElement = document.activeElement as HTMLElement | null
      }
      await nextTick()
      focusInitialControl()
    } else if (wasOpen) {
      restoreFocus()
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  if (props.isOpen) {
    restoreFocus()
  }
})

function handleClose() {
  emit('close')
}

const {
  // Form state
  title,
  description,
  dueDate,
  isBlocked,
  blockReason,
  selectedLabelIds,
  isFormValid,

  // Due date
  formattedDueDate,
  isOverdue,
  clearDueDate,

  // Comments
  newCommentContent,
  replyDraftByParent,
  editingCommentId,
  editingCommentContent,
  topLevelComments,
  getReplies,
  canEditComment,
  handleAddComment,
  handleStartEditComment,
  handleCancelEditComment,
  handleSaveEditComment,
  handleDeleteComment,

  // Provenance
  captureProvenance,
  captureProvenanceError,
  loadingCaptureProvenance,
  loadedCaptureProvenanceCardId,
  captureHref,
  proposalHref,

  // Delete
  showDeleteConfirm,
  isDeleting,
  deleteConfirmDescription,
  handleDeleteClick,
  handleDeleteCancel,
  handleDeleteConfirm,

  // Save
  handleSave,
} = useCardModal({
  getCard: () => props.card,
  getIsOpen: () => props.isOpen,
  getLabels: () => props.labels,
  onUpdated: () => emit('updated'),
  onClose: () => emit('close'),
})

useEscapeToClose(() => props.isOpen, handleClose)
</script>

<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- modal backdrop with dialog role and escape key handler; click-to-close is standard modal UX -->
  <div
    v-if="isOpen"
    ref="dialogRef"
    class="card-modal-viewport fixed inset-x-0 z-50 flex overflow-hidden"
    :style="visualViewportStyle"
    role="dialog"
    aria-label="Edit Card"
    aria-modal="true"
    tabindex="-1"
    @click.self="handleClose"
    @keydown.escape="handleClose"
    @keydown="handleKeydown"
  >
    <!-- Backdrop -->
    <div class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"></div>

    <!-- Modal -->
    <div class="card-modal-scroll-region relative max-h-[calc(100vh-2rem)] w-full max-w-2xl overflow-y-auto overscroll-contain rounded-lg border border-outline-variant/30 bg-surface-container p-6 shadow-xl" data-testid="card-modal-scroll-region" @click.stop>
        <CardModalHeader @close="handleClose" />

        <div class="space-y-4">
          <CardModalForm
            :card="card"
            v-model:title="title"
            v-model:description="description"
            v-model:due-date="dueDate"
            v-model:is-blocked="isBlocked"
            v-model:block-reason="blockReason"
            :formatted-due-date="formattedDueDate"
            :is-overdue="isOverdue"
            @clear-due-date="clearDueDate"
          />

          <CardModalLabels
            :labels="labels"
            v-model:selected-label-ids="selectedLabelIds"
          />

          <CardModalComments
            :top-level-comments="topLevelComments"
            :editing-comment-id="editingCommentId"
            :editing-comment-content="editingCommentContent"
            :reply-draft-by-parent="replyDraftByParent"
            :can-edit-comment-fn="canEditComment"
            :get-replies-fn="getReplies"
            v-model:new-comment-content="newCommentContent"
            @update:editing-comment-content="editingCommentContent = $event"
            @update:reply-draft-by-parent="replyDraftByParent = $event"
            @add-comment="handleAddComment($event)"
            @start-edit-comment="handleStartEditComment($event)"
            @cancel-edit-comment="handleCancelEditComment"
            @save-edit-comment="handleSaveEditComment($event)"
            @delete-comment="handleDeleteComment($event)"
          />

          <CardModalMetadata
            :card="card"
            :loading-capture-provenance="loadingCaptureProvenance"
            :capture-provenance-error="captureProvenanceError"
            :capture-provenance="captureProvenance"
            :loaded-capture-provenance-card-id="loadedCaptureProvenanceCardId"
            :capture-href-fn="captureHref"
            :proposal-href-fn="proposalHref"
          />
        </div>

      <CardModalActions
          :is-form-valid="isFormValid"
          :card="card"
          @save="handleSave"
          @close="handleClose"
        @delete-click="handleDeleteClick"
      />
    </div>
  </div>

  <!-- Delete Confirmation Dialog -->
  <TdDialog
    :open="showDeleteConfirm"
    title="Delete Card"
    :description="deleteConfirmDescription"
    :close-on-backdrop="!isDeleting"
    @close="handleDeleteCancel"
  >
    <template #footer>
      <button
        type="button"
        :disabled="isDeleting"
        class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        @click="handleDeleteCancel"
      >
        Cancel
      </button>
      <button
        type="button"
        :disabled="isDeleting"
        class="px-4 py-2 text-sm font-medium text-on-error bg-error hover:brightness-110 border border-transparent rounded-md transition-all disabled:opacity-50 disabled:cursor-not-allowed"
        @click="handleDeleteConfirm"
      >
        {{ isDeleting ? 'Deleting…' : 'Delete' }}
      </button>
    </template>
  </TdDialog>
</template>

<style scoped>
.card-modal-viewport {
  top: var(--card-modal-visual-viewport-offset-top);
  height: var(--card-modal-visual-viewport-height);
  align-items: flex-start;
  justify-content: stretch;
  padding: max(1rem, env(safe-area-inset-top))
    max(1rem, env(safe-area-inset-right))
    max(1rem, env(safe-area-inset-bottom))
    max(1rem, env(safe-area-inset-left));
}

@media (max-width: 767px) {
  .card-modal-scroll-region {
    display: flex;
    flex: 1 1 auto;
    flex-direction: column;
    min-height: 0;
    max-height: 100%;
  }
}

@media (min-width: 768px) {
  .card-modal-viewport {
    inset: 0;
    height: auto;
    align-items: center;
    justify-content: center;
    padding: 1rem;
  }
}
</style>
