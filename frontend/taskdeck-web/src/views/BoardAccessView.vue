<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { usePermissionsStore } from '../store/permissionsStore'
import { useSessionStore } from '../store/sessionStore'
import { useToastStore } from '../store/toastStore'
import type { BoardRole } from '../types/access'
import { normalizeBoardRole } from '../utils/roles'

const props = defineProps<{ boardId?: string | null }>()

const route = useRoute()
const permissions = usePermissionsStore()
const session = useSessionStore()
const toast = useToastStore()

const activeBoardId = ref<string>(props.boardId ?? (route.query.boardId as string ?? ''))
const newUserId = ref('')
const newRole = ref<BoardRole>('Viewer')
const showGrantForm = ref(false)
const granting = ref(false)

const roles: BoardRole[] = ['Owner', 'Admin', 'Editor', 'Viewer']

const accessList = computed(() => {
  if (!activeBoardId.value) return []
  return permissions.boardAccess.get(activeBoardId.value) ?? []
})

async function fetchAccessList() {
  if (!activeBoardId.value.trim()) return
  try {
    await permissions.fetchBoardAccess(activeBoardId.value.trim())
  } catch {
    // Store handles toast + error state.
  }
}

onMounted(fetchAccessList)

watch(
  () => props.boardId,
  (boardId) => {
    if (!boardId) return
    activeBoardId.value = boardId
    fetchAccessList()
  }
)

watch(
  () => route.query.boardId,
  (boardId) => {
    if (typeof boardId !== 'string' || !boardId.trim()) return
    activeBoardId.value = boardId.trim()
    fetchAccessList()
  }
)

async function handleGrant() {
  if (!activeBoardId.value.trim()) {
    toast.warning('Please enter a board ID first.')
    return
  }
  if (!newUserId.value.trim()) {
    toast.warning('Please enter a user ID.')
    return
  }

  try {
    granting.value = true
    await permissions.grantAccess(activeBoardId.value.trim(), {
      userId: newUserId.value.trim(),
      role: newRole.value,
    })
    newUserId.value = ''
    showGrantForm.value = false
  } catch {
    // Store handles toast + error state.
  } finally {
    granting.value = false
  }
}

async function handleRoleChange(accessId: string, role: string) {
  if (!activeBoardId.value.trim()) return
  const normalizedRole = normalizeBoardRole(role as BoardRole)
  try {
    await permissions.updateAccess(activeBoardId.value.trim(), accessId, { role: normalizedRole })
  } catch {
    // Store handles toast + error state.
  }
}

async function handleRevoke(accessId: string) {
  if (!activeBoardId.value.trim()) return
  if (confirm('Are you sure you want to revoke this access?')) {
    try {
      await permissions.revokeAccess(activeBoardId.value.trim(), accessId)
    } catch {
      // Store handles toast + error state.
    }
  }
}
</script>

<template>
  <div class="td-access-panel">
    <div class="td-access-header">
      <h1 class="td-section-title">Board Access</h1>
      <button class="td-btn td-btn--primary td-btn--sm" @click="showGrantForm = !showGrantForm">
        {{ showGrantForm ? 'Cancel' : '+ Add Member' }}
      </button>
    </div>

    <div class="td-board-selector">
      <label for="board-id" class="td-label">Board ID</label>
      <div class="td-board-selector-row">
        <input id="board-id" v-model="activeBoardId" type="text" class="td-input" placeholder="Enter board ID" />
        <button class="td-btn td-btn--secondary td-btn--sm" @click="fetchAccessList">Load</button>
      </div>
    </div>

    <div v-if="showGrantForm" class="td-grant-form">
      <div class="td-form-group">
        <label for="grant-user" class="td-label">User ID</label>
        <input id="grant-user" v-model="newUserId" type="text" class="td-input" placeholder="Enter user ID" />
      </div>
      <div class="td-form-group">
        <label for="grant-role" class="td-label">Role</label>
        <select id="grant-role" v-model="newRole" class="td-input">
          <option v-for="r in roles" :key="r" :value="r">{{ r }}</option>
        </select>
      </div>
      <button class="td-btn td-btn--primary td-btn--sm" :disabled="granting" @click="handleGrant">
        {{ granting ? 'Granting...' : 'Grant Access' }}
      </button>
    </div>

    <div v-if="permissions.loading" class="td-loading">Loading...</div>

    <div v-else class="td-access-list">
      <div v-if="!activeBoardId.trim()" class="td-empty">Enter a board ID to view access entries.</div>
      <div v-else-if="accessList.length === 0" class="td-empty">No access entries found.</div>
      <div v-for="entry in accessList" :key="entry.id" class="td-access-row">
        <div class="td-access-user">
          <span class="td-access-user-id">{{ entry.userId }}</span>
          <span v-if="entry.userId === session.userId" class="td-badge td-badge--info">You</span>
        </div>
        <div class="td-access-controls">
          <select
            :value="normalizeBoardRole(entry.role)"
            class="td-input td-input--sm"
            :disabled="entry.userId === session.userId"
            @change="handleRoleChange(entry.id, ($event.target as HTMLSelectElement).value)"
          >
            <option v-for="r in roles" :key="r" :value="r">{{ r }}</option>
          </select>
          <button
            class="td-btn td-btn--danger td-btn--sm"
            :disabled="entry.userId === session.userId"
            aria-label="Revoke access"
            @click="handleRevoke(entry.id)"
          >
            X
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-access-panel { background: var(--td-surface-primary); border-radius: var(--td-radius-lg); padding: var(--td-space-6); border: 1px solid var(--td-border-default); max-width: 840px; }
.td-access-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--td-space-4); }
.td-section-title { font-size: var(--td-font-lg); font-weight: 600; color: var(--td-text-primary); }
.td-board-selector { margin-bottom: var(--td-space-4); }
.td-board-selector-row { display: flex; gap: var(--td-space-2); align-items: center; }
.td-grant-form { display: flex; gap: var(--td-space-3); align-items: flex-end; margin-bottom: var(--td-space-4); padding: var(--td-space-4); background: var(--td-surface-secondary); border-radius: var(--td-radius-md); }
.td-form-group { display: flex; flex-direction: column; gap: var(--td-space-1); flex: 1; }
.td-label { font-size: var(--td-font-sm); font-weight: 500; color: var(--td-text-secondary); }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-input--sm { padding: var(--td-space-1) var(--td-space-2); font-size: var(--td-font-xs); }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn--secondary:hover { background: var(--td-surface-hover); }
.td-btn--danger { background: var(--td-color-error); color: var(--td-text-inverse); }
.td-btn--danger:hover:not(:disabled) { background: #dc2626; }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-loading { text-align: center; padding: var(--td-space-6); color: var(--td-text-secondary); }
.td-empty { text-align: center; padding: var(--td-space-6); color: var(--td-text-tertiary); }
.td-access-list { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-access-row { display: flex; justify-content: space-between; align-items: center; padding: var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); }
.td-access-user { display: flex; align-items: center; gap: var(--td-space-2); }
.td-access-user-id { font-size: var(--td-font-sm); font-family: monospace; }
.td-access-controls { display: flex; align-items: center; gap: var(--td-space-2); }
.td-badge { font-size: var(--td-font-xs); padding: 1px 6px; border-radius: var(--td-radius-sm); font-weight: 600; }
.td-badge--info { background: var(--td-color-info-light); color: var(--td-color-info); }
</style>
