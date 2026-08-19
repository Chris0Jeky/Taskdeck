<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import { cardsApi } from '../api/cardsApi'
import { useBoardStore } from '../store/boardStore'
import { useSavedViewStore, cardMatchesSavedViewFilter } from '../store/savedViewStore'
import type { SavedView, SavedViewFilter } from '../store/savedViewStore'
import type { Board, Card } from '../types/board'

const route = useRoute()
const router = useRouter()
const boardStore = useBoardStore()
const savedViewStore = useSavedViewStore()

const loading = ref(false)
const allCards = ref<Card[]>([])
const showCreateForm = ref(false)

// New view form state
const newViewName = ref('')
const newViewIcon = ref('V')
const newViewSearchText = ref('')
const newViewDueDateFilter = ref<SavedViewFilter['dueDateFilter']>('all')
const newViewBlockedOnly = ref(false)
const newViewLabelNames = ref('')

// Route param for active view
const routeViewId = computed(() => {
  const id = route.params.viewId
  return typeof id === 'string' ? id : null
})

// Sync route param to store
watch(routeViewId, (id) => {
  savedViewStore.setActiveView(id)
}, { immediate: true })

const activeView = computed(() => savedViewStore.activeView)

// Group filtered cards by board
interface BoardGroup {
  board: Board
  cards: Card[]
}

const filteredCardsByBoard = computed<BoardGroup[]>(() => {
  if (!activeView.value) return []

  const filter = activeView.value.filter
  const matched = allCards.value.filter((card) => cardMatchesSavedViewFilter(card, filter))

  const boardMap = new Map<string, { board: Board; cards: Card[] }>()
  for (const card of matched) {
    if (!boardMap.has(card.boardId)) {
      const board = boardStore.boards.find((b) => b.id === card.boardId)
      if (!board) continue
      boardMap.set(card.boardId, { board, cards: [] })
    }
    boardMap.get(card.boardId)!.cards.push(card)
  }

  return Array.from(boardMap.values())
})

const totalFilteredCount = computed(() =>
  filteredCardsByBoard.value.reduce((sum, bg) => sum + bg.cards.length, 0),
)

async function loadAllCards() {
  loading.value = true
  try {
    await boardStore.fetchBoards()
    const cardPromises = boardStore.boards
      .filter((b) => !b.isArchived)
      .map((board) => cardsApi.getCards(board.id))
    const results = await Promise.allSettled(cardPromises)
    allCards.value = results
      .filter((r): r is PromiseFulfilledResult<Card[]> => r.status === 'fulfilled')
      .flatMap((r) => r.value)
  } catch {
    // Board store handles its own errors
  } finally {
    loading.value = false
  }
}

function selectView(view: SavedView) {
  router.push(`/workspace/views/${view.id}`)
}

function clearView() {
  router.push('/workspace/views')
}

function navigateToCard(card: Card) {
  router.push(`/workspace/boards/${card.boardId}`)
}

function formatDueDate(value: string | null): string {
  if (!value) return 'No due date'
  return new Date(value).toLocaleDateString()
}

function resetCreateForm() {
  newViewName.value = ''
  newViewIcon.value = 'V'
  newViewSearchText.value = ''
  newViewDueDateFilter.value = 'all'
  newViewBlockedOnly.value = false
  newViewLabelNames.value = ''
  showCreateForm.value = false
}

function handleCreateView() {
  if (!newViewName.value.trim()) return

  const labelNames = newViewLabelNames.value
    .split(',')
    .map((l) => l.trim())
    .filter((l) => l.length > 0)

  const view = savedViewStore.createView(
    newViewName.value.trim(),
    newViewIcon.value || 'V',
    {
      searchText: newViewSearchText.value.trim(),
      labelNames,
      dueDateFilter: newViewDueDateFilter.value,
      showBlockedOnly: newViewBlockedOnly.value,
    },
  )

  resetCreateForm()
  selectView(view)
}

