<script setup lang="ts">
import { useAutomationChat } from '../composables/useAutomationChat'
import ChatHeroHeader from '../components/chat/ChatHeroHeader.vue'
import LlmHealthStatusBar from '../components/chat/LlmHealthStatusBar.vue'
import ChatSessionSidebar from '../components/chat/ChatSessionSidebar.vue'
import ChatMessageList from '../components/chat/ChatMessageList.vue'
import ChatComposeBar from '../components/chat/ChatComposeBar.vue'

const {
  sessions,
  selectedSession,
  loadingSessions,
  loadingBoards,
  loadingHealth,
  creatingSession,
  sendingMessage,
  chatHealth,
  chatHealthLoadError,
  newSessionTitle,
  newSessionBoardId,
  messageContent,
  requestProposal,
  boardOptions,
  sortedMessages,
  lastMessageIsClarification,
  selectedSessionBoardName,
  pendingSessionBoardContextLabel,
  queryBoardId,
  updateNewSessionBoardValue,
  handleNewSessionBoardSelect,
  handleCreateSession,
  handleSendMessage,
  handleSkipClarification,
  loadBoardOptions,
  loadSession,
  loadProviderHealth,
  openRoute,
  applyHintSuggestion,
  openReviewRoute,
  openProposalReview,
} = useAutomationChat()
</script>

<template>
  <div class="td-chat">
    <ChatHeroHeader
      :loading-health="loadingHealth"
      @refresh-health="loadProviderHealth()"
      @verify-llm="loadProviderHealth({ probe: true })"
      @open-review="openReviewRoute"
      @open-queue="openRoute('/workspace/automations/queue')"
    />

    <LlmHealthStatusBar
      :chat-health="chatHealth"
      :loading-health="loadingHealth"
      :chat-health-load-error="chatHealthLoadError"
    />

    <div class="td-chat-layout">
      <ChatSessionSidebar
        :sessions="sessions"
        :selected-session-id="selectedSession?.id ?? null"
        :loading-sessions="loadingSessions"
        :loading-boards="loadingBoards"
        :creating-session="creatingSession"
        :new-session-title="newSessionTitle"
        :new-session-board-id="newSessionBoardId"
        :board-options="boardOptions"
        :query-board-id="queryBoardId"
        :pending-session-board-context-label="pendingSessionBoardContextLabel"
        @update:new-session-title="newSessionTitle = $event"
        @update:new-session-board-id="updateNewSessionBoardValue($event)"
        @board-select="handleNewSessionBoardSelect"
        @create-session="handleCreateSession"
        @select-session="loadSession"
        @reload-boards="loadBoardOptions"
        @open-review="openReviewRoute"
      />

      <section class="td-chat-panel">
        <div v-if="!selectedSession" class="td-empty td-empty--panel">
          <h3 class="td-empty__title">Select or create a session</h3>
          <p class="td-empty__copy">
            Review handles the standard approve or reject path. Chat is here for manual operator follow-up when you
            need to inspect the conversation itself.
          </p>
          <div class="td-empty__actions">
            <button class="td-btn td-btn--primary td-btn--sm" @click="openReviewRoute">
              Open Review
            </button>
          </div>
        </div>

        <template v-else>
          <div class="td-chat-header">
            <h2>{{ selectedSession.title }}</h2>
            <div class="td-chat-meta" :data-session-id="selectedSession.id">
              <span>{{ selectedSessionBoardName }}</span>
              <span class="td-chat-meta__detail"> | Session {{ selectedSession.id }}</span>
            </div>
          </div>

          <ChatMessageList
            :messages="sortedMessages"
            :sending-message="sendingMessage"
            @apply-hint-suggestion="applyHintSuggestion"
            @open-proposal-review="openProposalReview"
          />

          <ChatComposeBar
            :message-content="messageContent"
            :request-proposal="requestProposal"
            :sending-message="sendingMessage"
            :last-message-is-clarification="lastMessageIsClarification"
            @update:message-content="messageContent = $event"
            @update:request-proposal="requestProposal = $event"
            @send-message="handleSendMessage"
            @skip-clarification="handleSkipClarification"
          />
        </template>
      </section>
    </div>
  </div>
</template>

<style scoped>
.td-chat {
  max-width: 1200px;
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-chat-layout {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: var(--td-space-4);
}

.td-chat-panel {
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-4);
  min-height: 560px;
}

.td-chat-header {
  padding-bottom: var(--td-space-2);
  border-bottom: 1px solid var(--td-border-default);
  margin-bottom: var(--td-space-3);
}

.td-chat-meta {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-xs);
}

.td-chat-meta__detail {
  font-family: monospace;
}

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

.td-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

@media (max-width: 900px) {
  .td-chat-layout {
    grid-template-columns: 1fr;
  }

  .td-chat-panel {
    min-height: 0;
  }
}
</style>
