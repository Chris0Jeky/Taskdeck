<script setup lang="ts">
import { computed, inject } from 'vue'
import type { CardComment } from '../../../types/comments'
import { cardCommentLoadContextKey } from '../../../composables/cardCommentLoadContext'

const props = defineProps<{
  topLevelComments: CardComment[]
  editingCommentId: string | null
  editingCommentContent: string
  replyDraftByParent: Record<string, string>
  canEditCommentFn: (comment: CardComment) => boolean
  getRepliesFn: (parentCommentId: string) => CardComment[]
  writesDisabled?: boolean
}>()

const emit = defineEmits<{
  (e: 'add-comment', parentCommentId?: string): void
  (e: 'start-edit-comment', comment: CardComment): void
  (e: 'cancel-edit-comment'): void
  (e: 'save-edit-comment', commentId: string): void
  (e: 'delete-comment', comment: CardComment): void
  (e: 'update:editingCommentContent', value: string): void
  (e: 'update:replyDraftByParent', value: Record<string, string>): void
}>()

const newCommentContent = defineModel<string>('newCommentContent', { required: true })
const commentLoad = inject(cardCommentLoadContextKey, null)
const commentsLoading = computed(() => commentLoad?.loading.value ?? false)
const commentsLoadError = computed(() => commentLoad?.error.value ?? null)

function retryComments() {
  commentLoad?.retry()
}

function updateReplyDraft(commentId: string, value: string) {
  const updated = { ...props.replyDraftByParent, [commentId]: value }
  emit('update:replyDraftByParent', updated)
}
</script>

