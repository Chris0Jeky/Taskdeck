<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useBoardStore } from '../store/boardStore'
import { logError } from '../utils/errorReporting'
import { TdSkeleton } from '../components/ui'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'

const router = useRouter()
const boardStore = useBoardStore()

const newBoardName = ref('')
const showCreateForm = ref(false)

onMounted(async () => {
  // Catch the rethrown error — boardStore.error is already set by handleApiError
  // so the template can display it. Without this catch, Vue treats the unhandled
  // rejection as a lifecycle-hook error and may tear down the component.
  await boardStore.fetchBoards().catch(() => {})
})

async function createBoard() {
  if (!newBoardName.value.trim()) return

  try {
    const board = await boardStore.createBoard({
      name: newBoardName.value,
    })

    newBoardName.value = ''
    showCreateForm.value = false

    // Navigate to the new board
    router.push(`/boards/${board.id}`)
  } catch (error) {
    logError('Failed to create board:', error)
  }
}

function goToBoard(id: string) {
  router.push(`/boards/${id}`)
}
</script>

<template>
  <div class="paper-boards">
    <div class="paper-boards__inner">
      <header class="paper-boards__hero">
        <div class="paper-boards__hero-copy">
          <span class="tk-eyebrow paper-boards__eyebrow">Workspace</span>
          <h1 class="tk-h1 paper-boards__title">My Boards</h1>
        </div>
        <div class="paper-boards__hero-actions">
          <PaperHLBtn
            :variant="showCreateForm ? 'default' : 'ember'"
            @click="showCreateForm = !showCreateForm"
          >
            + New Board
          </PaperHLBtn>
        </div>
      </header>

      <!-- Create Board Form -->
      <section v-if="showCreateForm" class="paper-boards__panel paper-boards__create">
        <h2 class="tk-h3 paper-boards__panel-title">Create New Board</h2>
        <form @submit.prevent="createBoard" class="paper-boards__form">
          <label for="new-board-name" class="sr-only">Board name</label>
          <input
            id="new-board-name"
            v-model="newBoardName"
            type="text"
            placeholder="Board name"
            class="paper-boards__input"
          />
          <PaperHLBtn type="submit" variant="ember">Create</PaperHLBtn>
          <PaperHLBtn variant="ghost" @click="showCreateForm = false">Cancel</PaperHLBtn>
        </form>
      </section>

      <!-- Loading State -->
      <div v-if="boardStore.loading" class="paper-boards__skeleton" role="status" aria-live="polite">
        <span class="sr-only">Loading boards...</span>
        <div class="paper-boards__grid">
          <div v-for="n in 6" :key="n" class="paper-boards__skeleton-card">
            <TdSkeleton width="70%" height="20px" />
            <TdSkeleton width="90%" height="12px" />
            <TdSkeleton width="50%" height="12px" />
            <div class="paper-boards__skeleton-footer">
              <TdSkeleton width="120px" height="10px" />
            </div>
          </div>
        </div>
      </div>

      <!-- Error State -->
      <div v-else-if="boardStore.error" class="paper-boards__error" role="alert">
        {{ boardStore.error }}
      </div>

      <!-- Empty State -->
      <div v-else-if="boardStore.boards.length === 0" class="paper-boards__empty">
        <svg
          class="paper-boards__empty-icon"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
          />
        </svg>
        <h3 class="paper-boards__empty-title">No boards</h3>
        <p class="paper-boards__empty-hint">Get started by creating a new board.</p>
        <div class="paper-boards__empty-actions">
          <PaperHLBtn variant="ember" @click="showCreateForm = true">+ Create Board</PaperHLBtn>
        </div>
      </div>

      <!-- Boards Grid -->
      <div v-else class="paper-boards__grid">
        <!--
          `cursor-pointer` is retained alongside the Paper hook: it is a
          behavioral (not color) Tailwind utility, and tests/e2e/stakeholder-demo
          .spec.ts selects the board card with `div.cursor-pointer`.  Dropping it
          would break that walkthrough for no styling gain.
        -->
        <div
          v-for="board in boardStore.boards"
          :key="board.id"
          role="button"
          tabindex="0"
          :aria-label="`Open board: ${board.name}`"
          class="paper-boards__card cursor-pointer"
          @click="goToBoard(board.id)"
          @keydown.enter="goToBoard(board.id)"
          @keydown.space.prevent="goToBoard(board.id)"
        >
          <h3 class="paper-boards__card-name">
            {{ board.name }}
          </h3>
          <p v-if="board.description" class="paper-boards__card-desc">
            {{ board.description }}
          </p>
          <div v-else class="paper-boards__card-desc paper-boards__card-desc--empty">No description</div>
          <div class="paper-boards__card-meta">
            Created {{ new Date(board.createdAt).toLocaleDateString() }}
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — BoardsListView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   The tokens live under `.paper` / `.paper-night` (the canonical shell), so the
   var() fallbacks keep this surface legible if it is ever rendered outside the
   Paper shell (Legacy/Obsidian "off" mode).  Raw Tailwind color utilities
   (`bg-surface`, `bg-ember`, `text-on-surface`) resolved to Obsidian values and
   are replaced here by tokens per the Option B per-view migration. */

