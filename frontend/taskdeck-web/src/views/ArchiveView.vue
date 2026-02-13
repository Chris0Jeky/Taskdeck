<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { archiveApi } from '../api/archiveApi'
import { useToastStore } from '../store/toastStore'
import type { ArchiveItem } from '../types/archive'
import { normalizeRestoreStatus } from '../utils/archive'
import { getErrorDisplay } from '../composables/useErrorMapper'

const toast = useToastStore()
const loading = ref(false)
const restoreBusyId = ref<string | null>(null)
const archiveItems = ref<ArchiveItem[]>([])
const entityTypeFilter = ref<'all' | 'board' | 'column' | 'card'>('all')

const filteredItems = computed(() => {
  if (entityTypeFilter.value === 'all') {
    return archiveItems.value
  }

  return archiveItems.value.filter(item => item.entityType.toLowerCase() === entityTypeFilter.value)
})

async function loadArchive() {
  try {
    loading.value = true
    archiveItems.value = await archiveApi.getItems({
      entityType: entityTypeFilter.value === 'all' ? undefined : entityTypeFilter.value,
      limit: 200,
    })
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load archive items').message)
  } finally {
    loading.value = false
  }
}

async function handleRestore(item: ArchiveItem) {
  if (!confirm(`Restore ${item.entityType} "${item.name}"?`)) {
    return
  }

  try {
    restoreBusyId.value = item.id
    const result = await archiveApi.restoreItem(item.entityType, item.entityId, {
      targetBoardId: null,
      restoreMode: 0,
      conflictStrategy: 0,
    })

    if (!result.success) {
      toast.error(result.errorMessage ?? 'Restore failed')
      return
    }

    archiveItems.value = archiveItems.value.filter(existing => existing.id !== item.id)
    toast.success(`Restored "${result.resolvedName ?? item.name}"`)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to restore archive item').message)
  } finally {
    restoreBusyId.value = null
  }
}

onMounted(() => {
  loadArchive().catch(() => {
    // Error handling done in loadArchive.
  })
})
</script>

<template>
  <div class="td-archive">
    <h1 class="td-page-title">Archive</h1>

    <div class="td-panel">
      <div class="td-toolbar">
        <select v-model="entityTypeFilter" class="td-input" @change="loadArchive">
          <option value="all">All types</option>
          <option value="board">Boards</option>
          <option value="column">Columns</option>
          <option value="card">Cards</option>
        </select>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="loadArchive" :disabled="loading">Refresh</button>
      </div>

      <div v-if="loading" class="td-loading">Loading archive...</div>

      <div v-else-if="filteredItems.length === 0" class="td-empty">
        No archived items found.
      </div>

      <div v-else class="td-archive-list">
        <div v-for="item in filteredItems" :key="item.id" class="td-archive-row">
          <div class="td-archive-info">
            <span class="td-badge">{{ item.entityType }}</span>
            <span class="td-archive-name">{{ item.name }}</span>
            <span class="td-archive-meta">
              board {{ item.boardId }} | status {{ normalizeRestoreStatus(item.restoreStatus) }} | archived
              {{ new Date(item.archivedAt).toLocaleString() }}
            </span>
          </div>
          <button
            class="td-btn td-btn--primary td-btn--sm"
            @click="handleRestore(item)"
            :disabled="restoreBusyId === item.id || normalizeRestoreStatus(item.restoreStatus) !== 'Available'"
          >
            {{ restoreBusyId === item.id ? 'Restoring...' : 'Restore' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-archive { max-width: 960px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-6); }
.td-toolbar { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-4); }
.td-loading, .td-empty { text-align: center; padding: var(--td-space-6); color: var(--td-text-secondary); }
.td-archive-list { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-archive-row { display: flex; justify-content: space-between; align-items: center; gap: var(--td-space-3); padding: var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); }
.td-archive-info { display: flex; align-items: center; gap: var(--td-space-3); min-width: 0; }
.td-archive-name { font-weight: 500; font-size: var(--td-font-sm); }
.td-archive-meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.td-badge { font-size: var(--td-font-xs); padding: 1px 6px; border-radius: var(--td-radius-sm); font-weight: 600; background: var(--td-surface-tertiary); color: var(--td-text-secondary); text-transform: uppercase; }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
</style>
