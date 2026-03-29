<script setup lang="ts">
import { onBeforeUnmount, ref, computed, watch } from 'vue'
import { useBoardStore } from '../../store/boardStore'
import { useSessionStore } from '../../store/sessionStore'
import { useEscapeToClose } from '../../composables/useEscapeToClose'
import TdDialog from '../ui/TdDialog.vue'
import type { Card, CardCaptureProvenance, Label } from '../../types/board'
import type { CardComment } from '../../types/comments'
import { normalizeProposalStatus } from '../../utils/automation'

const props = defineProps<{
  card: Card
  isOpen: boolean
  labels: Label[]
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'updated'): void
}>()

const boardStore = useBoardStore()
const sessionStore = useSessionStore()

// Form state
const title = ref('')
const description = ref('')
const dueDate = ref('')
const isBlocked = ref(false)
const blockReason = ref('')
const selectedLabelIds = ref<string[]>([])
const expectedUpdatedAt = ref<string | null>(null)
const newCommentContent = ref('')
const replyDraftByParent = ref<Record<string, string>>({})
const editingCommentId = ref<string | null>(null)
const editingCommentContent = ref('')
const captureProvenance = ref<CardCaptureProvenance | null>(null)
const captureProvenanceError = ref<string | null>(null)
const loadingCaptureProvenance = ref(false)
const loadedCaptureProvenanceCardId = ref<string | null>(null)
const showDeleteConfirm = ref(false)
const isDeleting = ref(false)
const deleteConfirmDescription = computed(
  () => `Are you sure you want to delete "${props.card.title}"? This action cannot be undone.`
)

const comments = computed<CardComment[]>(() => boardStore.getCardComments(props.card.id))
const topLevelComments = computed(() => comments.value.filter(comment => !comment.parentCommentId))

// Watch for card changes
watch(() => props.card, (newCard) => {
  if (newCard) {
    title.value = newCard.title
    description.value = newCard.description || ''
    dueDate.value = newCard.dueDate
      ? new Date(newCard.dueDate).toISOString().split('T')[0] ?? ''
      : ''
    isBlocked.value = newCard.isBlocked
    blockReason.value = newCard.blockReason || ''
    selectedLabelIds.value = newCard.labels.map(l => l.id)
    captureProvenance.value = null
    captureProvenanceError.value = null
    loadedCaptureProvenanceCardId.value = null

    if (props.isOpen) {
      void loadCaptureProvenance()
    }
  }
}, { immediate: true })

watch(
  () => props.isOpen,
  async (isOpen) => {
    if (isOpen) {
      expectedUpdatedAt.value = props.card.updatedAt
      await boardStore.fetchCardComments(props.card.boardId, props.card.id)
      await loadCaptureProvenance()
      boardStore.setEditingCard(props.card.id)
      return
    }

    newCommentContent.value = ''
    replyDraftByParent.value = {}
    editingCommentId.value = null
    editingCommentContent.value = ''
    captureProvenance.value = null
    captureProvenanceError.value = null
    loadingCaptureProvenance.value = false
    loadedCaptureProvenanceCardId.value = null

    if (boardStore.editingCardId === props.card.id) {
      boardStore.setEditingCard(null)
    }
  },
  { immediate: true }
)

const formattedDueDate = computed(() => {
  if (!props.card.dueDate) return 'No due date'
  const date = new Date(props.card.dueDate)
  return date.toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' })
})

const isOverdue = computed(() => {
  if (!props.card.dueDate) return false
  return new Date(props.card.dueDate) < new Date()
})

const isFormValid = computed(() => {
  if (title.value.trim().length === 0) return false
  if (isBlocked.value && blockReason.value.trim().length === 0) return false
  return true
})

function captureHref(captureItemId: string): string {
  return `/workspace/inbox?boardId=${encodeURIComponent(props.card.boardId)}#capture-${encodeURIComponent(captureItemId)}`
}

function proposalHref(proposalId: string): string {
  return `/workspace/review?boardId=${encodeURIComponent(props.card.boardId)}#proposal-${encodeURIComponent(proposalId)}`
}

function proposalStatusLabel(status: CardCaptureProvenance['proposalStatus']): string {
  return normalizeProposalStatus(status)
}

async function loadCaptureProvenance() {
  if (loadingCaptureProvenance.value || loadedCaptureProvenanceCardId.value === props.card.id) {
    return
  }

  loadingCaptureProvenance.value = true
  captureProvenanceError.value = null
  try {
    captureProvenance.value = await boardStore.fetchCardProvenance(props.card.boardId, props.card.id)
    loadedCaptureProvenanceCardId.value = props.card.id
  } catch {
    captureProvenance.value = null
    captureProvenanceError.value = 'Unable to load capture provenance.'
    loadedCaptureProvenanceCardId.value = props.card.id
  } finally {
    loadingCaptureProvenance.value = false
  }
}

