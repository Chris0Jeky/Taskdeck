<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { boardsApi } from '../api/boardsApi'
import { usePermissionsStore } from '../store/permissionsStore'
import { useSessionStore } from '../store/sessionStore'
import { useToastStore } from '../store/toastStore'
import type { BoardRole } from '../types/access'
import type { Board } from '../types/board'
import { normalizeBoardRole } from '../utils/roles'
import { normalizeBoardIdQueryParam } from '../utils/navigation'
import { getErrorDisplay } from '../composables/useErrorMapper'

const props = defineProps<{ boardId?: string | null }>()

const route = useRoute()
const router = useRouter()
const permissions = usePermissionsStore()
const session = useSessionStore()
const toast = useToastStore()

const activeBoardId = ref<string>(normalizeBoardIdQueryParam(props.boardId ?? route.query.boardId))
const availableBoards = ref<Board[]>([])
const loadingBoards = ref(false)
const newUserId = ref('')
const newRole = ref<BoardRole>('Viewer')
const showGrantForm = ref(false)
const granting = ref(false)

const roles: BoardRole[] = ['Owner', 'Admin', 'Editor', 'Viewer']
const accessBusy = computed(() => loadingBoards.value || permissions.loading)
const refreshAccessLabel = computed(() => {
  if (permissions.loading) {
    return 'Refreshing...'
  }

  if (loadingBoards.value) {
    return 'Loading boards...'
  }

  return 'Refresh Access'
})

const boardOptions = computed(() => {
  const options = [...availableBoards.value]
    .sort((left, right) => left.name.localeCompare(right.name))
    .map((board) => ({
      id: board.id,
      label: board.isArchived ? `${board.name} (Archived)` : board.name,
    }))

  if (activeBoardId.value.trim() && !options.some((option) => option.id === activeBoardId.value.trim())) {
    options.unshift({
      id: activeBoardId.value.trim(),
      label: `Deep-linked board (${activeBoardId.value.trim().slice(0, 8)}...)`,
    })
  }

  return options
})

const accessList = computed(() => {
  if (!activeBoardId.value) return []
  return permissions.boardAccess.get(activeBoardId.value) ?? []
})

async function loadBoards() {
  try {
    loadingBoards.value = true
    availableBoards.value = await boardsApi.getBoards()

    if (!activeBoardId.value.trim() && availableBoards.value.length > 0) {
      activeBoardId.value = availableBoards.value[0]!.id
    }
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load boards for access management.').message)
  } finally {
    loadingBoards.value = false
  }
}

async function fetchAccessList() {
  if (!activeBoardId.value.trim()) return
  try {
    await permissions.fetchBoardAccess(activeBoardId.value.trim())
  } catch {
    // Store handles toast + error state.
  }
}

onMounted(async () => {
  await loadBoards()
})

watch(
  () => props.boardId,
  (boardId) => {
    const normalizedBoardId = normalizeBoardIdQueryParam(boardId)
    if (!normalizedBoardId || normalizedBoardId === activeBoardId.value.trim()) return
    activeBoardId.value = normalizedBoardId
  }
)

watch(
  () => route.query.boardId,
  (boardId) => {
    const normalizedBoardId = normalizeBoardIdQueryParam(boardId)
    if (!normalizedBoardId || normalizedBoardId === activeBoardId.value.trim()) return
    activeBoardId.value = normalizedBoardId
  }
)

watch(activeBoardId, (boardId, previousBoardId) => {
  const normalizedBoardId = boardId.trim()
  if (!normalizedBoardId || normalizedBoardId === previousBoardId?.trim()) {
    return
  }

  void fetchAccessList()
}, { immediate: true })

