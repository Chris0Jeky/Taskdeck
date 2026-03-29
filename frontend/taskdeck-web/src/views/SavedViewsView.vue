<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
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
  <div class="td-saved-views">
    <header class="td-saved-views__hero td-panel">
      <div class="td-saved-views__hero-copy">
        <span class="td-saved-views__eyebrow">Productivity</span>
        <h1 class="td-page-title">Saved Views</h1>
        <p class="td-saved-views__subtitle">
          Reusable filters for recurring work recovery flows. Click a view to see matching cards across all boards.
        </p>
      </div>
      <div class="td-saved-views__hero-actions">
        <button
          class="td-btn td-btn--primary"
          @click="showCreateForm = !showCreateForm"
        >
          {{ showCreateForm ? 'Cancel' : 'New View' }}
        </button>
        <button
          v-if="activeView"
          class="td-btn td-btn--secondary"
          @click="clearView"
        >
          Clear Filter
        </button>
      </div>
    </header>

    <WorkspaceHelpCallout
      topic="saved-views"
      title="What are Saved Views?"
      description="Saved Views are reusable filters that help you quickly find cards across all your boards. Use the built-in views for common workflows or create your own custom filters."
    />

    <!-- Create form -->
    <section v-if="showCreateForm" class="td-panel td-saved-views__create-form">
      <h2 class="td-section-title">Create a custom view</h2>
      <div class="td-saved-views__form-grid">
        <div class="td-saved-views__form-field">
          <label for="sv-name">Name</label>
          <input
            id="sv-name"
            v-model="newViewName"
            type="text"
            class="td-input"
            placeholder="e.g. My Blocked Tasks"
          />
        </div>
        <div class="td-saved-views__form-field">
          <label for="sv-icon">Icon letter</label>
          <input
            id="sv-icon"
            v-model="newViewIcon"
            type="text"
            class="td-input"
            maxlength="2"
            placeholder="V"
          />
        </div>
        <div class="td-saved-views__form-field">
          <label for="sv-search">Search text</label>
          <input
            id="sv-search"
            v-model="newViewSearchText"
            type="text"
            class="td-input"
            placeholder="Filter by title or description"
          />
        </div>
        <div class="td-saved-views__form-field">
          <label for="sv-due">Due date</label>
          <select id="sv-due" v-model="newViewDueDateFilter" class="td-input">
            <option value="all">All</option>
            <option value="overdue">Overdue</option>
            <option value="due-today">Due today</option>
            <option value="due-week">Due this week</option>
            <option value="no-date">No due date</option>
          </select>
        </div>
        <div class="td-saved-views__form-field">
          <label for="sv-labels">Label names (comma-separated)</label>
          <input
            id="sv-labels"
            v-model="newViewLabelNames"
            type="text"
            class="td-input"
            placeholder="review, urgent"
          />
        </div>
        <div class="td-saved-views__form-field td-saved-views__form-field--checkbox">
          <label>
            <input v-model="newViewBlockedOnly" type="checkbox" />
            Blocked cards only
          </label>
        </div>
      </div>
      <div class="td-saved-views__form-actions">
        <button
          class="td-btn td-btn--primary"
          :disabled="!newViewName.trim()"
          @click="handleCreateView"
        >
          Create View
        </button>
        <button class="td-btn td-btn--ghost" @click="resetCreateForm">Cancel</button>
      </div>
    </section>

    <!-- View picker -->
    <section class="td-saved-views__picker">
      <div class="td-saved-views__group">
        <h2 class="td-saved-views__group-label">Default Views</h2>
        <div class="td-saved-views__view-grid">
          <button
            v-for="view in savedViewStore.defaultViews"
            :key="view.id"
            class="td-saved-views__card"
            :class="{ 'td-saved-views__card--active': activeView?.id === view.id }"
            @click="selectView(view)"
          >
            <span class="td-saved-views__card-icon">{{ view.icon }}</span>
            <span class="td-saved-views__card-name">{{ view.name }}</span>
          </button>
        </div>
      </div>

      <div v-if="savedViewStore.customViews.length > 0" class="td-saved-views__group">
        <h2 class="td-saved-views__group-label">Custom Views</h2>
        <div class="td-saved-views__view-grid">
          <div
            v-for="view in savedViewStore.customViews"
            :key="view.id"
            class="td-saved-views__card-wrapper"
          >
            <button
              class="td-saved-views__card"
              :class="{ 'td-saved-views__card--active': activeView?.id === view.id }"
              @click="selectView(view)"
            >
              <span class="td-saved-views__card-icon">{{ view.icon }}</span>
              <span class="td-saved-views__card-name">{{ view.name }}</span>
            </button>
            <button
              class="td-saved-views__delete-btn"
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
    <section v-if="activeView" class="td-saved-views__results">
      <div class="td-saved-views__results-header td-panel">
        <h2 class="td-section-title">
          {{ activeView.name }}
          <span class="td-saved-views__results-count">{{ totalFilteredCount }} card{{ totalFilteredCount === 1 ? '' : 's' }}</span>
        </h2>
      </div>

      <div v-if="loading" class="td-panel td-saved-views__placeholder" aria-live="polite">
        Loading cards...
      </div>

      <div v-else-if="filteredCardsByBoard.length === 0" class="td-panel td-saved-views__empty">
        <p>No cards match this view's criteria.</p>
        <p class="td-saved-views__empty-hint">
          Try adjusting filters or creating cards with matching attributes.
        </p>
      </div>

      <div v-else class="td-saved-views__board-groups">
        <article
          v-for="group in filteredCardsByBoard"
          :key="group.board.id"
          class="td-panel td-saved-views__board-group"
        >
          <h3 class="td-saved-views__board-name">{{ group.board.name }}</h3>
          <div class="td-saved-views__card-list">
            <button
              v-for="card in group.cards"
              :key="card.id"
              class="td-saved-views__result-card"
              @click="navigateToCard(card)"
            >
              <span class="td-saved-views__result-title">{{ card.title }}</span>
              <span class="td-saved-views__result-meta">
                <span v-if="card.isBlocked" class="td-saved-views__blocked-badge">Blocked</span>
                <span v-if="card.dueDate">Due {{ formatDueDate(card.dueDate) }}</span>
                <span v-for="label in card.labels" :key="label.id" class="td-saved-views__label-tag" :style="{ background: label.colorHex + '33', color: label.colorHex }">
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
/* ── Obsidian & Ember — SavedViewsView ── */