async function handleSave() {
  if (!isFormValid.value) return

  try {
    await boardStore.updateCard(props.card.boardId, props.card.id, {
      title: title.value !== props.card.title ? title.value : null,
      description: description.value !== props.card.description ? description.value : null,
      dueDate: dueDate.value ? new Date(dueDate.value).toISOString() : null,
      isBlocked: isBlocked.value !== props.card.isBlocked ? isBlocked.value : null,
      blockReason: isBlocked.value ? blockReason.value : null,
      labelIds: selectedLabelIds.value,
      expectedUpdatedAt: expectedUpdatedAt.value,
    })

    emit('updated')
    emit('close')
  } catch (error) {
    console.error('Failed to update card:', error)
  }
}

function handleDeleteClick() {
  showDeleteConfirm.value = true
}

function handleDeleteCancel() {
  showDeleteConfirm.value = false
}

async function handleDeleteConfirm() {
  if (isDeleting.value) return
  isDeleting.value = true
  try {
    await boardStore.deleteCard(props.card.boardId, props.card.id)
    showDeleteConfirm.value = false
    emit('updated')
    emit('close')
  } catch (error) {
    console.error('Failed to delete card:', error)
  } finally {
    isDeleting.value = false
  }
}

function handleClose() {
  emit('close')
}

function clearDueDate() {
  dueDate.value = ''
}

function getReplies(parentCommentId: string) {
  return comments.value.filter(comment => comment.parentCommentId === parentCommentId)
}

function canEditComment(comment: CardComment) {
  return sessionStore.userId === comment.authorUserId
}

async function handleAddComment(parentCommentId?: string) {
  const content = parentCommentId
    ? (replyDraftByParent.value[parentCommentId] ?? '').trim()
    : newCommentContent.value.trim()

  if (!content) {
    return
  }

  try {
    await boardStore.createCardComment(props.card.boardId, props.card.id, {
      content,
      parentCommentId: parentCommentId ?? null,
    })

    if (parentCommentId) {
      replyDraftByParent.value[parentCommentId] = ''
    } else {
      newCommentContent.value = ''
    }
  } catch (error) {
    console.error('Failed to add comment:', error)
  }
}

function handleStartEditComment(comment: CardComment) {
  if (!canEditComment(comment) || comment.isDeleted) {
    return
  }

  editingCommentId.value = comment.id
  editingCommentContent.value = comment.content
}

function handleCancelEditComment() {
  editingCommentId.value = null
  editingCommentContent.value = ''
}

async function handleSaveEditComment(commentId: string) {
  const content = editingCommentContent.value.trim()
  if (!content) {
    return
  }

  try {
    await boardStore.updateCardComment(props.card.boardId, props.card.id, commentId, { content })
    handleCancelEditComment()
  } catch (error) {
    console.error('Failed to update comment:', error)
  }
}

async function handleDeleteComment(comment: CardComment) {
  if (!canEditComment(comment)) {
    return
  }

  if (!confirm('Delete this comment?')) {
    return
  }

  try {
    await boardStore.deleteCardComment(props.card.boardId, props.card.id, comment.id)
  } catch (error) {
    console.error('Failed to delete comment:', error)
  }
}

useEscapeToClose(() => props.isOpen, handleClose)

onBeforeUnmount(() => {
  if (boardStore.editingCardId === props.card.id) {
    boardStore.setEditingCard(null)
  }

  expectedUpdatedAt.value = null
  newCommentContent.value = ''
  replyDraftByParent.value = {}
  editingCommentId.value = null
  editingCommentContent.value = ''
  captureProvenance.value = null
  captureProvenanceError.value = null
  loadingCaptureProvenance.value = false
  loadedCaptureProvenanceCardId.value = null
})
</script>

