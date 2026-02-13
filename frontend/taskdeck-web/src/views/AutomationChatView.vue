<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { chatApi } from '../api/chatApi'
import { useToastStore } from '../store/toastStore'
import type { ChatSession } from '../types/chat'
import { normalizeChatRole } from '../utils/chat'
import { getErrorDisplay } from '../composables/useErrorMapper'

const toast = useToastStore()

const sessions = ref<ChatSession[]>([])
const selectedSession = ref<ChatSession | null>(null)
const loadingSessions = ref(false)
const creatingSession = ref(false)
const sendingMessage = ref(false)

const newSessionTitle = ref('')
const newSessionBoardId = ref('')
const messageContent = ref('')
const requestProposal = ref(false)

const sortedMessages = computed(() => {
  const current = selectedSession.value
  if (!current) {
    return []
  }

  return [...current.recentMessages].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime())
})

async function loadSessions() {
  try {
    loadingSessions.value = true
    sessions.value = await chatApi.getMySessions()
    if (!selectedSession.value && sessions.value.length > 0) {
      await loadSession(sessions.value[0]!.id)
    }
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load chat sessions').message)
  } finally {
    loadingSessions.value = false
  }
}

async function loadSession(sessionId: string) {
  try {
    selectedSession.value = await chatApi.getSession(sessionId)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load chat session').message)
  }
}

async function handleCreateSession() {
  if (!newSessionTitle.value.trim()) {
    toast.error('Session title is required')
    return
  }

  try {
    creatingSession.value = true
    const created = await chatApi.createSession({
      title: newSessionTitle.value.trim(),
      boardId: newSessionBoardId.value.trim() || null,
    })
    newSessionTitle.value = ''
    newSessionBoardId.value = ''
    await loadSessions()
    await loadSession(created.id)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to create session').message)
  } finally {
    creatingSession.value = false
  }
}

async function handleSendMessage() {
  if (!selectedSession.value) {
    toast.error('Select a session first')
    return
  }
  if (!messageContent.value.trim()) {
    return
  }

  try {
    sendingMessage.value = true
    await chatApi.sendMessage(selectedSession.value.id, {
      content: messageContent.value.trim(),
      requestProposal: requestProposal.value,
    })
    messageContent.value = ''
    requestProposal.value = false
    await loadSession(selectedSession.value.id)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to send message').message)
  } finally {
    sendingMessage.value = false
  }
}

onMounted(() => {
  void loadSessions()
})
</script>

<template>
  <div class="td-chat">
    <h1 class="td-page-title">Automation Chat</h1>

    <div class="td-chat-layout">
      <aside class="td-chat-sessions">
        <h2 class="td-subtitle">Sessions</h2>

        <div class="td-form-group">
          <input v-model="newSessionTitle" class="td-input" type="text" placeholder="Session title" />
          <input v-model="newSessionBoardId" class="td-input" type="text" placeholder="Board ID (optional)" />
          <button class="td-btn td-btn--primary td-btn--sm" @click="handleCreateSession" :disabled="creatingSession">
            {{ creatingSession ? 'Creating...' : 'Create Session' }}
          </button>
        </div>

        <div v-if="loadingSessions" class="td-loading">Loading sessions...</div>
        <div v-else-if="sessions.length === 0" class="td-empty">No sessions yet.</div>
        <button
          v-for="session in sessions"
          :key="session.id"
          :class="['td-session-item', { 'td-session-item--active': selectedSession?.id === session.id }]"
          @click="loadSession(session.id)"
        >
          <div class="td-session-title">{{ session.title }}</div>
          <div class="td-session-meta">{{ new Date(session.updatedAt).toLocaleString() }}</div>
        </button>
      </aside>

      <section class="td-chat-panel">
        <div v-if="!selectedSession" class="td-empty">Select or create a chat session.</div>

        <template v-else>
          <div class="td-chat-header">
            <h2>{{ selectedSession.title }}</h2>
            <div class="td-chat-meta">Session {{ selectedSession.id }}</div>
          </div>

          <div class="td-chat-messages">
            <div v-if="sortedMessages.length === 0" class="td-empty">No messages yet.</div>
            <div v-for="message in sortedMessages" :key="message.id" class="td-message">
              <div class="td-message-header">
                <span class="td-message-role">{{ normalizeChatRole(message.role) }}</span>
                <span class="td-message-time">{{ new Date(message.createdAt).toLocaleTimeString() }}</span>
              </div>
              <div class="td-message-content">{{ message.content }}</div>
              <div v-if="message.proposalId" class="td-message-proposal">Proposal: {{ message.proposalId }}</div>
            </div>
          </div>

          <div class="td-chat-compose">
            <textarea
              v-model="messageContent"
              class="td-textarea"
              rows="3"
              placeholder="Describe an automation instruction..."
              @keydown.ctrl.enter.prevent="handleSendMessage"
            ></textarea>
            <label class="td-checkbox">
              <input v-model="requestProposal" type="checkbox" />
              Request proposal generation
            </label>
            <button class="td-btn td-btn--primary" @click="handleSendMessage" :disabled="sendingMessage">
              {{ sendingMessage ? 'Sending...' : 'Send Message' }}
            </button>
          </div>
        </template>
      </section>
    </div>
  </div>
</template>

<style scoped>
.td-chat { max-width: 1200px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-chat-layout { display: grid; grid-template-columns: 320px 1fr; gap: var(--td-space-4); }
.td-chat-sessions, .td-chat-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-4); min-height: 560px; }
.td-subtitle { font-size: var(--td-font-base); margin-bottom: var(--td-space-3); }
.td-form-group { display: flex; flex-direction: column; gap: var(--td-space-2); margin-bottom: var(--td-space-3); }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-session-item { width: 100%; border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); background: var(--td-surface-secondary); padding: var(--td-space-2); text-align: left; cursor: pointer; margin-bottom: var(--td-space-2); }
.td-session-item--active { border-color: var(--td-color-primary); background: var(--td-color-primary-light); }
.td-session-title { font-weight: 600; font-size: var(--td-font-sm); }
.td-session-meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); margin-top: var(--td-space-1); }
.td-chat-header { padding-bottom: var(--td-space-2); border-bottom: 1px solid var(--td-border-default); margin-bottom: var(--td-space-3); }
.td-chat-meta { color: var(--td-text-tertiary); font-size: var(--td-font-xs); }
.td-chat-messages { display: flex; flex-direction: column; gap: var(--td-space-2); min-height: 360px; max-height: 420px; overflow-y: auto; margin-bottom: var(--td-space-3); }
.td-message { border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); padding: var(--td-space-2); }
.td-message-header { display: flex; justify-content: space-between; margin-bottom: var(--td-space-1); }
.td-message-role { font-weight: 600; font-size: var(--td-font-xs); }
.td-message-time { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-message-content { white-space: pre-wrap; font-size: var(--td-font-sm); }
.td-message-proposal { margin-top: var(--td-space-1); font-size: var(--td-font-xs); color: var(--td-color-primary); font-family: monospace; }
.td-chat-compose { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-textarea { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); resize: vertical; }
.td-checkbox { display: inline-flex; align-items: center; gap: var(--td-space-1); font-size: var(--td-font-xs); color: var(--td-text-secondary); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-loading, .td-empty { text-align: center; color: var(--td-text-secondary); padding: var(--td-space-4); }
</style>
