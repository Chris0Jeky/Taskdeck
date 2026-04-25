<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useGlobalSearch } from '../../composables/useGlobalSearch'
import type { SearchBoardHit, SearchCardHit } from '../../api/searchApi'
import PaperIcon from './PaperIcon.vue'
import PaperKbd from './PaperKbd.vue'
import type { CommandItem } from '../shell/ShellCommandPalette.vue'

/**
 * PaperCommandPalette — paper-mode command palette.  Mirrors the shape of
 * `ShellCommandPalette` (same `CommandItem` shape, same emits) but renders
 * with the paper-card hairline aesthetic from
 * `design_handoff_taskdeck_paper/paper/surface-misc.jsx::CommandPaletteSurface`.
 *
 * Visual contract:
 *   - 640px wide centered card, 4px radius, hairline border, on a dimmed
 *     paper backdrop.
 *   - 13px Inter input, no border, 16px padding inside the header.
 *   - Result rows: 40px height, hairline icon, label, mono kbd hint.
 *   - AI rows (`kind === 'action'` with `keywords` containing 'haiku') get an
 *     ember dot prefix and a "haiku" mono tag in the right-hand kbd column.
 *
 * Behaviour:
 *   - Filters items locally on input.  Up/Down navigate; Enter activates;
 *     Escape closes.
 *   - Activates with `activate(item)` — the same emit used by AppShell so the
 *     existing wiring from PAPER-03 keeps working without changes.
 */

const props = defineProps<{
  visible: boolean
  items: CommandItem[]
}>()

const emit = defineEmits<{
  close: []
  activate: [item: CommandItem]
}>()

const inputEl = ref<HTMLInputElement | null>(null)
const query = ref('')
const selectedIndex = ref(0)
const listboxId = 'paper-command-palette-listbox'

type PaperPaletteItem =
  | { type: 'command'; data: CommandItem }
  | { type: 'board'; data: SearchBoardHit }
  | { type: 'card'; data: SearchCardHit }

const {
  query: searchQuery,
  boards: searchBoards,
  cards: searchCards,
  loading: searchLoading,
  hasMoreCards,
  loadingMore: searchLoadingMore,
  totalCardCount,
  reset: resetSearch,
  loadMore: searchLoadMore,
} = useGlobalSearch(200)

const filteredCommandItems = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return props.items
  return props.items.filter((item) => {
    const haystack = `${item.label} ${item.path ?? ''} ${item.keywords ?? ''}`.toLowerCase()
    return haystack.includes(q)
  })
})

/**
 * Group items into "Action · ai", "Cards" / "Boards", "Capture" sections in a
 * way that mirrors the JSX reference.  We can't infer cards/boards from the
 * existing CommandItem shape (those come from the search composable in the
 * legacy palette), so we keep two simple buckets here: AI actions and the
 * rest.
 */
function isAiItem(item: CommandItem): boolean {
  return item.kind === 'action' && /haiku|propose|split/i.test(`${item.label} ${item.keywords ?? ''}`)
}

const aiItems = computed<PaperPaletteItem[]>(() =>
  filteredCommandItems.value.filter(isAiItem).map((item) => ({ type: 'command', data: item })),
)
const otherItems = computed<PaperPaletteItem[]>(() =>
  filteredCommandItems.value.filter((item) => !isAiItem(item)).map((item) => ({ type: 'command', data: item })),
)
const boardItems = computed<PaperPaletteItem[]>(() =>
  searchBoards.value.map((board) => ({ type: 'board', data: board })),
)
const cardItems = computed<PaperPaletteItem[]>(() =>
  searchCards.value.map((card) => ({ type: 'card', data: card })),
)

const orderedItems = computed<PaperPaletteItem[]>(() => [
  ...aiItems.value,
  ...otherItems.value,
  ...boardItems.value,
  ...cardItems.value,
])
const hasQuery = computed(() => query.value.trim().length >= 2)
const commandCount = computed(() => aiItems.value.length + otherItems.value.length)
const boardCount = computed(() => boardItems.value.length)

function isActive(index: number): boolean {
  return index === selectedIndex.value
}

function selectNext() {
  const total = orderedItems.value.length
  if (total === 0) return
  selectedIndex.value = (selectedIndex.value + 1) % total
}

function selectPrev() {
  const total = orderedItems.value.length
  if (total === 0) return
  selectedIndex.value = (selectedIndex.value - 1 + total) % total
}

