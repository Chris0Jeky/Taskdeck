<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
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

const filteredItems = computed(() => {
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

const aiItems = computed(() => filteredItems.value.filter(isAiItem))
const otherItems = computed(() => filteredItems.value.filter((item) => !isAiItem(item)))

const orderedItems = computed<CommandItem[]>(() => [...aiItems.value, ...otherItems.value])

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

function activate(item: CommandItem) {
  emit('activate', item)
}

function activateSelected() {
  const item = orderedItems.value[selectedIndex.value]
  if (item) emit('activate', item)
}

function setSelected(index: number) {
  selectedIndex.value = index
}

function handleClose() {
  emit('close')
}

const activeItemId = computed(() => `paper-palette-row-${selectedIndex.value}`)

// Compute an "AI offset" so we can render the section break and reset
// numbering between AI and non-AI rows visually.
const aiCount = computed(() => aiItems.value.length)

watch(
  () => props.visible,
  async (open) => {
    if (!open) {
      query.value = ''
      selectedIndex.value = 0
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
    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions, vuejs-accessibility/click-events-have-key-events -- modal backdrop with dialog role; Escape close is wired on the input element; click-to-close is standard modal UX -->
    <div
      v-if="visible"
      class="paper-palette-backdrop"
      role="dialog"
      aria-modal="true"
      aria-label="Command palette"
      @click.self="handleClose"
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
            @keydown.escape.prevent="handleClose"
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
              :key="item.id"
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
              @click="activate(item)"
            >
              <span class="paper-palette__row-glyph paper-palette__row-glyph--ember" aria-hidden="true">◆</span>
              <span class="paper-palette__row-body">
                <span class="paper-palette__row-label">{{ item.label }}</span>
                <span v-if="item.keywords" class="paper-palette__row-sub">{{ item.keywords }}</span>
              </span>
              <span class="paper-palette__row-tag paper-palette__row-tag--ember">haiku</span>
            </button>
          </section>

          <section v-if="otherItems.length > 0" class="paper-palette__section" data-section="other">
            <div class="paper-palette__section-title tk-eyebrow">Commands</div>
            <button
              v-for="(item, i) in otherItems"
              :id="`paper-palette-row-${aiCount + i}`"
              :key="item.id"
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
              @click="activate(item)"
            >
              <span class="paper-palette__row-glyph" aria-hidden="true">·</span>
              <span class="paper-palette__row-body">
                <span class="paper-palette__row-label">{{ item.label }}</span>
                <span v-if="item.path" class="paper-palette__row-sub">{{ item.path }}</span>
              </span>
              <span class="paper-palette__row-tag">{{ item.kind === 'navigation' ? 'jump' : 'do' }}</span>
            </button>
          </section>

          <div v-if="orderedItems.length === 0" class="paper-palette__empty">
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
