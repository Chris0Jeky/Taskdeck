<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { boardsApi } from '../api/boardsApi'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
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
const newIdentifier = ref('')
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
  if (!newIdentifier.value.trim()) {
    toast.warning('Please enter an email or username.')
    return
  }

  try {
    granting.value = true
    await permissions.grantAccess(activeBoardId.value.trim(), {
      identifier: newIdentifier.value.trim(),
      role: newRole.value,
    })
    newIdentifier.value = ''
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
  <div class="paper-access">
    <header class="paper-access__hero">
      <div class="paper-access__hero-copy">
        <span class="tk-eyebrow paper-access__eyebrow">Advanced</span>
        <h1 class="tk-h1 paper-access__title">Board Access</h1>
        <p class="tk-lede paper-access__subtitle">
          Choose a board, then manage who can view or edit it. This is an access-control surface, not the normal
          place where day-to-day work happens.
        </p>
      </div>

      <div class="paper-access__hero-actions">
        <PaperHLBtn variant="ember" :disabled="accessBusy" @click="fetchAccessList">
          {{ refreshAccessLabel }}
        </PaperHLBtn>
        <PaperHLBtn @click="openRoute('/workspace/boards')">Open Boards</PaperHLBtn>
      </div>
    </header>

    <WorkspaceHelpCallout
      topic="board-access-selectors"
      title="Why use the board selector here?"
      description="Choose the board from the selector instead of memorizing IDs. Access is an advanced sharing surface, while normal capture, review, and board work should still happen in Home, Inbox, Review, and Boards."
    >
      <template #actions>
        <PaperHLBtn @click="openRoute('/workspace/boards')">Open Boards</PaperHLBtn>
        <PaperHLBtn @click="openRoute('/workspace/review')">Open Review</PaperHLBtn>
      </template>
    </WorkspaceHelpCallout>

    <section class="paper-access__panel">
      <div class="paper-access__header">
        <div>
          <h2 class="tk-h3 paper-access__panel-title">Choose a board</h2>
          <p class="paper-access__panel-desc">
            Normal flows should not depend on memorized board IDs. Deep links still work, but the picker is the primary
            way to choose a board here.
          </p>
        </div>

        <PaperHLBtn variant="ember" @click="showGrantForm = !showGrantForm">
          {{ showGrantForm ? 'Cancel' : '+ Add Member' }}
        </PaperHLBtn>
      </div>

      <div class="paper-access__board-selector">
        <label for="board-selector" class="paper-access__label">Board</label>
        <div class="paper-access__board-selector-row">
          <select id="board-selector" v-model="activeBoardId" class="paper-access__input" :disabled="loadingBoards">
            <option value="" disabled>Select board...</option>
            <option v-for="board in boardOptions" :key="board.id" :value="board.id">
              {{ board.label }}
            </option>
          </select>
          <PaperHLBtn :disabled="loadingBoards" @click="loadBoards">
            {{ loadingBoards ? 'Loading...' : 'Reload Boards' }}
          </PaperHLBtn>
        </div>
      </div>

      <div v-if="showGrantForm" class="paper-access__grant-form">
        <div class="paper-access__form-group">
          <label for="grant-user" class="paper-access__label">Email or username</label>
          <input
            id="grant-user"
            v-model="newIdentifier"
            type="text"
            class="paper-access__input"
            placeholder="Enter email or username"
            autocomplete="off"
          />
        </div>
        <div class="paper-access__form-group">
          <label for="grant-role" class="paper-access__label">Role</label>
          <select id="grant-role" v-model="newRole" class="paper-access__input">
            <option v-for="r in roles" :key="r" :value="r">{{ r }}</option>
          </select>
        </div>
        <PaperHLBtn variant="ember" :disabled="granting" @click="handleGrant">
          {{ granting ? 'Granting...' : 'Grant Access' }}
        </PaperHLBtn>
      </div>

      <div v-if="permissions.loading" class="paper-access__notice">Loading access entries...</div>

      <div v-else class="paper-access__list">
        <div v-if="boardOptions.length === 0" class="paper-access__notice">
          <h3 class="tk-h3 paper-access__notice-title">No boards available yet</h3>
          <p class="paper-access__panel-desc">Create a board first, then come back here to manage access.</p>
          <PaperHLBtn variant="ember" @click="openRoute('/workspace/boards')">Create or Open Boards</PaperHLBtn>
        </div>
        <div v-else-if="!activeBoardId.trim()" class="paper-access__notice">
          <h3 class="tk-h3 paper-access__notice-title">Select a board to manage access</h3>
          <p class="paper-access__panel-desc">Pick a board above to load current members and roles.</p>
        </div>
        <div v-else-if="accessList.length === 0" class="paper-access__notice">
          <h3 class="tk-h3 paper-access__notice-title">No extra members yet</h3>
          <p class="paper-access__panel-desc">This board currently only shows the owner path. Add a member when you are ready to share it.</p>
        </div>
        <div v-for="entry in accessList" :key="entry.id" class="paper-access__row">
          <div class="paper-access__user">
            <span class="paper-access__user-id">{{ entry.userId }}</span>
            <span v-if="entry.userId === session.userId" class="paper-access__badge">You</span>
          </div>
          <div class="paper-access__controls">
            <select
              :value="normalizeBoardRole(entry.role)"
              :aria-label="`Role for ${entry.userId}`"
              class="paper-access__input paper-access__input--sm"
              :disabled="entry.userId === session.userId"
              @change="handleRoleChange(entry.id, ($event.target as HTMLSelectElement).value)"
            >
              <option v-for="r in roles" :key="r" :value="r">{{ r }}</option>
            </select>
            <button
              class="paper-access__revoke-btn"
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
/* ── Paper & Graphite — BoardAccessView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night` in paper-tokens.css and are NOT
   defined at :root, so outside the Paper shell (Legacy/Obsidian "off" mode)
   every var() resolves to its literal fallback. The substrate line on the root —
   `background: var(--paper, #f3eee5)` painted alongside `color: var(--ink,
   #1a1814)` — is what keeps the text legible in Legacy: without it the near-black
   ink lands on AppShell's Obsidian `--td-surface-base` (#131313) at ~1.05:1. It
   is a no-op under `.paper` / `.paper-night`, where `.td-shell--paper
   .td-content` already paints `var(--paper)`.
   Paper typography (the `tk-*` classes) is scoped as `.paper .tk-*` /
   `.paper-night .tk-*` and intentionally does NOT render in Legacy mode — only
   legibility is preserved there, not the Paper type ladder.

   WorkspaceHelpCallout is a shared component with its own chrome and is not
   restyled here; only the buttons this view passes into its #actions slot
   become Paper hairline buttons. */

.paper-access {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
  max-width: 960px;
  font-family: var(--sans, system-ui, sans-serif);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-access__hero {
  display: flex;
  flex-direction: row;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--s-6, 24px);
}

.paper-access__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  max-width: 720px;
}