function activate(item: PaperPaletteItem) {
  if (item.type === 'command') {
    emit('activate', item.data)
    return
  }

  if (item.type === 'board') {
    emit('activate', {
      id: `search:board:${item.data.id}`,
      label: item.data.name,
      icon: 'board',
      path: `/workspace/boards/${item.data.id}`,
      keywords: item.data.description ?? '',
      kind: 'navigation',
    })
    return
  }

  emit('activate', {
    id: `search:card:${item.data.id}`,
    label: item.data.title,
    icon: 'card',
    path: `/workspace/boards/${item.data.boardId}`,
    keywords: `${item.data.boardName} ${item.data.columnName} ${item.data.description ?? ''}`.trim(),
    kind: 'navigation',
  })
}

function activateSelected() {
  const item = orderedItems.value[selectedIndex.value]
  if (item) activate(item)
}

function setSelected(index: number) {
  selectedIndex.value = index
}

function handleClose() {
  emit('close')
}

const activeItemId = computed(() =>
  orderedItems.value.length > 0 ? `paper-palette-row-${selectedIndex.value}` : undefined,
)

// Compute an "AI offset" so we can render the section break and reset
// numbering between AI and non-AI rows visually.
const aiCount = computed(() => aiItems.value.length)
const boardOffset = computed(() => commandCount.value)
const cardOffset = computed(() => commandCount.value + boardCount.value)

function labelFor(item: PaperPaletteItem): string {
  if (item.type === 'command') return item.data.label
  if (item.type === 'board') return item.data.name
  return item.data.title
}

function subtextFor(item: PaperPaletteItem): string | undefined {
  if (item.type === 'command') return item.data.path
  if (item.type === 'board') return item.data.description ?? undefined
  return `${item.data.boardName} / ${item.data.columnName}`
}

function keywordsFor(item: PaperPaletteItem): string | undefined {
  return item.type === 'command' ? item.data.keywords : subtextFor(item)
}

function tagFor(item: PaperPaletteItem): string {
  if (item.type === 'board') return 'board'
  if (item.type === 'card') return 'card'
  return item.data.kind === 'navigation' ? 'jump' : 'do'
}

watch(query, (value) => {
  searchQuery.value = value
})

watch(
  () => props.visible,
  async (open) => {
    if (!open) {
      query.value = ''
      selectedIndex.value = 0
      resetSearch()
      return
    }
    await nextTick()
    inputEl.value?.focus()
  },
)

watch(orderedItems, (items) => {
  if (selectedIndex.value >= items.length) {
    selectedIndex.value = 0
  }
})
</script>