async function handleGrant() {
  if (!activeBoardId.value.trim()) {
    toast.warning('Choose a board first.')
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

function openRoute(path: string) {
  void router.push(path)
}
</script>

<template>
  <div class="td-access">
    <header class="td-panel td-access__hero">
      <div class="td-access__hero-copy">
        <span class="td-access__eyebrow">Advanced</span>
        <h1 class="td-page-title">Board Access</h1>
        <p class="td-access__subtitle">
          Choose a board, then manage who can view or edit it. This is an access-control surface, not the normal
          place where day-to-day work happens.
        </p>
      </div>

      <div class="td-access__hero-actions">
        <button class="td-btn td-btn--primary" :disabled="accessBusy" @click="fetchAccessList">
          {{ refreshAccessLabel }}
        </button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/boards')">Open Boards</button>
      </div>
    </header>

    <section class="td-panel td-access__panel">
      <div class="td-access__header">
        <div>
          <h2 class="td-section-title">Choose a board</h2>
          <p class="td-section-desc">
            Normal flows should not depend on memorized board IDs. Deep links still work, but the picker is the primary
            way to choose a board here.
          </p>
        </div>

        <button class="td-btn td-btn--primary td-btn--sm" @click="showGrantForm = !showGrantForm">
          {{ showGrantForm ? 'Cancel' : '+ Add Member' }}
        </button>
      </div>

      <div class="td-board-selector">
        <label for="board-selector" class="td-label">Board</label>
        <div class="td-board-selector-row">
          <select id="board-selector" v-model="activeBoardId" class="td-input" :disabled="loadingBoards">
            <option value="" disabled>Select board...</option>
            <option v-for="board in boardOptions" :key="board.id" :value="board.id">
              {{ board.label }}
            </option>
          </select>
          <button class="td-btn td-btn--secondary td-btn--sm" :disabled="loadingBoards" @click="loadBoards">
            {{ loadingBoards ? 'Loading...' : 'Reload Boards' }}
          </button>
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

      <div v-if="permissions.loading" class="td-loading">Loading access entries...</div>

      <div v-else class="td-access-list">
        <div v-if="boardOptions.length === 0" class="td-empty">
          <h3 class="td-section-title">No boards available yet</h3>
          <p class="td-section-desc">Create a board first, then come back here to manage access.</p>
          <button class="td-btn td-btn--primary" @click="openRoute('/workspace/boards')">Create or Open Boards</button>
        </div>
        <div v-else-if="!activeBoardId.trim()" class="td-empty">
          <h3 class="td-section-title">Select a board to manage access</h3>
          <p class="td-section-desc">Pick a board above to load current members and roles.</p>
        </div>
        <div v-else-if="accessList.length === 0" class="td-empty">
          <h3 class="td-section-title">No extra members yet</h3>
          <p class="td-section-desc">This board currently only shows the owner path. Add a member when you are ready to share it.</p>
        </div>
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
    </section>
  </div>
</template>

<style scoped>
.td-access {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
  max-width: 960px;
}

.td-access__hero,
.td-access__panel {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-access__hero {
  flex-direction: row;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-6);
}

.td-access__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-access__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-text-tertiary);
}

.td-access__subtitle {
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-access__hero-actions,
.td-board-selector-row,
.td-access-controls {
  display: flex;
  gap: var(--td-space-2);
  align-items: center;
  flex-wrap: wrap;
}

.td-access__header {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-3);
  align-items: flex-start;
}

.td-page-title {
  font-size: var(--td-font-2xl);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-section-title {
  font-size: var(--td-font-lg);
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-section-desc {
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-board-selector,
.td-form-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-grant-form {
  display: flex;
  gap: var(--td-space-3);
  align-items: flex-end;
  flex-wrap: wrap;
  padding: var(--td-space-4);
  background: var(--td-surface-secondary);
  border-radius: var(--td-radius-md);
  border: 1px solid var(--td-border-default);
}

.td-label {
  font-size: var(--td-font-sm);
  font-weight: 500;
  color: var(--td-text-secondary);
}

.td-input {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  min-width: 220px;
  background: var(--td-surface-primary);
}

.td-input--sm {
  min-width: 0;
  padding: var(--td-space-1) var(--td-space-2);
  font-size: var(--td-font-xs);
}

.td-input:focus {
  outline: none;
  border-color: var(--td-border-focus);
  box-shadow: var(--td-focus-ring);
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

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border: 1px solid var(--td-border-default);
}

.td-btn--danger {
  background: var(--td-color-error);
  color: var(--td-text-inverse);
}

.td-btn:hover:not(:disabled) {
  filter: brightness(0.98);
}

.td-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.td-loading,
.td-empty {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  align-items: flex-start;
  padding: var(--td-space-4);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-secondary);
  border: 1px solid var(--td-border-default);
}

.td-access-list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-access-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
}

.td-access-user {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
}

.td-access-user-id {
  font-size: var(--td-font-sm);
  font-family: monospace;
}

.td-badge {
  font-size: var(--td-font-xs);
  padding: 1px 6px;
  border-radius: var(--td-radius-sm);
  font-weight: 600;
}

.td-badge--info {
  background: var(--td-color-info-light);
  color: var(--td-color-info);
}

@media (max-width: 900px) {
  .td-access__hero,
  .td-access__header,
  .td-access-row {
    flex-direction: column;
    align-items: flex-start;
  }

  .td-access-controls {
    width: 100%;
  }

  .td-input {
    width: 100%;
  }
}
</style>
