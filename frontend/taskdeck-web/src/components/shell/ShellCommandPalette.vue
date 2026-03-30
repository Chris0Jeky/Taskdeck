<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useGlobalSearch } from '../../composables/useGlobalSearch'
import type { SearchBoardHit, SearchCardHit } from '../../api/searchApi'

export type CommandItem = {
  id: string
  label: string
  icon: string
  path?: string
  keywords?: string
  kind: 'navigation' | 'action'
  action?: () => void
}

export type PaletteItem =
  | { type: 'command'; data: CommandItem }
  | { type: 'board'; data: SearchBoardHit }
  | { type: 'card'; data: SearchCardHit }

const props = defineProps<{
  visible: boolean
  items: CommandItem[]
}>()

const emit = defineEmits<{
  close: []
  activate: [item: CommandItem]
  navigateToBoard: [boardId: string]
  navigateToCard: [boardId: string, cardId: string]
}>()

const commandPaletteInput = ref<HTMLInputElement | null>(null)
const commandQuery = ref('')
const selectedIndex = ref(0)
const commandListboxId = 'td-command-palette-listbox'

const { query: searchQuery, boards: searchBoards, cards: searchCards, loading: searchLoading, reset: resetSearch } = useGlobalSearch(200)

// Filter command items locally
const filteredCommandItems = computed(() => {
  const normalizedQuery = commandQuery.value.trim().toLowerCase()
  if (!normalizedQuery) {
    return props.items
  }

  return props.items.filter((item) =>
    item.label.toLowerCase().includes(normalizedQuery) ||
    item.path?.toLowerCase().includes(normalizedQuery) ||
    item.keywords?.toLowerCase().includes(normalizedQuery)
  )
})

// Build unified flat list of all palette items
const allPaletteItems = computed<PaletteItem[]>(() => {
  const items: PaletteItem[] = []

  // Commands always come first
  for (const cmd of filteredCommandItems.value) {
    items.push({ type: 'command', data: cmd })
  }

  // Board results from backend search
  for (const board of searchBoards.value) {
    items.push({ type: 'board', data: board })
  }

  // Card results from backend search
  for (const card of searchCards.value) {
    items.push({ type: 'card', data: card })
  }

  return items
})

// Group boundaries for section headers
const commandCount = computed(() => filteredCommandItems.value.length)
const boardCount = computed(() => searchBoards.value.length)
const cardCount = computed(() => searchCards.value.length)

const hasQuery = computed(() => commandQuery.value.trim().length >= 2)

const activeItemId = computed(() => {
  if (allPaletteItems.value.length === 0) {
    return undefined
  }
  return `td-palette-option-${selectedIndex.value}`
})

function selectNext() {
  const itemCount = allPaletteItems.value.length
  if (itemCount === 0) return
  selectedIndex.value = (selectedIndex.value + 1) % itemCount
}

function selectPrevious() {
  const itemCount = allPaletteItems.value.length
  if (itemCount === 0) return
  selectedIndex.value = (selectedIndex.value - 1 + itemCount) % itemCount
}

function setSelected(index: number) {
  selectedIndex.value = index
}

function activateItem(item: PaletteItem) {
  if (item.type === 'command') {
    emit('activate', item.data)
  } else if (item.type === 'board') {
    emit('navigateToBoard', item.data.id)
  } else if (item.type === 'card') {
    emit('navigateToCard', item.data.boardId, item.data.id)
  }
}

function activateSelected() {
  const selected = allPaletteItems.value[selectedIndex.value]
  if (!selected) return
  activateItem(selected)
}

function handleClose() {
  emit('close')
}

function getItemLabel(item: PaletteItem): string {
  if (item.type === 'command') {
    return item.data.kind === 'navigation' ? `Go to ${item.data.label}` : item.data.label
  }
  if (item.type === 'board') {
    return item.data.name
  }
  return item.data.title
}

function getItemIcon(item: PaletteItem): string {
  if (item.type === 'command') return item.data.icon
  if (item.type === 'board') return '\u{1F4CB}'
  return '\u{1F4C4}'
}

function getItemSubtext(item: PaletteItem): string | null {
  if (item.type === 'board' && item.data.description) {
    return item.data.description.length > 60
      ? item.data.description.slice(0, 60) + '...'
      : item.data.description
  }
  if (item.type === 'card') {
    return `${item.data.boardName} / ${item.data.columnName}`
  }
  return null
}

// Returns the group header text for a given index, or null if no header should be shown
function groupHeaderAt(index: number): string | null {
  if (index === 0 && commandCount.value > 0) return 'Commands'
  if (index === commandCount.value && boardCount.value > 0) return 'Boards'
  if (index === commandCount.value + boardCount.value && cardCount.value > 0) return 'Cards'
  return null
}

// Sync local query with search composable
watch(commandQuery, (val) => {
  searchQuery.value = val
})

watch(() => props.visible, async (isOpen) => {
  if (!isOpen) {
    commandQuery.value = ''
    selectedIndex.value = 0
    resetSearch()
    return
  }
  await nextTick()
  commandPaletteInput.value?.focus()
})

watch(allPaletteItems, (items) => {
  if (items.length === 0) {
    selectedIndex.value = 0
    return
  }
  if (selectedIndex.value >= items.length) {
    selectedIndex.value = 0
  }
})
</script>