<template>
  <Teleport to="body">
    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions, vuejs-accessibility/click-events-have-key-events -- modal backdrop with dialog role; Escape close is wired on the dialog container; click-to-close is standard modal UX -->
    <div
      v-if="visible"
      class="paper-palette-backdrop"
      role="dialog"
      aria-modal="true"
      aria-label="Command palette"
      @click.self="handleClose"
      @keydown.escape.prevent="handleClose"
    >
      <div class="paper-palette card-lift" data-paper-palette>
        <header class="paper-palette__head">
          <PaperIcon name="search" />
          <input
            ref="inputEl"
            v-model="query"
            type="text"
            class="paper-palette__input"
            placeholder="Go anywhere · capture · ask"
            aria-label="Command palette search"
            role="combobox"
            aria-autocomplete="list"
            :aria-expanded="visible"
            :aria-controls="listboxId"
            :aria-activedescendant="activeItemId"
            @keydown.down.prevent="selectNext"
            @keydown.up.prevent="selectPrev"
            @keydown.enter.prevent="activateSelected"
          />
          <PaperKbd>esc</PaperKbd>
        </header>

        <div :id="listboxId" class="paper-palette__results" role="listbox" aria-label="Results">
          <section v-if="aiItems.length > 0" class="paper-palette__section" data-section="ai">
            <div class="paper-palette__section-title tk-eyebrow">Action · ai</div>
            <button
              v-for="(item, i) in aiItems"
              :id="`paper-palette-row-${i}`"
              :key="`${item.type}-${item.data.id}`"
              type="button"
              role="option"
              :aria-selected="isActive(i)"
              :class="[
                'paper-palette__row',
                'paper-palette__row--ai',
                { 'paper-palette__row--active': isActive(i) },
              ]"
              tabindex="-1"
              @mouseenter="setSelected(i)"
              @focusin="setSelected(i)"
              @keydown.enter.prevent="activate(item)"
              @click="activate(item)"
            >
              <span class="paper-palette__row-glyph paper-palette__row-glyph--ember" aria-hidden="true">◆</span>
              <span class="paper-palette__row-body">
                <span class="paper-palette__row-label">{{ labelFor(item) }}</span>
                <span v-if="keywordsFor(item)" class="paper-palette__row-sub">{{ keywordsFor(item) }}</span>
              </span>
              <span class="paper-palette__row-tag paper-palette__row-tag--ember">haiku</span>
            </button>
          </section>

          <section v-if="otherItems.length > 0" class="paper-palette__section" data-section="other">
            <div class="paper-palette__section-title tk-eyebrow">Commands</div>
            <button
              v-for="(item, i) in otherItems"
              :id="`paper-palette-row-${aiCount + i}`"
              :key="`${item.type}-${item.data.id}`"
              type="button"
              role="option"
              :aria-selected="isActive(aiCount + i)"
              :class="[
                'paper-palette__row',
                { 'paper-palette__row--active': isActive(aiCount + i) },
              ]"
              tabindex="-1"
              @mouseenter="setSelected(aiCount + i)"
              @focusin="setSelected(aiCount + i)"
              @keydown.enter.prevent="activate(item)"
              @click="activate(item)"
            >
              <span class="paper-palette__row-glyph" aria-hidden="true">·</span>
              <span class="paper-palette__row-body">
                <span class="paper-palette__row-label">{{ labelFor(item) }}</span>
                <span v-if="subtextFor(item)" class="paper-palette__row-sub">{{ subtextFor(item) }}</span>
              </span>
              <span class="paper-palette__row-tag">{{ tagFor(item) }}</span>
            </button>
          </section>

          <section v-if="boardItems.length > 0" class="paper-palette__section" data-section="boards">
            <div class="paper-palette__section-title tk-eyebrow">Boards</div>
            <button
              v-for="(item, i) in boardItems"
              :id="`paper-palette-row-${boardOffset + i}`"
              :key="`${item.type}-${item.data.id}`"
              type="button"
              role="option"
              :aria-selected="isActive(boardOffset + i)"
              :class="[
                'paper-palette__row',
                { 'paper-palette__row--active': isActive(boardOffset + i) },
              ]"
              tabindex="-1"
              @mouseenter="setSelected(boardOffset + i)"
              @focusin="setSelected(boardOffset + i)"
              @keydown.enter.prevent="activate(item)"
              @click="activate(item)"
            >
              <span class="paper-palette__row-glyph" aria-hidden="true">.</span>
              <span class="paper-palette__row-body">
                <span class="paper-palette__row-label">{{ labelFor(item) }}</span>
                <span v-if="subtextFor(item)" class="paper-palette__row-sub">{{ subtextFor(item) }}</span>
              </span>
              <span class="paper-palette__row-tag">{{ tagFor(item) }}</span>
            </button>
          </section>

          <section v-if="cardItems.length > 0" class="paper-palette__section" data-section="cards">
            <div class="paper-palette__section-title tk-eyebrow">Cards</div>
            <button
              v-for="(item, i) in cardItems"
              :id="`paper-palette-row-${cardOffset + i}`"
              :key="`${item.type}-${item.data.id}`"
              type="button"
              role="option"
              :aria-selected="isActive(cardOffset + i)"
              :class="[
                'paper-palette__row',
                { 'paper-palette__row--active': isActive(cardOffset + i) },
              ]"
              tabindex="-1"
              @mouseenter="setSelected(cardOffset + i)"
              @focusin="setSelected(cardOffset + i)"
              @keydown.enter.prevent="activate(item)"
              @click="activate(item)"
            >
              <span class="paper-palette__row-glyph" aria-hidden="true">.</span>
              <span class="paper-palette__row-body">
                <span class="paper-palette__row-label">{{ labelFor(item) }}</span>
                <span v-if="subtextFor(item)" class="paper-palette__row-sub">{{ subtextFor(item) }}</span>
              </span>
              <span class="paper-palette__row-tag">{{ tagFor(item) }}</span>
            </button>
          </section>

          <div v-if="hasMoreCards && hasQuery && !searchLoading" class="paper-palette__load-more">
            <button
              type="button"
              class="paper-palette__load-more-btn"
              :disabled="searchLoadingMore"
              @click="searchLoadMore()"
            >
              {{ searchLoadingMore ? 'Loading...' : `Load more cards (${cardItems.length} of ${totalCardCount})` }}
            </button>
          </div>

          <div v-if="(searchLoading || searchLoadingMore) && hasQuery" class="paper-palette__loading">
            {{ searchLoadingMore ? 'Loading more...' : 'Searching...' }}
          </div>

          <div v-if="orderedItems.length === 0 && !searchLoading" class="paper-palette__empty">
            <span class="tk-meta">No results — try a card serial like C-090</span>
          </div>
        </div>

        <footer class="paper-palette__footer">
          <span class="tk-meta">
            <PaperKbd>↑↓</PaperKbd> navigate
            <span class="paper-palette__sep">·</span>
            <PaperKbd>⏎</PaperKbd> commit
            <span class="paper-palette__sep">·</span>
            <PaperKbd>esc</PaperKbd> dismiss
          </span>
        </footer>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.paper-palette-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(26, 24, 20, 0.18);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 120px;
  z-index: 60;
}

