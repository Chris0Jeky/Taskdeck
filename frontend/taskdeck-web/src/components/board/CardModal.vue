<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useEscapeToClose } from '../../composables/useEscapeToClose'
import { useCardModal } from '../../composables/useCardModal'
import { useVisualViewport } from '../../composables/useVisualViewport'
import TdDialog from '../ui/TdDialog.vue'
import CardParentField from './CardParentField.vue'
import CardAssignmentField from './CardAssignmentField.vue'
import CardDetachList from './CardDetachList.vue'
import CardArchiveAction from './CardArchiveAction.vue'
import { useBoardStore } from '../../store/boardStore'
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
  suppressDiscardPrompt?: boolean
  skipFocusRestore?: boolean
}>(), {
  presentation: 'modal',
  suppressDiscardPrompt: false,
  skipFocusRestore: false,
})

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'updated'): void
  (e: 'dirty-change', dirty: boolean): void
  (e: 'saving-change', saving: boolean): void
}>()

const { t } = useI18n()
const router = useRouter()
const boardStore = useBoardStore()
async function refreshArchiveState() {
  emit('close')
  await boardStore.fetchBoard(props.card.boardId)
}
const pendingThinkingPath = ref<string | null>(null)
const assignmentDirty = ref(false)
/*
 * #2981. An assignment PUT that has left the browser cannot be recalled, so
 * while one is unanswered this editor may not offer — or honour — any
 * affordance whose promise is "discarded". Every close path funnels through
 * `handleClose`/`closeWithoutPrompt`, and both refuse in favour of a notice
 * that says what is actually true. The state is also emitted upward so a host
 * that owns its own switch/navigation prompts (the Paper board) can refuse the
 * same way.
 */
const assignmentSaving = ref(false)
const showSavePendingNotice = ref(false)
const hasUnsavedChanges = computed(() => hasCardUnsavedChanges.value || assignmentDirty.value)
function refuseWhileAssignmentSaving() {
  showDiscardConfirm.value = false
  showSavePendingNotice.value = true
}
function dismissSavePendingNotice() {
  showSavePendingNotice.value = false
}
function acceptAssignments(saved: Card, previousVersion?: string) {
  acceptAssignmentVersion(saved.updatedAt, previousVersion)
  const index = boardStore.currentBoardCards.findIndex(c => c.id === saved.id)
  if (index >= 0) boardStore.currentBoardCards.splice(index, 1, saved)
}

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

function shouldPreserveFocusDuringPresentationTransition() {
  const dialog = dialogRef.value
  if (!dialog) return true

  const activeElement = document.activeElement
  if (activeElement instanceof HTMLElement) {
    if (dialog.contains(activeElement)) return true
    if (activeElement.closest('[role="dialog"]')) return true
  }

  return Array.from(
    document.querySelectorAll<HTMLElement>('[role="dialog"][aria-modal="true"]'),
  ).some((candidate) => candidate !== dialog && !dialog.contains(candidate))
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
      if (props.skipFocusRestore) {
        previouslyFocusedElement = null
      } else {
        restoreFocus()
      }
    }
  },
  { immediate: true },
)

// A desktop inspector stays mounted while another card is selected. Move focus
// to the new editor's close control so keyboard users arrive at the newly
// selected card instead of remaining in a control whose contents just changed.
watch(
  () => props.card.id,
  async (cardId, previousCardId) => {
    if (!props.isOpen || cardId === previousCardId) return
    await nextTick()
    focusInitialControl()
  },
)

watch(
  isInspector,
  async (inspector, wasInspector) => {
    if (!props.isOpen || inspector || !wasInspector) return
    await nextTick()
    if (!props.isOpen || isInspector.value || shouldPreserveFocusDuringPresentationTransition()) return
    focusInitialControl()
  },
)

onUnmounted(() => {
  if (props.isOpen && !props.skipFocusRestore) {
    restoreFocus()
  }
})

function closeWithoutPrompt() {
  if (assignmentSaving.value) {
    refuseWhileAssignmentSaving()
    return
  }

  const destination = pendingThinkingPath.value
  pendingThinkingPath.value = null
  showDiscardConfirm.value = false
  emit('close')
  if (destination) void router.push(destination)
}

function openThinkingDeck() {
  if (assignmentSaving.value) {
    refuseWhileAssignmentSaving()
    return
  }

  const destination = `/workspace/boards/${props.card.boardId}/cards/${props.card.id}/thinking`
  if (hasUnsavedChanges.value) {
    pendingThinkingPath.value = destination
    showDiscardConfirm.value = true
    return
  }
  void router.push(destination)
}

function keepEditing() {
  pendingThinkingPath.value = null
  showDiscardConfirm.value = false
}