.td-saved-views {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-5);
}

/* ── Hero panel ── */

.td-saved-views__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-6);
  align-items: flex-start;
}

.td-saved-views__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-saved-views__eyebrow {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.2em;
  text-transform: uppercase;
  color: var(--td-color-ember);
}

.td-saved-views__subtitle {
  font-size: var(--td-font-base);
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-saved-views__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

/* ── Create form ── */

.td-saved-views__create-form {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-saved-views__form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--td-space-3);
}

.td-saved-views__form-field {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-saved-views__form-field label {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--td-text-secondary);
}

.td-saved-views__form-field--checkbox label {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  text-transform: none;
  letter-spacing: normal;
  cursor: pointer;
}

.td-saved-views__form-actions {
  display: flex;
  gap: var(--td-space-2);
}

/* ── View picker ── */

.td-saved-views__picker {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-saved-views__group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-saved-views__group-label {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  color: var(--td-text-tertiary);
  letter-spacing: 0.2em;
  text-transform: uppercase;
}

.td-saved-views__view-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: var(--td-space-3);
}

.td-saved-views__card-wrapper {
  position: relative;
}

.td-saved-views__card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--td-space-2);
  width: 100%;
  padding: var(--td-space-4);
  border-radius: var(--td-radius-lg);
  border: 0.5px solid var(--td-border-ghost);
  background: var(--td-surface-container);
  cursor: pointer;
  transition: background var(--td-transition-fast), border-color var(--td-transition-fast);
}

.td-saved-views__card:hover {
  background: var(--td-surface-bright);
}

.td-saved-views__card--active {
  border-color: var(--td-color-ember);
  background: linear-gradient(to bottom, var(--td-color-ember-dim), var(--td-surface-container));
}

.td-saved-views__card-icon {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-2xl);
  font-weight: 800;
  color: var(--td-text-primary);
}

.td-saved-views__card-name {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--td-text-secondary);
  text-align: center;
}

.td-saved-views__delete-btn {
  position: absolute;
  top: var(--td-space-1);
  right: var(--td-space-1);
  background: transparent;
  border: none;
  color: var(--td-text-tertiary);
  cursor: pointer;
  padding: var(--td-space-1);
  border-radius: var(--td-radius-md);
  transition: color var(--td-transition-fast), background var(--td-transition-fast);
}

.td-saved-views__delete-btn:hover {
  color: var(--td-color-error);
  background: var(--td-surface-container-high);
}

/* ── Results ── */

.td-saved-views__results {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-saved-views__results-header {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
}

.td-saved-views__results-count {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 600;
  color: var(--td-text-tertiary);
  margin-left: var(--td-space-2);
}

.td-saved-views__placeholder {
  color: var(--td-text-tertiary);
}

.td-saved-views__empty {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  color: var(--td-text-secondary);
}

.td-saved-views__empty-hint {
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
}

.td-saved-views__board-groups {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-saved-views__board-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-saved-views__board-name {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-base);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-saved-views__card-list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-saved-views__result-card {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  text-align: left;
  width: 100%;
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 0.5px solid var(--td-border-ghost);
  background: var(--td-surface-container-high);
  cursor: pointer;
  border-left: 2px solid transparent;
  transition: background var(--td-transition-fast), border-color var(--td-transition-fast);
}

.td-saved-views__result-card:hover {
  background: var(--td-surface-bright);
  border-left-color: var(--td-color-ember);
}

.td-saved-views__result-title {
  font-size: var(--td-font-sm);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-saved-views__result-meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  line-height: 1.5;
}

.td-saved-views__blocked-badge {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
  padding: 0 var(--td-space-2);
  border-radius: var(--td-radius-sm);
  font-weight: 700;
  font-size: var(--td-font-xs);
}

.td-saved-views__label-tag {
  padding: 0 var(--td-space-2);
  border-radius: var(--td-radius-sm);
  font-weight: 600;
  font-size: var(--td-font-xs);
}

/* ── Responsive ── */

@media (max-width: 768px) {
  .td-saved-views__hero {
    flex-direction: column;
  }
}
</style>