<template>
  <Teleport to="body">
    <div
      v-if="visible"
      class="td-overlay"
      role="dialog"
      aria-label="Command palette"
      aria-modal="true"
      @click.self="handleClose"
    >
      <div class="td-command-palette">
        <input
          ref="commandPaletteInput"
          v-model="commandQuery"
          type="text"
          class="td-command-palette__input"
          placeholder="Type a command or search boards and cards..."
          autofocus
          role="combobox"
          aria-autocomplete="list"
          :aria-expanded="visible"
          :aria-controls="commandListboxId"
          :aria-activedescendant="activeItemId"
          @keydown.escape.prevent="handleClose"
          @keydown.down.prevent="selectNext"
          @keydown.up.prevent="selectPrevious"
          @keydown.enter.prevent="activateSelected"
        />
        <div
          :id="commandListboxId"
          class="td-command-palette__results"
          role="listbox"
          aria-label="Results"
        >
          <template v-for="(item, index) in allPaletteItems" :key="`${item.type}-${item.type === 'command' ? item.data.id : item.type === 'board' ? item.data.id : item.data.id}`">
            <div v-if="groupHeaderAt(index)" class="td-command-palette__group-title">
              {{ groupHeaderAt(index) }}
            </div>
            <div
              :id="`td-palette-option-${index}`"
              :data-palette-index="index"
              :class="[
                'td-command-palette__item',
                index === selectedIndex ? 'td-command-palette__item--active' : ''
              ]"
              role="option"
              :aria-selected="index === selectedIndex"
              @mouseenter="setSelected(index)"
              @click="activateItem(item)"
            >
              <span class="td-command-palette__item-icon">{{ getItemIcon(item) }}</span>
              <span class="td-command-palette__item-content">
                <span class="td-command-palette__item-label">{{ getItemLabel(item) }}</span>
                <span v-if="getItemSubtext(item)" class="td-command-palette__item-subtext">{{ getItemSubtext(item) }}</span>
              </span>
            </div>
          </template>

          <div v-if="searchLoading && hasQuery" class="td-command-palette__loading">
            Searching...
          </div>

          <div v-if="allPaletteItems.length === 0 && !searchLoading" class="td-command-palette__empty">
            {{ hasQuery ? 'No results found.' : 'No matching commands.' }}
          </div>
        </div>

        <div class="td-command-palette__footer">
          <span class="td-command-palette__hint">
            <kbd>&uarr;</kbd><kbd>&darr;</kbd> navigate
          </span>
          <span class="td-command-palette__hint">
            <kbd>Enter</kbd> select
          </span>
          <span class="td-command-palette__hint">
            <kbd>Esc</kbd> close
          </span>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.td-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.7);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 15vh;
  z-index: 50;
  backdrop-filter: blur(4px);
}

.td-command-palette {
  background: var(--td-glass-bg);
  backdrop-filter: blur(var(--td-glass-blur));
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  box-shadow: var(--td-shadow-xl);
  width: 100%;
  max-width: 560px;
  overflow: hidden;
}

.td-command-palette__input {
  width: 100%;
  padding: var(--td-space-5);
  border: none;
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-lg);
  outline: none;
  border-bottom: 1px solid var(--td-border-default);
  background: transparent;
  color: var(--td-text-primary);
}

.td-command-palette__input::placeholder {
  color: var(--td-text-tertiary);
}

.td-command-palette__results {
  max-height: 360px;
  overflow-y: auto;
  padding: var(--td-space-2);
}

.td-command-palette__group-title {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  color: var(--td-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.2em;
  padding: var(--td-space-3) var(--td-space-4);
}

.td-command-palette__item {
  display: flex;
  align-items: center;
  gap: var(--td-space-4);
  width: 100%;
  padding: var(--td-space-3) var(--td-space-4);
  border: none;
  border-left: 2px solid transparent;
  border-radius: var(--td-radius-sm);
  background: transparent;
  cursor: pointer;
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  letter-spacing: 0.05em;
  text-align: left;
  color: var(--td-text-muted);
  transition: all var(--td-transition-fast);
}

.td-command-palette__item:hover {
  background: var(--td-surface-bright);
  color: var(--td-text-primary);
}

.td-command-palette__item--active {
  background: var(--td-surface-bright);
  color: var(--td-color-ember);
  border-left-color: var(--td-color-ember-glow);
}

.td-command-palette__item-icon {
  flex-shrink: 0;
  width: 20px;
  text-align: center;
}

.td-command-palette__item-content {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.td-command-palette__item-label {
  text-transform: uppercase;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.td-command-palette__item-subtext {
  font-size: 0.75em;
  color: var(--td-text-tertiary);
  text-transform: none;
  letter-spacing: normal;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.td-command-palette__loading {
  padding: var(--td-space-4);
  color: var(--td-text-tertiary);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  letter-spacing: 0.05em;
  text-align: center;
}

.td-command-palette__empty {
  padding: var(--td-space-4);
  color: var(--td-text-tertiary);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  letter-spacing: 0.05em;
}

.td-command-palette__footer {
  display: flex;
  gap: var(--td-space-4);
  padding: var(--td-space-2) var(--td-space-4);
  border-top: 1px solid var(--td-border-default);
  background: var(--td-surface-container);
}

.td-command-palette__hint {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 0.65rem;
  color: var(--td-text-tertiary);
  display: flex;
  align-items: center;
  gap: 2px;
}

.td-command-palette__hint kbd {
  display: inline-block;
  padding: 1px 4px;
  border: 1px solid var(--td-border-default);
  border-radius: 3px;
  font-family: inherit;
  font-size: inherit;
  background: var(--td-surface-base);
  color: var(--td-text-muted);
}
</style>