.paper-access__eyebrow {
  color: var(--ember, #a8421f);
}

.paper-access__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

.paper-access__subtitle {
  margin: 0;
  color: var(--ink-2, #3a352d);
}

.paper-access__hero-actions,
.paper-access__board-selector-row,
.paper-access__controls {
  display: flex;
  gap: var(--s-2, 8px);
  align-items: center;
  flex-wrap: wrap;
}

/* ── Panel ── */

.paper-access__panel {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  padding: var(--s-5, 20px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-access__header {
  display: flex;
  justify-content: space-between;
  gap: var(--s-3, 12px);
  align-items: flex-start;
}

.paper-access__panel-title {
  margin: 0;
  font-size: var(--t-lg, 18px);
  color: var(--ink-deep, #0a0908);
}

.paper-access__panel-desc {
  margin: 0;
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
  line-height: 1.55;
}

/* ── Forms ── */

.paper-access__board-selector,
.paper-access__form-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-access__grant-form {
  display: flex;
  gap: var(--s-3, 12px);
  align-items: flex-end;
  flex-wrap: wrap;
  padding: var(--s-4, 16px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
}

.paper-access__label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #6c6557);
}

.paper-access__input {
  min-width: 220px;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-access__input--sm {
  min-width: 0;
  padding: var(--s-1, 4px) var(--s-2, 8px);
  font-size: var(--t-sm, 12px);
}

.paper-access__input:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

/* ── Notices (loading + empty states) ── */

.paper-access__notice {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  align-items: flex-start;
  padding: var(--s-4, 16px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink-2, #3a352d);
  font-size: var(--t-md, 13.5px);
}

.paper-access__notice-title {
  margin: 0;
  font-size: var(--t-lg, 18px);
  color: var(--ink-deep, #0a0908);
}

/* ── Access entries ── */

.paper-access__list {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-access__row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
  padding: var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper-card, #fbf7ee);
}

.paper-access__user {
  display: flex;
  align-items: center;
  gap: var(--s-2, 8px);
}

.paper-access__user-id {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
  color: var(--ink, #1a1814);
}

.paper-access__badge {
  padding: 1px 6px;
  border-radius: var(--r-1, 2px);
  border: 1px solid var(--ember, #a8421f);
  background: var(--ember-tint, #f0d9c8);
  color: var(--ember-ink, #6e2810);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
}

/* Destructive action: ember-deep outline, never a saturated red fill. */
.paper-access__revoke-btn {
  padding: var(--s-1, 4px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--ember-deep, #7a2e15);
  background: transparent;
  color: var(--ember-deep, #7a2e15);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-sm, 12px);
  font-weight: 600;
  cursor: pointer;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-access__revoke-btn:hover:not(:disabled) {
  background: var(--ember-bloom, #a8421f1a);
}

.paper-access__revoke-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

@media (max-width: 900px) {
  .paper-access__hero,
  .paper-access__header,
  .paper-access__row {
    flex-direction: column;
    align-items: flex-start;
  }

  .paper-access__controls {
    width: 100%;
  }

  .paper-access__input {
    width: 100%;
  }
}
</style>