const {
  // Form state
  parentCardId,
  detachPreview,
  deletePreviewError,
  deletePreviewLoading,
  workItemType,
  title,
  description,
  dueDate,
  isBlocked,
  blockReason,
  selectedLabelIds,
  isFormValid,
  hasUnsavedChanges: hasCardUnsavedChanges,
  acceptAssignmentVersion,
  isSaving,
  saveError,

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

watch(hasUnsavedChanges, (dirty) => {
  emit('dirty-change', dirty)
}, { immediate: true })

// A save that starts behind an already-open discard confirmation replaces it:
// that dialog's only action is now a promise the editor cannot keep. When the
// save settles the notice is withdrawn, restoring every control and leaving the
// field to show the committed assignees or the failure and the kept draft.
watch(assignmentSaving, (saving) => {
  emit('saving-change', saving)
  if (saving) {
    if (showDiscardConfirm.value) refuseWhileAssignmentSaving()
    return
  }
  dismissSavePendingNotice()
}, { immediate: true })

watch(() => props.suppressDiscardPrompt, (suppress) => {
  if (!suppress) return

  pendingThinkingPath.value = null
  showDiscardConfirm.value = false
}, { immediate: true })

function handleClose() {
  if (props.suppressDiscardPrompt) return

  if (assignmentSaving.value) {
    refuseWhileAssignmentSaving()
    return
  }

  if (hasUnsavedChanges.value) {
    showDiscardConfirm.value = true
    return
  }
  closeWithoutPrompt()
}

useEscapeToClose(
  () =>
    props.isOpen &&
    !props.suppressDiscardPrompt &&
    !showSavePendingNotice.value &&
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
        <CardParentField v-model="parentCardId" :card="card" :disabled="isSaving || !!card.isArchived" />
        <CardAssignmentField v-if="isOpen" :card="card" :disabled="isSaving"
          :read-only="boardStore.currentBoard?.id !== card.boardId || boardStore.currentBoard?.canWrite !== true || !!boardStore.currentBoard?.isArchived || !!card.isArchived"
          @dirty-change="assignmentDirty = $event" @saving-change="assignmentSaving = $event"
          @saved="acceptAssignments" />
        <CardArchiveAction :key="card.updatedAt" :card="card" :disabled="hasUnsavedChanges"
          @changed="emit('updated'); emit('close')" @refresh="refreshArchiveState" />
        <button type="button" class="mb-4 rounded-md border border-outline-variant/40 px-3 py-2 text-sm text-on-surface hover:bg-surface-container-high" @click="openThinkingDeck">Open thinking deck <span aria-hidden="true">↗</span></button>

        <p v-if="saveError" role="alert" class="my-3 text-sm text-error">{{ saveError }}</p>
        <fieldset :disabled="card.isArchived || isSaving" class="space-y-4">
          <CardModalForm
            :card="card"
            v-model:title="title"
            v-model:work-item-type="workItemType"
            :can-edit-type="boardStore.currentBoard?.id === card.boardId && boardStore.currentBoard.canWrite === true && !boardStore.currentBoard.isArchived && !card.isArchived"
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
        </fieldset>

      <p v-if="assignmentSaving" role="status" class="text-sm">Saving assignments… the editor stays open until the server answers.</p>
      <p v-else-if="assignmentDirty" class="text-sm">Save or cancel assignment changes before saving other card fields.</p>
      <CardModalActions
          :is-form-valid="isFormValid && !card.isArchived && !isSaving && !assignmentDirty"
          :is-saving="isSaving"
          :card="card"
          @save="handleSave"
          @close="handleClose"
        @delete-click="handleDeleteClick"
      />
    </div>
  </div>

  <!-- In-flight assignment save: the honest answer to a close request -->
  <TdDialog
    :open="showSavePendingNotice"
    title="Saving assignments…"
    description="This assignment change was already sent to the server, so it cannot be discarded or cancelled. Wait for the save to finish, then close the editor."
    :close-on-backdrop="true"
    @close="dismissSavePendingNotice"
  >
    <template #footer>
      <button
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors"
        data-testid="card-assignment-save-pending-dismiss"
        @click="dismissSavePendingNotice"
      >
        Keep editing
      </button>
    </template>
  </TdDialog>

  <!-- Delete Confirmation Dialog -->
  <TdDialog
    :open="showDiscardConfirm"
    title="Discard card changes?"
    description="This card has unsaved changes. Discard them and close the editor?"
    @close="keepEditing"
  >
    <template #footer>
      <button
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors"
        data-testid="card-discard-cancel"
        @click="keepEditing"
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
    <p v-if="deletePreviewLoading" role="status">Loading every affected child...</p>
    <p v-if="deletePreviewError" role="alert">{{ deletePreviewError }}</p>
    <CardDetachList v-if="detachPreview" :preview="detachPreview" />
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
        :disabled="isDeleting || !detachPreview || !!deletePreviewError"
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