<template>
  <div class="pt-4 border-t border-outline-variant/30 space-y-3">
    <h3 class="text-sm font-semibold text-on-surface">Comments</h3>
    <div class="space-y-2">
      <textarea
        id="new-card-comment"
        v-model="newCommentContent"
        :disabled="writesDisabled"
        rows="2"
        class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary disabled:cursor-not-allowed disabled:opacity-60"
        placeholder="Write a comment... Use @username to mention teammates."
      ></textarea>
      <div class="flex justify-end">
        <button
          id="add-card-comment"
          type="button"
          class="px-3 py-1.5 text-sm font-medium text-on-primary-container bg-primary-container hover:brightness-110 disabled:opacity-40 disabled:cursor-not-allowed rounded-md transition-all"
          :disabled="writesDisabled || newCommentContent.trim().length === 0"
          @click="$emit('add-comment')"
        >
          Add Comment
        </button>
      </div>
    </div>

    <p
      v-if="commentsLoading"
      role="status"
      class="text-sm text-on-surface-variant"
      data-testid="card-comments-loading"
    >
      Loading comments…
    </p>

    <div v-else-if="commentsLoadError" class="space-y-2">
      <p
        id="card-comments-load-error"
        role="alert"
        class="text-sm text-error"
        data-testid="card-comments-load-error"
      >
        {{ commentsLoadError }}
      </p>
      <button
        type="button"
        class="px-3 py-1.5 text-sm font-medium text-on-surface border border-outline-variant/40 rounded-md hover:bg-surface-container-high transition-colors"
        aria-describedby="card-comments-load-error"
        data-testid="card-comments-retry"
        @click="retryComments"
      >
        Retry comments
      </button>
    </div>

    <div
      v-if="!commentsLoading && !commentsLoadError && topLevelComments.length === 0"
      class="text-sm text-on-surface-variant italic"
      data-testid="card-comments-empty"
    >
      No comments yet.
    </div>

    <div v-if="topLevelComments.length > 0" class="space-y-3">
      <div
        v-for="comment in topLevelComments"
        :key="comment.id"
        class="border border-outline-variant/30 rounded-md p-3 space-y-2 bg-surface-container-low"
      >
        <div class="flex items-start justify-between gap-2">
          <div class="text-xs text-on-surface-variant">
            <span class="font-medium text-on-surface">{{ comment.authorUsername }}</span>
            <span class="mx-1">&bull;</span>
            <span>{{ new Date(comment.createdAt).toLocaleString() }}</span>
            <span v-if="comment.editedAt" class="ml-1 italic">(edited)</span>
          </div>
          <div v-if="canEditCommentFn(comment) && !comment.isDeleted" class="flex gap-2 text-xs">
            <button
              type="button"
              :disabled="writesDisabled"
              class="text-primary hover:text-primary/80 disabled:cursor-not-allowed disabled:opacity-40"
              @click="$emit('start-edit-comment', comment)"
            >
              Edit
            </button>
            <button
              type="button"
              :disabled="writesDisabled"
              class="text-error hover:text-error/80 disabled:cursor-not-allowed disabled:opacity-40"
              @click="$emit('delete-comment', comment)"
            >
              Delete
            </button>
          </div>
        </div>

        <div v-if="editingCommentId === comment.id" class="space-y-2">
          <textarea
            :value="editingCommentContent"
            :disabled="writesDisabled"
            aria-label="Edit comment"
            rows="2"
            class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface focus:outline-none focus:ring-2 focus:ring-primary disabled:cursor-not-allowed disabled:opacity-60"
            @input="$emit('update:editingCommentContent', ($event.target as HTMLTextAreaElement).value)"
          ></textarea>
          <div class="flex justify-end gap-2">
            <button
              type="button"
              :disabled="writesDisabled"
              class="px-3 py-1.5 text-sm text-on-surface-variant border border-outline-variant/40 rounded-md hover:bg-surface-container-high disabled:cursor-not-allowed disabled:opacity-40 transition-colors"
              @click="$emit('cancel-edit-comment')"
            >
              Cancel
            </button>
            <button
              type="button"
              class="px-3 py-1.5 text-sm text-on-primary-container bg-primary-container rounded-md hover:brightness-110 disabled:opacity-40 disabled:cursor-not-allowed"
              :disabled="writesDisabled || editingCommentContent.trim().length === 0"
              @click="$emit('save-edit-comment', comment.id)"
            >
              Save
            </button>
          </div>
        </div>

        <p
          v-else
          class="text-sm whitespace-pre-wrap"
          :class="comment.isDeleted ? 'text-on-surface-variant italic' : 'text-on-surface'"
        >
          {{ comment.content }}
        </p>

        <div class="pl-3 border-l-2 border-outline-variant/30 space-y-2">
          <div
            v-for="reply in getRepliesFn(comment.id)"
            :key="reply.id"
            class="space-y-1"
          >
            <div class="text-xs text-on-surface-variant">
              <span class="font-medium text-on-surface">{{ reply.authorUsername }}</span>
              <span class="mx-1">&bull;</span>
              <span>{{ new Date(reply.createdAt).toLocaleString() }}</span>
            </div>
            <p
              class="text-sm whitespace-pre-wrap"
              :class="reply.isDeleted ? 'text-on-surface-variant italic' : 'text-on-surface'"
            >
              {{ reply.content }}
            </p>
          </div>

          <div v-if="!comment.isDeleted" class="space-y-2 pt-1">
            <textarea
              :value="replyDraftByParent[comment.id] ?? ''"
              :disabled="writesDisabled"
              aria-label="Reply to comment"
              rows="2"
              class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary disabled:cursor-not-allowed disabled:opacity-60"
              placeholder="Reply..."
              @input="updateReplyDraft(comment.id, ($event.target as HTMLTextAreaElement).value)"
            ></textarea>
            <div class="flex justify-end">
              <button
                type="button"
                class="px-3 py-1.5 text-sm font-medium text-on-primary-container bg-primary-container hover:brightness-110 disabled:opacity-40 disabled:cursor-not-allowed rounded-md transition-all"
                :disabled="writesDisabled || !(replyDraftByParent[comment.id] ?? '').trim().length"
                @click="$emit('add-comment', comment.id)"
              >
                Reply
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
