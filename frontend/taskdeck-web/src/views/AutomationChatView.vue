<script setup lang="ts">
import { useAutomationChat } from '../composables/useAutomationChat'
import ChatHeroHeader from '../components/chat/ChatHeroHeader.vue'
import LlmHealthStatusBar from '../components/chat/LlmHealthStatusBar.vue'
import ChatSessionSidebar from '../components/chat/ChatSessionSidebar.vue'
import ChatMessageList from '../components/chat/ChatMessageList.vue'
import ChatComposeBar from '../components/chat/ChatComposeBar.vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'

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
  <div class="paper-chat">
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

    <div class="paper-chat__layout">
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

      <section class="paper-chat__panel">
        <div v-if="!selectedSession" class="paper-chat__empty">
          <h3 class="paper-chat__empty-title">Select or create a session</h3>
          <p class="paper-chat__empty-copy">
            Review handles the standard approve or reject path. Chat is here for manual operator follow-up when you
            need to inspect the conversation itself.
          </p>
          <div class="paper-chat__empty-actions">
            <PaperHLBtn variant="ember" @click="openReviewRoute">
              Open Review
            </PaperHLBtn>
          </div>
        </div>

        <template v-else>
          <div class="paper-chat__header">
            <h2 class="tk-h3 paper-chat__session-title">{{ selectedSession.title }}</h2>
            <div class="paper-chat__meta" :data-session-id="selectedSession.id">
              <span>{{ selectedSessionBoardName }}</span>
              <span class="paper-chat__meta-detail"> | Session {{ selectedSession.id }}</span>
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
/* ── Paper & Graphite — AutomationChatView shell ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell.  This view is a thin
   shell: the chat hero, sidebar, message list and compose bar are separate
   components under components/chat/ and are not part of this slice. */

.paper-chat {
  max-width: 1200px;
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.paper-chat__layout {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: var(--s-4, 16px);
}

.paper-chat__panel {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-4, 16px);
  min-height: 560px;
}

.paper-chat__header {
  padding-bottom: var(--s-2, 8px);
  border-bottom: 1px solid var(--line, #d8d0bf);
  margin-bottom: var(--s-3, 12px);
}

.paper-chat__session-title { margin: 0 0 var(--s-1, 4px); font-size: var(--t-lg, 18px); }

.paper-chat__meta {
  color: var(--mute, #6c6557);
  font-size: var(--t-xs, 10.5px);
}

.paper-chat__meta-detail { font-family: var(--mono, ui-monospace, monospace); }

.paper-chat__empty {
  text-align: center;
  color: var(--ink-2, #3a352d);
  padding: var(--s-4, 16px);
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  align-items: center;
  justify-content: center;
  min-height: 160px;
}

.paper-chat__empty-title {
  margin: 0;
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-chat__empty-copy {
  margin: 0;
  max-width: 420px;
  line-height: 1.5;
  color: var(--mute, #6c6557);
}

.paper-chat__empty-actions {
  display: flex;
  gap: var(--s-2, 8px);
  flex-wrap: wrap;
  justify-content: center;
}

@media (max-width: 900px) {
  .paper-chat__layout {
    grid-template-columns: 1fr;
  }

  .paper-chat__panel {
    min-height: 0;
  }
}
</style>