.paper-palette {
  width: min(640px, calc(100vw - 32px));
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: 4px;
  font-family: var(--sans);
  color: var(--ink);
  overflow: hidden;
}

.paper-palette__head {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  border-bottom: 1px solid var(--line);
}

.paper-palette__input {
  flex: 1;
  border: 0;
  outline: 0;
  background: transparent;
  font-family: var(--sans);
  font-size: 13px;
  color: var(--ink-deep);
}

.paper-palette__input::placeholder {
  color: var(--mute);
}

.paper-palette__results {
  max-height: 360px;
  overflow-y: auto;
}

.paper-palette__section + .paper-palette__section {
  border-top: 1px solid var(--line-soft);
}

.paper-palette__section-title {
  padding: 10px 18px 4px;
  color: var(--faint);
}

.paper-palette__row {
  display: grid;
  grid-template-columns: 20px 1fr 80px;
  gap: 12px;
  align-items: center;
  width: 100%;
  height: 40px;
  padding: 0 18px;
  background: transparent;
  border: none;
  border-left: 2px solid transparent;
  text-align: left;
  cursor: pointer;
  font-family: var(--sans);
  color: var(--ink);
}

.paper-palette__row:hover {
  background: var(--paper-2);
}

.paper-palette__row--active {
  background: linear-gradient(90deg, var(--ember-bloom) 0%, transparent 70%);
  border-left-color: var(--ember);
}

.paper-palette__row--active .paper-palette__row-label {
  color: var(--ember);
  font-weight: 500;
}

.paper-palette__row-glyph {
  font-family: var(--serif);
  font-size: 14px;
  color: var(--faint);
  text-align: center;
}

.paper-palette__row-glyph--ember {
  color: var(--ember);
}

.paper-palette__row-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.paper-palette__row-label {
  font-size: 13.5px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--ink);
}

.paper-palette__row-sub {
  font-family: var(--mono);
  font-size: 10.5px;
  color: var(--mute);
  letter-spacing: 0.04em;
  margin-top: 1px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.paper-palette__row-tag {
  font-family: var(--mono);
  font-size: 10.5px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  text-align: right;
  color: var(--mute);
}

.paper-palette__row-tag--ember {
  color: var(--ember);
}

.paper-palette__empty {
  padding: 24px 18px;
  text-align: center;
}

.paper-palette__load-more,
.paper-palette__loading {
  padding: 14px 18px;
  text-align: center;
}

.paper-palette__load-more-btn {
  background: transparent;
  border: 1px solid var(--line);
  border-radius: 3px;
  color: var(--ember);
  cursor: pointer;
  font-family: var(--mono);
  font-size: 10.5px;
  letter-spacing: 0.12em;
  padding: 6px 10px;
  text-transform: uppercase;
}

.paper-palette__load-more-btn:disabled {
  cursor: wait;
  opacity: 0.6;
}

.paper-palette__loading {
  color: var(--mute);
  font-family: var(--mono);
  font-size: 10.5px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.paper-palette__footer {
  padding: 10px 18px;
  border-top: 1px solid var(--line);
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-family: var(--sans);
  font-size: 11px;
  color: var(--mute);
}

.paper-palette__sep {
  margin: 0 6px;
  color: var(--whisper);
}
</style>
