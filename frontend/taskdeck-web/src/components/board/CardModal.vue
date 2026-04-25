<script setup lang="ts">
import { useEscapeToClose } from '../../composables/useEscapeToClose'
import { useCardModal } from '../../composables/useCardModal'
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
    class="fixed inset-0 z-50 overflow-y-auto"
    role="dialog"
    aria-label="Edit Card"
    aria-modal="true"
    @click.self="handleClose"
    @keydown.escape="handleClose"
  >
    <!-- Backdrop -->
    <div class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"></div>

    <!-- Modal -->
    <div class="flex min-h-full items-center justify-center p-4">
      <div class="relative bg-surface-container rounded-lg shadow-xl max-w-2xl w-full p-6 border border-outline-variant/30" @click.stop>
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
          @save="handleSave"
          @close="handleClose"
          @delete-click="handleDeleteClick"
        />
      </div>
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
        class="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 border border-gray-300 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        @click="handleDeleteCancel"
      >
        Cancel
      </button>
      <button
        type="button"
        :disabled="isDeleting"
        class="px-4 py-2 text-sm font-medium text-white bg-red-600 hover:bg-red-700 border border-transparent rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        @click="handleDeleteConfirm"
      >
        {{ isDeleting ? 'Deleting…' : 'Delete' }}
      </button>
    </template>
  </TdDialog>
</template>
