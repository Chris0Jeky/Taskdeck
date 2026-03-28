<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'

export type CommandItem = {
  id: string
  label: string
  icon: string
  path?: string
  keywords?: string
  kind: 'navigation' | 'action'
  action?: () => void
}

const props = defineProps<{
  visible: boolean
  items: CommandItem[]
}>()

const emit = defineEmits<{
  close: []
  activate: [item: CommandItem]
}>()

const commandPaletteInput = ref<HTMLInputElement | null>(null)
const commandQuery = ref('')
const selectedCommandIndex = ref(0)
const commandListboxId = 'td-command-palette-listbox'

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

const activeCommandId = computed(() => {
  if (filteredCommandItems.value.length === 0) {
    return undefined
  }

  return `td-command-option-${selectedCommandIndex.value}`
})

function selectNextCommand() {
  const itemCount = filteredCommandItems.value.length
  if (itemCount === 0) return
  selectedCommandIndex.value = (selectedCommandIndex.value + 1) % itemCount
}

function selectPreviousCommand() {
  const itemCount = filteredCommandItems.value.length
  if (itemCount === 0) return
  selectedCommandIndex.value = (selectedCommandIndex.value - 1 + itemCount) % itemCount
}

function setSelectedCommand(index: number) {
  selectedCommandIndex.value = index
}

function activateCommand(item: CommandItem) {
  emit('activate', item)
}

function activateSelectedCommand() {
  const selectedItem = filteredCommandItems.value[selectedCommandIndex.value]
  if (!selectedItem) return
  activateCommand(selectedItem)
}

function handleClose() {
  emit('close')
}

watch(() => props.visible, async (isOpen) => {
  if (!isOpen) {
    commandQuery.value = ''
    selectedCommandIndex.value = 0
    return
  }
  await nextTick()
  commandPaletteInput.value?.focus()
})

watch(filteredCommandItems, (items) => {
  if (items.length === 0) {
    selectedCommandIndex.value = 0
    return
  }

  if (selectedCommandIndex.value >= items.length) {
    selectedCommandIndex.value = 0
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
          placeholder="Type a command or search..."
          autofocus
          role="combobox"
          aria-autocomplete="list"
          :aria-expanded="visible"
          :aria-controls="commandListboxId"
          :aria-activedescendant="activeCommandId"
          @keydown.escape.prevent="handleClose"
          @keydown.down.prevent="selectNextCommand"
          @keydown.up.prevent="selectPreviousCommand"
          @keydown.enter.prevent="activateSelectedCommand"
        />
        <div
          :id="commandListboxId"
          class="td-command-palette__results"
          role="listbox"
          aria-label="Commands"
        >
          <div class="td-command-palette__group">
            <div class="td-command-palette__group-title">Commands</div>
            <div
              v-for="(item, index) in filteredCommandItems"
              :key="item.id"
              :id="`td-command-option-${index}`"
              :data-command-index="index"
              :class="[
                'td-command-palette__item',
                index === selectedCommandIndex ? 'td-command-palette__item--active' : ''
              ]"
              role="option"
              :aria-selected="index === selectedCommandIndex"
              @mouseenter="setSelectedCommand(index)"
              @click="activateCommand(item)"
            >
              <span>{{ item.icon }}</span>
              <span>{{ item.kind === 'navigation' ? `Go to ${item.label}` : item.label }}</span>
            </div>
            <div v-if="filteredCommandItems.length === 0" class="td-command-palette__empty">
              No matching commands.
            </div>
          </div>
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
  backdrop-filter: blur(24px);
  border: 0.5px solid rgba(91, 64, 62, 0.2);
  box-shadow: var(--td-shadow-lg);
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
  border-bottom: 1px solid rgba(91, 64, 62, 0.15);
  background: transparent;
  color: var(--td-text-primary);
}

.td-command-palette__input::placeholder {
  color: var(--td-text-tertiary);
}

.td-command-palette__results {
  max-height: 300px;
  overflow-y: auto;
  padding: var(--td-space-2);
}

.td-command-palette__group-title {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 9px;
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
  background: transparent;
  cursor: pointer;
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 11px;
  letter-spacing: 0.05em;
  text-transform: uppercase;
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
  border-left: 2px solid var(--td-color-ember-glow);
}

.td-command-palette__empty {
  padding: var(--td-space-4);
  color: var(--td-text-tertiary);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 11px;
  letter-spacing: 0.05em;
}
</style>
