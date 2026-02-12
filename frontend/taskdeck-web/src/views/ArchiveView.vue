<script setup lang="ts">
import { ref } from 'vue'
import { useToastStore } from '../store/toastStore'

const toast = useToastStore()
const loading = ref(false)

const archiveItems = ref<Array<{
  entityType: string
  entityId: string
  name: string
  boardContext: string
  archivedAt: string
  archivedBy: string | null
}>>([])

async function handleRestore(entityType: string, _entityId: string) {
  if (confirm(`Restore this ${entityType}?`)) {
    toast.info('Archive restore endpoints not yet available. Will use POST /api/archive/{entityType}/{id}/restore')
  }
}
</script>

<template>
  <div class="td-archive">
    <h1 class="td-page-title">Archive</h1>

    <div class="td-panel">
      <div v-if="loading" class="td-loading">Loading archive...</div>

      <div v-else-if="archiveItems.length === 0" class="td-placeholder">
        <div class="td-placeholder__icon">📦</div>
        <h3>Archive Recovery</h3>
        <p>Archived boards, columns, and cards will appear here when the archive recovery API endpoints are implemented.</p>
        <p class="td-placeholder__detail">
          This view will support: browsing archived entities, restoring with conflict checks, and navigating to restored entities.
        </p>
        <p class="td-placeholder__detail">
          Required endpoints: GET /api/archive/items, POST /api/archive/{'{entityType}'}/{'{id}'}/restore
        </p>
      </div>

      <div v-else class="td-archive-list">
        <div v-for="item in archiveItems" :key="`${item.entityType}-${item.entityId}`" class="td-archive-row">
          <div class="td-archive-info">
            <span class="td-badge">{{ item.entityType }}</span>
            <span class="td-archive-name">{{ item.name }}</span>
            <span class="td-archive-meta">in {{ item.boardContext }} · archived {{ new Date(item.archivedAt).toLocaleDateString() }}</span>
          </div>
          <button class="td-btn td-btn--primary td-btn--sm" @click="handleRestore(item.entityType, item.entityId)">
            Restore
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-archive { max-width: 800px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-6); }
.td-loading { text-align: center; padding: var(--td-space-6); color: var(--td-text-secondary); }
.td-placeholder { text-align: center; padding: var(--td-space-8); }
.td-placeholder__icon { font-size: 3rem; margin-bottom: var(--td-space-4); }
.td-placeholder h3 { font-size: var(--td-font-lg); font-weight: 600; margin-bottom: var(--td-space-2); }
.td-placeholder p { font-size: var(--td-font-sm); color: var(--td-text-secondary); margin-bottom: var(--td-space-2); }
.td-placeholder__detail { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-archive-list { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-archive-row { display: flex; justify-content: space-between; align-items: center; padding: var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); }
.td-archive-info { display: flex; align-items: center; gap: var(--td-space-3); }
.td-archive-name { font-weight: 500; font-size: var(--td-font-sm); }
.td-archive-meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-badge { font-size: var(--td-font-xs); padding: 1px 6px; border-radius: var(--td-radius-sm); font-weight: 600; background: var(--td-surface-tertiary); color: var(--td-text-secondary); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover { background: var(--td-color-primary-hover); }
</style>