<template>
  <div
    v-if="isOpen"
    class="fixed inset-0 z-50 overflow-y-auto"
    @click.self="handleClose"
  >
    <!-- Backdrop -->
    <div class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"></div>

    <!-- Modal -->
    <div class="flex min-h-full items-center justify-center p-4">
      <div class="relative bg-white rounded-lg shadow-xl max-w-2xl w-full p-6" @click.stop>
        <!-- Header -->
        <div class="flex items-start justify-between mb-4">
          <h2 class="text-2xl font-semibold text-gray-900">Edit Card</h2>
          <button
            @click="handleClose"
            class="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Form -->
        <div class="space-y-4">
          <!-- Title -->
          <div>
            <label for="card-title" class="block text-sm font-medium text-gray-700 mb-1">
              Title *
            </label>
            <input
              id="card-title"
              v-model="title"
              type="text"
              required
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="Card title"
            />
          </div>

          <!-- Description -->
          <div>
            <label for="card-description" class="block text-sm font-medium text-gray-700 mb-1">
              Description
            </label>
            <textarea
              id="card-description"
              v-model="description"
              rows="4"
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="Add a more detailed description..."
            ></textarea>
          </div>

          <!-- Due Date -->
          <div>
            <label for="card-due-date" class="block text-sm font-medium text-gray-700 mb-1">
              Due Date
            </label>
            <div class="flex gap-2">
              <input
                id="card-due-date"
                v-model="dueDate"
                type="date"
                class="flex-1 px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              <button
                v-if="dueDate"
                @click="clearDueDate"
                type="button"
                class="px-3 py-2 text-sm text-gray-600 hover:text-gray-800 border border-gray-300 rounded-md hover:bg-gray-50"
              >
                Clear
              </button>
            </div>
            <p v-if="card.dueDate" class="mt-1 text-xs" :class="isOverdue ? 'text-red-600' : 'text-gray-500'">
              Current: {{ formattedDueDate }}
              <span v-if="isOverdue" class="font-medium">(Overdue)</span>
            </p>
          </div>

          <!-- Blocked Status -->
          <div class="border border-gray-200 rounded-md p-4">
            <div class="flex items-center mb-2">
              <input
                id="card-is-blocked"
                v-model="isBlocked"
                type="checkbox"
                class="w-4 h-4 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
              />
              <label for="card-is-blocked" class="ml-2 text-sm font-medium text-gray-700">
                Mark as blocked
              </label>
            </div>
            <div v-if="isBlocked">
              <label for="card-block-reason" class="block text-sm font-medium text-gray-700 mb-1">
                Block Reason *
              </label>
              <textarea
                id="card-block-reason"
                v-model="blockReason"
                rows="2"
                required
                class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="Why is this card blocked?"
              ></textarea>
            </div>
          </div>

          <!-- Labels -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-2">
              Labels
            </label>
            <div v-if="labels.length > 0" class="flex flex-col gap-2">
              <label
                v-for="label in labels"
                :key="label.id"
                class="inline-flex items-center gap-2 px-3 py-1.5 rounded-md text-sm font-medium transition-all cursor-pointer"
                :class="selectedLabelIds.includes(label.id)
                  ? 'text-white ring-2 ring-offset-2 ring-blue-500'
                  : 'text-gray-700 bg-gray-100 hover:bg-gray-200'"
                :style="selectedLabelIds.includes(label.id) ? { backgroundColor: label.colorHex } : {}"
              >
                <input
                  :id="`label-${label.id}`"
                  v-model="selectedLabelIds"
                  type="checkbox"
                  :value="label.id"
                  class="w-4 h-4 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
                />
                <span>{{ label.name }}</span>
              </label>
            </div>
            <p v-else class="text-sm text-gray-500 italic">No labels available</p>
          </div>

          <!-- Comments -->
          <div class="pt-4 border-t border-gray-200 space-y-3">
            <h3 class="text-sm font-semibold text-gray-800">Comments</h3>
            <div class="space-y-2">
              <textarea
                id="new-card-comment"
                v-model="newCommentContent"
                rows="2"
                class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="Write a comment... Use @username to mention teammates."
              ></textarea>
              <div class="flex justify-end">
                <button
                  id="add-card-comment"
                  type="button"
                  class="px-3 py-1.5 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed rounded-md transition-colors"
                  :disabled="newCommentContent.trim().length === 0"
                  @click="handleAddComment()"
                >
                  Add Comment
                </button>
              </div>
            </div>

            <div v-if="topLevelComments.length === 0" class="text-sm text-gray-500 italic">
              No comments yet.
            </div>

            <div v-else class="space-y-3">
              <div
                v-for="comment in topLevelComments"
                :key="comment.id"
                class="border border-gray-200 rounded-md p-3 space-y-2"
              >
                <div class="flex items-start justify-between gap-2">
                  <div class="text-xs text-gray-500">
                    <span class="font-medium text-gray-700">{{ comment.authorUsername }}</span>
                    <span class="mx-1">•</span>
                    <span>{{ new Date(comment.createdAt).toLocaleString() }}</span>
                    <span v-if="comment.editedAt" class="ml-1 italic">(edited)</span>
                  </div>
                  <div v-if="canEditComment(comment) && !comment.isDeleted" class="flex gap-2 text-xs">
                    <button
                      type="button"
                      class="text-blue-600 hover:text-blue-700"
                      @click="handleStartEditComment(comment)"
                    >
                      Edit
                    </button>
                    <button
                      type="button"
                      class="text-red-600 hover:text-red-700"
                      @click="handleDeleteComment(comment)"
                    >
                      Delete
                    </button>
                  </div>
                </div>

                <div v-if="editingCommentId === comment.id" class="space-y-2">
                  <textarea
                    v-model="editingCommentContent"
                    rows="2"
                    class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                  ></textarea>
                  <div class="flex justify-end gap-2">
                    <button
                      type="button"
                      class="px-3 py-1.5 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50"
                      @click="handleCancelEditComment"
                    >
                      Cancel
                    </button>
                    <button
                      type="button"
                      class="px-3 py-1.5 text-sm text-white bg-blue-600 rounded-md hover:bg-blue-700 disabled:bg-gray-400"
                      :disabled="editingCommentContent.trim().length === 0"
                      @click="handleSaveEditComment(comment.id)"
                    >
                      Save
                    </button>
                  </div>
                </div>

                <p
                  v-else
                  class="text-sm whitespace-pre-wrap"
                  :class="comment.isDeleted ? 'text-gray-400 italic' : 'text-gray-800'"
                >
                  {{ comment.content }}
                </p>

                <div class="pl-3 border-l-2 border-gray-200 space-y-2">
                  <div
                    v-for="reply in getReplies(comment.id)"
                    :key="reply.id"
                    class="space-y-1"
                  >
                    <div class="text-xs text-gray-500">
                      <span class="font-medium text-gray-700">{{ reply.authorUsername }}</span>
                      <span class="mx-1">•</span>
                      <span>{{ new Date(reply.createdAt).toLocaleString() }}</span>
                    </div>
                    <p
                      class="text-sm whitespace-pre-wrap"
                      :class="reply.isDeleted ? 'text-gray-400 italic' : 'text-gray-800'"
                    >
                      {{ reply.content }}
                    </p>
                  </div>

                  <div v-if="!comment.isDeleted" class="space-y-2 pt-1">
                    <textarea
                      v-model="replyDraftByParent[comment.id]"
                      rows="2"
                      class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                      placeholder="Reply..."
                    ></textarea>
                    <div class="flex justify-end">
                      <button
                        type="button"
                        class="px-3 py-1.5 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed rounded-md transition-colors"
                        :disabled="!(replyDraftByParent[comment.id] ?? '').trim().length"
                        @click="handleAddComment(comment.id)"
                      >
                        Reply
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Metadata -->
          <div class="pt-4 border-t border-gray-200">
            <div class="text-xs text-gray-500 space-y-1">
              <p>Created: {{ new Date(card.createdAt).toLocaleString() }}</p>
              <p>Last updated: {{ new Date(card.updatedAt).toLocaleString() }}</p>
            </div>
            <div class="mt-3 space-y-2">
              <div v-if="loadingCaptureProvenance" class="text-xs text-gray-500">
                Loading capture provenance...
              </div>
              <div v-else-if="captureProvenanceError" class="text-xs text-red-600" role="alert">
                {{ captureProvenanceError }}
              </div>
              <div v-else-if="captureProvenance" class="space-y-2">
                <div class="flex flex-wrap items-center gap-2 text-xs">
                  <span class="px-2 py-1 rounded-full bg-blue-100 text-blue-700 font-semibold uppercase tracking-wide">
                    Capture Origin
                  </span>
                  <span class="text-gray-500">Proposal status: {{ proposalStatusLabel(captureProvenance.proposalStatus) }}</span>
                </div>
                <div class="flex flex-wrap items-center gap-2 text-xs">
                  <a
                    class="px-2 py-1 rounded-md border border-blue-200 text-blue-700 hover:bg-blue-50"
                    :href="captureHref(captureProvenance.captureItemId)"
                  >
                    Open Capture
                  </a>
                  <a
                    class="px-2 py-1 rounded-md border border-blue-200 text-blue-700 hover:bg-blue-50"
                    :href="proposalHref(captureProvenance.proposalId)"
                  >
                    Open Proposal
                  </a>
                </div>
                <p v-if="captureProvenance.triageRunId" class="text-xs text-gray-500">
                  Triage run: {{ captureProvenance.triageRunId }}
                </p>
              </div>
              <p v-else class="text-xs text-gray-500 italic">No capture provenance available.</p>
            </div>
          </div>
        </div>

        <!-- Actions -->
        <div class="mt-6 flex items-center justify-between">
          <button
            @click="handleDeleteClick"
            type="button"
            class="px-4 py-2 text-sm font-medium text-red-600 hover:text-red-700 hover:bg-red-50 border border-red-300 rounded-md transition-colors"
          >
            Delete Card
          </button>
          <div class="flex gap-2">
            <button
              @click="handleClose"
              type="button"
              class="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 border border-gray-300 rounded-md transition-colors"
            >
              Cancel
            </button>
            <button
              @click="handleSave"
              :disabled="!isFormValid"
              type="button"
              class="px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed rounded-md transition-colors"
            >
              Save Changes
            </button>
          </div>
        </div>
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