function handleDeleteView(viewId: string) {
  savedViewStore.deleteView(viewId)
  if (routeViewId.value === viewId) {
    clearView()
  }
}

onMounted(loadAllCards)
</script>

<template>
  <div class="paper-views">
    <header class="paper-views__hero">
      <div class="paper-views__hero-copy">
        <span class="tk-eyebrow paper-views__eyebrow">Productivity</span>
        <h1 class="tk-h1 paper-views__title">Saved Views</h1>
        <p class="tk-lede paper-views__subtitle">
          Reusable filters for recurring work recovery flows. Click a view to see matching cards across all boards.
        </p>
      </div>
      <div class="paper-views__hero-actions">
        <PaperHLBtn
          :variant="showCreateForm ? 'default' : 'ember'"
          @click="showCreateForm = !showCreateForm"
        >
          {{ showCreateForm ? 'Cancel' : 'New View' }}
        </PaperHLBtn>
        <PaperHLBtn
          v-if="activeView"
          variant="ghost"
          @click="clearView"
        >
          Clear Filter
        </PaperHLBtn>
      </div>
    </header>

    <WorkspaceHelpCallout
      topic="saved-views"
      title="What are Saved Views?"
      description="Saved Views are reusable filters that help you quickly find cards across all your boards. Use the built-in views for common workflows or create your own custom filters."
    />

    <!-- Create form -->
    <section v-if="showCreateForm" class="paper-views__panel paper-views__create-form">
      <h2 class="tk-h3 paper-views__panel-title">Create a custom view</h2>
      <div class="paper-views__form-grid">
        <div class="paper-views__form-field">
          <label class="paper-views__field-label" for="sv-name">Name</label>
          <input
            id="sv-name"
            v-model="newViewName"
            type="text"
            class="paper-views__input"
            placeholder="e.g. My Blocked Tasks"
          />
        </div>
        <div class="paper-views__form-field">
          <label class="paper-views__field-label" for="sv-icon">Icon letter</label>
          <input
            id="sv-icon"
            v-model="newViewIcon"
            type="text"
            class="paper-views__input"
            maxlength="2"
            placeholder="V"
          />
        </div>
        <div class="paper-views__form-field">
          <label class="paper-views__field-label" for="sv-search">Search text</label>
          <input
            id="sv-search"
            v-model="newViewSearchText"
            type="text"
            class="paper-views__input"
            placeholder="Filter by title or description"
          />
        </div>
        <div class="paper-views__form-field">
          <label class="paper-views__field-label" for="sv-due">Due date</label>
          <select id="sv-due" v-model="newViewDueDateFilter" class="paper-views__input">
            <option value="all">All</option>
            <option value="overdue">Overdue</option>
            <option value="due-today">Due today</option>
            <option value="due-week">Due this week</option>
            <option value="no-date">No due date</option>
          </select>
        </div>
        <div class="paper-views__form-field">
          <label class="paper-views__field-label" for="sv-labels">Label names (comma-separated)</label>
          <input
            id="sv-labels"
            v-model="newViewLabelNames"
            type="text"
            class="paper-views__input"
            placeholder="review, urgent"
          />
        </div>
        <div class="paper-views__form-field paper-views__form-field--checkbox">
          <label class="paper-views__checkbox">
            <input v-model="newViewBlockedOnly" type="checkbox" />
            Blocked cards only
          </label>
        </div>
      </div>
      <div class="paper-views__form-actions">
        <PaperHLBtn
          variant="ember"
          :disabled="!newViewName.trim()"
          @click="handleCreateView"
        >
          Create View
        </PaperHLBtn>
        <PaperHLBtn variant="ghost" @click="resetCreateForm">Cancel</PaperHLBtn>
      </div>
    </section>

    <!-- View picker -->
    <section class="paper-views__picker">
      <div class="paper-views__group">
        <h2 class="tk-eyebrow paper-views__group-label">Default Views</h2>
        <div class="paper-views__view-grid">
          <button
            v-for="view in savedViewStore.defaultViews"
            :key="view.id"
            class="paper-views__card"
            :class="{ 'paper-views__card--active': activeView?.id === view.id }"
            @click="selectView(view)"
          >
            <span class="paper-views__card-icon">{{ view.icon }}</span>
            <span class="paper-views__card-name">{{ view.name }}</span>
          </button>
        </div>
      </div>

      <div v-if="savedViewStore.customViews.length > 0" class="paper-views__group">
        <h2 class="tk-eyebrow paper-views__group-label">Custom Views</h2>
        <div class="paper-views__view-grid">
          <div
            v-for="view in savedViewStore.customViews"
            :key="view.id"
            class="paper-views__card-wrapper"
          >
            <button
              class="paper-views__card"
              :class="{ 'paper-views__card--active': activeView?.id === view.id }"
              @click="selectView(view)"
            >
              <span class="paper-views__card-icon">{{ view.icon }}</span>
              <span class="paper-views__card-name">{{ view.name }}</span>
            </button>
            <button
              class="paper-views__delete-btn"
              aria-label="Delete view"
              title="Delete view"
              @click.stop="handleDeleteView(view.id)"
            >
              <span class="material-symbols-outlined text-base">close</span>
            </button>
          </div>
        </div>
      </div>
    </section>

    <!-- Results -->
    <section v-if="activeView" class="paper-views__results">
      <div class="paper-views__panel paper-views__results-header">
        <h2 class="tk-h3 paper-views__panel-title">
          {{ activeView.name }}
          <span class="paper-views__results-count">{{ totalFilteredCount }} card{{ totalFilteredCount === 1 ? '' : 's' }}</span>
        </h2>
      </div>

      <div v-if="loading" class="paper-views__panel paper-views__placeholder" aria-live="polite">
        Loading cards...
      </div>

      <div v-else-if="filteredCardsByBoard.length === 0" class="paper-views__panel paper-views__empty">
        <p>No cards match this view's criteria.</p>
        <p class="paper-views__empty-hint">
          Try adjusting filters or creating cards with matching attributes.
        </p>
      </div>

      <div v-else class="paper-views__board-groups">
        <article
          v-for="group in filteredCardsByBoard"
          :key="group.board.id"
          class="paper-views__panel paper-views__board-group"
        >
          <h3 class="paper-views__board-name">{{ group.board.name }}</h3>
          <div class="paper-views__card-list">
            <button
              v-for="card in group.cards"
              :key="card.id"
              class="paper-views__result-card"
              @click="navigateToCard(card)"
            >
              <span class="paper-views__result-title">{{ card.title }}</span>
              <span class="paper-views__result-meta">
                <span v-if="card.isBlocked" class="paper-views__blocked-badge">Blocked</span>
                <span v-if="card.dueDate">Due {{ formatDueDate(card.dueDate) }}</span>
                <span v-for="label in card.labels" :key="label.id" class="paper-views__label-tag td-dynamic-label" :style="{ '--td-dynamic-color': label.colorHex }">
                  {{ label.name }}
                </span>
              </span>
            </button>
          </div>
        </article>
      </div>
    </section>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — SavedViewsView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens are defined under `.paper` / `.paper-night` (the canonical shell), so
   var() fallbacks keep the surface legible if the view is ever rendered outside
   the Paper shell (Legacy/Obsidian "off" mode). */