.paper-boards {
  min-height: 100%;
  background: var(--paper, #f3eee5);
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.paper-boards__inner {
  max-width: 1280px;
  margin: 0 auto;
  padding: var(--s-8, 32px) var(--s-4, 16px);
}

/* ── Hero ── */

.paper-boards__hero {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--s-6, 24px);
  margin-bottom: var(--s-8, 32px);
}

.paper-boards__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-boards__eyebrow {
  color: var(--mute, #635c4e);
}

.paper-boards__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

.paper-boards__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2, 8px);
  flex-shrink: 0;
}

/* ── Panels & create form ── */

.paper-boards__panel {
  padding: var(--s-4, 16px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-boards__panel-title {
  margin: 0 0 var(--s-4, 16px);
  font-size: var(--t-lg, 18px);
  color: var(--ink-deep, #0a0908);
}

.paper-boards__create {
  margin-bottom: var(--s-6, 24px);
}

.paper-boards__form {
  display: flex;
  gap: var(--s-3, 12px);
  align-items: center;
  flex-wrap: wrap;
}

.paper-boards__input {
  flex: 1 1 220px;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-boards__input::placeholder {
  color: var(--whisper, #c2bba8);
}

.paper-boards__input:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

/* ── Grid & board cards ── */

.paper-boards__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: var(--s-6, 24px);
}

.paper-boards__card {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  padding: var(--s-6, 24px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  cursor: pointer;
  transition:
    background var(--d-quick, 140ms) var(--ease-paper, ease),
    border-color var(--d-quick, 140ms) var(--ease-paper, ease),
    box-shadow var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-boards__card:hover {
  background: var(--paper-2, #ebe5d8);
  border-color: var(--ink-2, #3a352d);
  box-shadow: var(--shadow-lift, 0 6px 14px -8px #1a181430);
}

.paper-boards__card:focus-visible {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-boards__card-name {
  margin: 0;
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-h3, 22px);
  font-weight: 500;
  color: var(--ink-deep, #0a0908);
}

.paper-boards__card-desc {
  margin: 0;
  font-size: var(--t-md, 13.5px);
  color: var(--ink-2, #3a352d);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.paper-boards__card-desc--empty {
  font-style: italic;
  color: var(--mute, #635c4e);
}

.paper-boards__card-meta {
  margin-top: auto;
  padding-top: var(--s-2, 8px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  letter-spacing: 0.04em;
  color: var(--mute, #635c4e);
}

/* ── Skeleton ── */

.paper-boards__skeleton-card {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  padding: var(--s-6, 24px);
  border-radius: var(--r-3, 6px);
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  min-height: 140px;
}

.paper-boards__skeleton-footer {
  margin-top: auto;
  padding-top: var(--s-3, 12px);
}

/* ── Error & empty states ── */

.paper-boards__error {
  padding: var(--s-4, 16px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--overdue, #8c4a26);
  background: var(--overdue-tint, #ecd9c4);
  color: var(--ember-ink, #6e2810);
  font-size: var(--t-md, 13.5px);
}

.paper-boards__empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: var(--s-12, 56px) var(--s-4, 16px);
}

.paper-boards__empty-icon {
  width: 48px;
  height: 48px;
  color: var(--whisper, #c2bba8);
}

.paper-boards__empty-title {
  margin: var(--s-2, 8px) 0 0;
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 500;
  color: var(--ink-deep, #0a0908);
}

.paper-boards__empty-hint {
  margin: var(--s-1, 4px) 0 0;
  font-size: var(--t-md, 13.5px);
  color: var(--mute, #635c4e);
}

.paper-boards__empty-actions {
  margin-top: var(--s-6, 24px);
}

/* ── Responsive ── */

@media (min-width: 640px) {
  .paper-boards__inner {
    padding-left: var(--s-6, 24px);
    padding-right: var(--s-6, 24px);
  }
}

@media (max-width: 640px) {
  .paper-boards__hero {
    flex-direction: column;
  }
}
</style>
