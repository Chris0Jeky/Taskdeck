<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
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

const props = withDefaults(defineProps<{
  card: Card
  isOpen: boolean
  labels: Label[]
  presentation?: 'modal' | 'inspector'
}>(), {
  presentation: 'modal',
})

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'updated'): void
}>()

const { t } = useI18n()

const dialogRef = ref<HTMLElement | null>(null)
const showDiscardConfirm = ref(false)
let previouslyFocusedElement: HTMLElement | null = null
const isInspector = computed(() => props.presentation === 'inspector')

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
  if (isInspector.value || event.key !== 'Tab' || !dialogRef.value) return

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

function closeWithoutPrompt() {
  showDiscardConfirm.value = false
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
  hasUnsavedChanges,

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
  showCommentDeleteConfirm,
  isDeletingComment,
  handleCommentDeleteCancel,
  handleCommentDeleteConfirm,

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

function handleClose() {
  if (hasUnsavedChanges.value) {
    showDiscardConfirm.value = true
    return
  }
  closeWithoutPrompt()
}

useEscapeToClose(
  () =>
    props.isOpen &&
    !showDiscardConfirm.value &&
    !showDeleteConfirm.value &&
    !showCommentDeleteConfirm.value,
  handleClose,
)
</script>

<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- modal backdrop with dialog role and escape key handler; click-to-close is standard modal UX -->
  <div
    v-if="isOpen"
    ref="dialogRef"
    :class="[
      'card-modal-viewport flex overflow-hidden',
      isInspector ? 'card-modal-viewport--inspector' : 'card-modal-viewport--modal fixed inset-x-0 z-50',
    ]"
    :style="isInspector ? undefined : visualViewportStyle"
    role="dialog"
    aria-label="Edit Card"
    :aria-modal="isInspector ? undefined : 'true'"
    tabindex="-1"
    @click.self="handleClose"
    @keydown.escape="handleClose"
    @keydown="handleKeydown"
  >
    <!-- Backdrop -->
    <div v-if="!isInspector" class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"></div>

    <!-- Modal -->
    <div
      class="card-modal-scroll-region relative w-full overflow-y-auto overscroll-contain rounded-lg border border-outline-variant/30 bg-surface-container p-6 shadow-xl"
      :class="isInspector ? 'card-modal-scroll-region--inspector' : 'max-h-[calc(100vh-2rem)] max-w-2xl'"
      data-testid="card-modal-scroll-region"
      :data-presentation="presentation"
      @click.stop
    >
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
    :open="showDiscardConfirm"
    title="Discard card changes?"
    description="This card has unsaved changes. Discard them and close the editor?"
    @close="showDiscardConfirm = false"
  >
    <template #footer>
      <button
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors"
        data-testid="card-discard-cancel"
        @click="showDiscardConfirm = false"
      >
        Keep editing
      </button>
      <button
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-error bg-error hover:brightness-110 border border-transparent rounded-md transition-all"
        data-testid="card-discard-confirm"
        @click="closeWithoutPrompt"
      >
        Discard changes
      </button>
    </template>
  </TdDialog>

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

  <TdDialog
    :open="showCommentDeleteConfirm"
    :title="t('cardModal.commentDelete.title')"
    :description="t('cardModal.commentDelete.description')"
    :close-on-backdrop="!isDeletingComment"
    @close="handleCommentDeleteCancel"
  >
    <template #footer>
      <button
        type="button"
        :disabled="isDeletingComment"
        class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        data-testid="card-comment-delete-cancel"
        @click="handleCommentDeleteCancel"
      >
        {{ t('cardModal.commentDelete.cancel') }}
      </button>
      <button
        type="button"
        :disabled="isDeletingComment"
        class="px-4 py-2 text-sm font-medium text-on-error bg-error hover:brightness-110 border border-transparent rounded-md transition-all disabled:opacity-50 disabled:cursor-not-allowed"
        data-testid="card-comment-delete-confirm"
        @click="handleCommentDeleteConfirm"
      >
        {{ isDeletingComment ? t('cardModal.commentDelete.deleting') : t('cardModal.commentDelete.confirm') }}
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

.card-modal-viewport--inspector {
  position: sticky;
  top: 1rem;
  flex: 0 0 min(420px, 36vw);
  height: calc(100vh - 2rem);
  min-width: 340px;
  align-self: flex-start;
}

.card-modal-scroll-region--inspector {
  max-height: 100%;
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
