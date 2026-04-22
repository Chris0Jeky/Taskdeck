<script setup lang="ts">
import type { ChatSession } from '../../types/chat'
import type { InputAssistOption } from '../../utils/inputAssist'
import InputAssistField from '../common/InputAssistField.vue'

defineProps<{
  sessions: ChatSession[]
  selectedSessionId: string | null
  loadingSessions: boolean
  loadingBoards: boolean
  creatingSession: boolean
  newSessionTitle: string
  newSessionBoardId: string
  boardOptions: InputAssistOption[]
  queryBoardId: string
  pendingSessionBoardContextLabel: string
}>()

const emit = defineEmits<{
  (e: 'update:newSessionTitle', value: string): void
  (e: 'update:newSessionBoardId', value: string): void
  (e: 'board-select', option: InputAssistOption): void
  (e: 'create-session'): void
  (e: 'select-session', sessionId: string): void
  (e: 'reload-boards'): void
  (e: 'open-review'): void
}>()
</script>

<template>
  <aside class="td-chat-sessions">
    <div class="td-chat-section-head">
      <div>
        <h2 class="td-subtitle">Sessions</h2>
        <p class="td-chat-section-copy">
          Create a manual session when you need operator control. Pick a board by name if the conversation should
          stay anchored to one workspace.
        </p>
      </div>
      <button class="td-btn td-btn--secondary td-btn--sm" :disabled="loadingBoards" @click="emit('reload-boards')">
        {{ loadingBoards ? 'Loading...' : 'Reload Boards' }}
      </button>
    </div>

    <div class="td-form-group">
      <input
        :value="newSessionTitle"
        class="td-input"
        type="text"
        aria-label="Session title"
        placeholder="Session title"
        @input="emit('update:newSessionTitle', ($event.target as HTMLInputElement).value)"
      />
      <InputAssistField
        :model-value="newSessionBoardId"
        :options="boardOptions"
        aria-label="Board context"
        placeholder="Board context (optional)"
        no-results-text="No matching boards."
        @update:model-value="emit('update:newSessionBoardId', $event)"
        @select="emit('board-select', $event)"
      />
      <p v-if="queryBoardId" class="td-chat-board-context">
        Board context will stay anchored to {{ pendingSessionBoardContextLabel }}.
      </p>
      <button class="td-btn td-btn--primary td-btn--sm" @click="emit('create-session')" :disabled="creatingSession">
        {{ creatingSession ? 'Creating...' : 'Create Session' }}
      </button>
    </div>

    <div v-if="loadingSessions" class="td-loading">Loading sessions...</div>
    <div v-else-if="sessions.length === 0" class="td-empty td-empty--panel">
      <h3 class="td-empty__title">No chat sessions yet</h3>
      <p class="td-empty__copy">
        Return to Review for the normal proposal flow, or create a session here when you specifically need an
        operator conversation.
      </p>
      <div class="td-empty__actions">
        <button class="td-btn td-btn--primary td-btn--sm" @click="emit('open-review')">
          Open Review
        </button>
      </div>
    </div>
    <button
      v-for="session in sessions"
      :key="session.id"
      :class="['td-session-item', { 'td-session-item--active': selectedSessionId === session.id }]"
      @click="emit('select-session', session.id)"
    >
      <div class="td-session-title">{{ session.title }}</div>
      <div class="td-session-meta">{{ new Date(session.updatedAt).toLocaleString() }}</div>
    </button>
  </aside>
</template>

<style scoped>
.td-chat-sessions {
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-4);
  min-height: 560px;
}

.td-chat-section-head {
  display: flex;
  gap: var(--td-space-3);
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: var(--td-space-3);
}

.td-subtitle {
  font-size: var(--td-font-base);
  margin-bottom: var(--td-space-1);
}

.td-chat-section-copy {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-chat-board-context {
  margin: 0;
  font-size: var(--td-font-xs);
  color: var(--td-color-primary);
  font-weight: 600;
}

.td-form-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  margin-bottom: var(--td-space-3);
}

.td-input {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
}

.td-session-item {
  width: 100%;
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-secondary);
  padding: var(--td-space-2);
  text-align: left;
  cursor: pointer;
  margin-bottom: var(--td-space-2);
}

.td-session-item--active {
  border-color: var(--td-color-primary);
  background: var(--td-color-primary-light);
}

.td-session-title {
  font-weight: 600;
  font-size: var(--td-font-sm);
}

.td-session-meta {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  margin-top: var(--td-space-1);
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-4);
  border: none;
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  font-weight: 600;
  cursor: pointer;
}

.td-btn--sm {
  padding: var(--td-space-1) var(--td-space-3);
  font-size: var(--td-font-xs);
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--primary:hover:not(:disabled) {
  background: var(--td-color-primary-hover);
}

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border: 1px solid var(--td-border-default);
}

.td-btn--secondary:hover:not(:disabled) {
  background: var(--td-surface-hover);
}

.td-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.td-loading,
.td-empty {
  text-align: center;
  color: var(--td-text-secondary);
  padding: var(--td-space-4);
}

.td-empty--panel {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  align-items: center;
  justify-content: center;
  min-height: 160px;
}

.td-empty__title {
  margin: 0;
  font-size: var(--td-font-base);
  color: var(--td-text-primary);
}

.td-empty__copy {
  margin: 0;
  max-width: 420px;
  line-height: 1.5;
}

.td-empty__actions {
  display: flex;
  gap: var(--td-space-2);
  flex-wrap: wrap;
  justify-content: center;
}

@media (max-width: 900px) {
  .td-chat-section-head {
    flex-direction: column;
  }

  .td-chat-sessions {
    min-height: 0;
  }
}
</style>
