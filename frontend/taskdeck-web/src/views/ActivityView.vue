<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useAuditStore } from '../store/auditStore'

const route = useRoute()
const audit = useAuditStore()

const viewMode = ref<'board' | 'entity' | 'user'>('board')
const boardId = ref('')
const entityType = ref('')
const entityId = ref('')
const userId = ref('')
const limit = ref(50)

onMounted(() => {
  if (route.params.boardId) {
    viewMode.value = 'board'
    boardId.value = route.params.boardId as string
    fetchHistory()
  }
})

watch(() => route.params, (params) => {
  if (params.boardId && (params.boardId !== boardId.value || viewMode.value !== 'board')) {
    viewMode.value = 'board'
    boardId.value = params.boardId as string
    fetchHistory()
  } else if (params.entityType && params.entityId &&
    (params.entityType !== entityType.value || params.entityId !== entityId.value || viewMode.value !== 'entity')) {
    viewMode.value = 'entity'
    entityType.value = params.entityType as string
    entityId.value = params.entityId as string
    fetchHistory()
  } else if (params.userId && (params.userId !== userId.value || viewMode.value !== 'user')) {
    viewMode.value = 'user'
    userId.value = params.userId as string
    fetchHistory()
  }
})

async function fetchHistory() {
  if (viewMode.value === 'board' && boardId.value) {
    await audit.fetchBoardHistory(boardId.value, limit.value)
  } else if (viewMode.value === 'entity' && entityType.value && entityId.value) {
    await audit.fetchEntityHistory(entityType.value, entityId.value, limit.value)
  } else if (viewMode.value === 'user' && userId.value) {
    await audit.fetchUserHistory(userId.value, limit.value)
  }
}

function handleManualFetch() {
  fetchHistory()
}

function formatTimestamp(ts: string): string {
  return new Date(ts).toLocaleString()
}
</script>

<template>
  <div class="td-activity">
    <h1 class="td-page-title">Activity</h1>

    <!-- Manual query form -->
    <div class="td-activity__controls">
      <div class="td-form-row">
        <select v-model="viewMode" class="td-input">
          <option value="board">Board History</option>
          <option value="entity">Entity History</option>
          <option value="user">User History</option>
        </select>

        <input v-if="viewMode === 'board'" v-model="boardId" type="text" class="td-input" placeholder="Board ID" />
        <template v-if="viewMode === 'entity'">
          <input v-model="entityType" type="text" class="td-input" placeholder="Entity Type" />
          <input v-model="entityId" type="text" class="td-input" placeholder="Entity ID" />
        </template>
        <input v-if="viewMode === 'user'" v-model="userId" type="text" class="td-input" placeholder="User ID" />

        <select v-model.number="limit" class="td-input td-input--sm">
          <option :value="25">25</option>
          <option :value="50">50</option>
          <option :value="100">100</option>
        </select>

        <button class="td-btn td-btn--primary td-btn--sm" @click="handleManualFetch">Fetch</button>
      </div>
    </div>

    <!-- Loading -->
    <div v-if="audit.loading" class="td-loading">Loading activity...</div>

    <!-- Timeline -->
    <div v-else class="td-timeline">
      <div v-if="audit.entries.length === 0" class="td-empty">No activity entries found.</div>
      <div v-for="entry in audit.entries" :key="entry.id" class="td-timeline__entry">
        <div class="td-timeline__dot"></div>
        <div class="td-timeline__content">
          <div class="td-timeline__header">
            <span class="td-timeline__action">{{ entry.action }}</span>
            <span class="td-timeline__time">{{ formatTimestamp(entry.timestamp) }}</span>
          </div>
          <div class="td-timeline__details">
            <span class="td-timeline__entity">{{ entry.entityType }} · {{ entry.entityId }}</span>
            <span v-if="entry.actorName" class="td-timeline__actor">by {{ entry.actorName }}</span>
          </div>
          <div v-if="entry.details" class="td-timeline__message">{{ entry.details }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-activity { max-width: 800px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-activity__controls { background: var(--td-surface-primary); border-radius: var(--td-radius-lg); padding: var(--td-space-4); margin-bottom: var(--td-space-4); border: 1px solid var(--td-border-default); }
.td-form-row { display: flex; gap: var(--td-space-2); flex-wrap: wrap; align-items: center; }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-input--sm { max-width: 80px; }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover { background: var(--td-color-primary-hover); }
.td-loading { text-align: center; padding: var(--td-space-8); color: var(--td-text-secondary); }
.td-empty { text-align: center; padding: var(--td-space-8); color: var(--td-text-tertiary); }
.td-timeline { display: flex; flex-direction: column; gap: 0; }
.td-timeline__entry { display: flex; gap: var(--td-space-4); padding: var(--td-space-4) 0; border-left: 2px solid var(--td-border-default); margin-left: var(--td-space-3); padding-left: var(--td-space-4); position: relative; }
.td-timeline__dot { position: absolute; left: -6px; top: var(--td-space-5); width: 10px; height: 10px; background: var(--td-color-primary); border-radius: 50%; border: 2px solid var(--td-surface-secondary); }
.td-timeline__content { flex: 1; background: var(--td-surface-primary); border-radius: var(--td-radius-md); padding: var(--td-space-3); border: 1px solid var(--td-border-default); }
.td-timeline__header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--td-space-1); }
.td-timeline__action { font-weight: 600; font-size: var(--td-font-sm); color: var(--td-text-primary); }
.td-timeline__time { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-timeline__details { font-size: var(--td-font-xs); color: var(--td-text-secondary); display: flex; gap: var(--td-space-2); }
.td-timeline__entity { font-family: monospace; }
.td-timeline__message { font-size: var(--td-font-sm); color: var(--td-text-secondary); margin-top: var(--td-space-2); padding-top: var(--td-space-2); border-top: 1px solid var(--td-border-default); }
</style>