.paper-views {
  display: flex;
  flex-direction: column;
  gap: var(--s-5, 20px);
  background: var(--paper, #f3eee5);
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-views__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--s-6, 24px);
  align-items: flex-start;
}

.paper-views__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  max-width: 720px;
}

.paper-views__eyebrow {
  color: var(--ember, #a8421f);
}

.paper-views__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

.paper-views__subtitle {
  margin: 0;
  color: var(--ink-2, #3a352d);
}

.paper-views__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2, 8px);
  flex-shrink: 0;
}

/* ── Panels ── */

.paper-views__panel {
  padding: var(--s-4, 16px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-views__panel-title {
  margin: 0;
  font-size: var(--t-lg, 18px);
  color: var(--ink-deep, #0a0908);
}

/* ── Create form ── */

.paper-views__create-form {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
}

.paper-views__form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--s-3, 12px);
}

.paper-views__form-field {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-views__field-label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #635c4e);
}

.paper-views__input {
  width: 100%;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-views__input:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-views__checkbox {
  display: flex;
  align-items: center;
  gap: var(--s-2, 8px);
  cursor: pointer;
  color: var(--ink-2, #3a352d);
  font-size: var(--t-md, 13.5px);
}

.paper-views__form-actions {
  display: flex;
  gap: var(--s-2, 8px);
}

/* ── View picker ── */

.paper-views__picker {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
}

.paper-views__group {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-views__group-label {
  margin: 0;
  color: var(--mute, #635c4e);
}

.paper-views__view-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: var(--s-3, 12px);
}

.paper-views__card-wrapper {
  position: relative;
}

.paper-views__card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--s-2, 8px);
  width: 100%;
  padding: var(--s-4, 16px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  cursor: pointer;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease),
    border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-views__card:hover {
  background: var(--paper-2, #ebe5d8);
  border-color: var(--ink-2, #3a352d);
}

.paper-views__card--active {
  border-color: var(--ember, #a8421f);
  background: var(--ember-tint, #f0d9c8);
}

.paper-views__card-icon {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-h3, 22px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-views__card-name {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--ink-2, #3a352d);
  text-align: center;
}

.paper-views__delete-btn {
  position: absolute;
  top: var(--s-1, 4px);
  right: var(--s-1, 4px);
  background: transparent;
  border: none;
  color: var(--mute, #635c4e);
  cursor: pointer;
  padding: var(--s-1, 4px);
  border-radius: var(--r-2, 4px);
  transition: color var(--d-quick, 140ms) var(--ease-paper, ease),
    background var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-views__delete-btn:hover {
  color: var(--ember-deep, #7a2e15);
  background: var(--ember-bloom, #a8421f1a);
}

/* ── Results ── */

.paper-views__results {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
}

.paper-views__results-header {
  display: flex;
  align-items: center;
  gap: var(--s-3, 12px);
}

.paper-views__results-count {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  color: var(--mute, #635c4e);
  margin-left: var(--s-2, 8px);
}

.paper-views__placeholder {
  color: var(--mute, #635c4e);
}

.paper-views__empty {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  color: var(--ink-2, #3a352d);
}

.paper-views__empty-hint {
  font-size: var(--t-sm, 12px);
  color: var(--mute, #635c4e);
}

.paper-views__board-groups {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
}

.paper-views__board-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-views__board-name {
  margin: 0;
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-views__card-list {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-views__result-card {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  text-align: left;
  width: 100%;
  padding: var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  cursor: pointer;
  border-left: 2px solid transparent;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease),
    border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-views__result-card:hover {
  background: var(--paper-2, #ebe5d8);
  border-left-color: var(--ember, #a8421f);
}

.paper-views__result-title {
  font-size: var(--t-md, 13.5px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-views__result-meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2, 8px);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #635c4e);
  line-height: 1.5;
}

.paper-views__blocked-badge {
  background: var(--overdue-tint, #ecd9c4);
  color: var(--overdue, #8c4a26);
  padding: 0 var(--s-2, 8px);
  border-radius: var(--r-1, 2px);
  font-weight: 700;
  font-size: var(--t-xs, 10.5px);
}

.paper-views__label-tag {
  padding: 0 var(--s-2, 8px);
  border-radius: var(--r-1, 2px);
  font-weight: 600;
  font-size: var(--t-xs, 10.5px);
}

/* ── Responsive ── */

@media (max-width: 768px) {
  .paper-views__hero {
    flex-direction: column;
  }
}
</style>
