<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Card, Column } from '../../types/board'
import PaperBoardCard, { type PaperBoardCardVariant } from './PaperBoardCard.vue'
import PaperCardComposer from './board/PaperCardComposer.vue'
import PaperHLBtn from '../../components/paper/PaperHLBtn.vue'
import PaperIcon from '../../components/paper/PaperIcon.vue'

/**
 * PaperBoardColumn — Paper-styled kanban column.
 *
 * Surface: 280px wide, --paper-2 background, hairline border, 12px padding.
 * Header: mono serial (`§ 04`) + serif name + column controls + count badge.
 * WIP-limit warning surfaces as an overdue tagstamp on the column header.
 * Footer: primary `+ card` (direct create) with `+ capture` demoted beneath it.
 *
 * Presentational by design — every mutation is emitted up to `PaperBoardView`,
 * which owns the `boardStore` calls. The one exception is the composer's draft
 * text, which lives inside `PaperCardComposer`.
 *
 * `+ card` vs `+ capture` (#1945 / ADR-0056): `+ card` writes a card straight
 * to this column and stays on the board. `+ capture` leaves for Inbox and
 * produces a proposal that has to be reviewed. Both are legitimate; only the
 * first is the direct lane, so it is the visually primary one.
 *
 * Drag state and card events are propagated up to the orchestrator
 * (`PaperBoardView`) so existing `useBoardDragDrop` semantics keep working.
 */
const props = withDefaults(
  defineProps<{
    column: Column
    /** 1-based index used to render the `§ NN` serial. */
    index: number
    cards: Card[]
    /** Hide card DOM and footer actions while retaining the lane header/drop surface. */
    collapsed?: boolean
    /** Card visual variant — propagated to every card in the column. */
    cardVariant?: PaperBoardCardVariant
    selectedCardId?: string | null
    /** When true the column shows the drop-target highlight. */
    isDragOver?: boolean
    /** False for the leftmost column — disables its move-left control. */
    canMoveLeft?: boolean
    /** False for the rightmost column — disables its move-right control. */
    canMoveRight?: boolean
    /** True while this column's inline card composer is open. */
    composerOpen?: boolean
    composerBusy?: boolean
    composerError?: string | null
  }>(),
  {
    cardVariant: 'index',
    collapsed: false,
    selectedCardId: null,
    isDragOver: false,
    canMoveLeft: false,
    canMoveRight: false,
    composerOpen: false,
    composerBusy: false,
    composerError: null,
  },
)

const emit = defineEmits<{
  (event: 'capture', column: Column): void
  (event: 'toggle-collapse', column: Column): void
  (event: 'edit', column: Column): void
  (event: 'move', column: Column, direction: 'left' | 'right'): void
  (event: 'open-composer', column: Column): void
  (event: 'submit-card', column: Column, title: string): void
  (event: 'cancel-composer'): void
  (event: 'card-click', card: Card): void
  (event: 'card-dragstart', card: Card, e: DragEvent): void
  (event: 'card-dragend'): void
  (event: 'card-drop', card: Card, column: Column, e: DragEvent): void
  (event: 'card-dragover', card: Card, e: DragEvent): void
}>()

const { t } = useI18n()

const contentId = computed(() => `paper-board-column-content-${props.column.id}`)
const collapseLabel = computed(() => t(
  props.collapsed ? 'boardDetail.column.expandAria' : 'boardDetail.column.collapseAria',
  { column: props.column.name },
))

const serial = computed(() => `§ ${String(props.index).padStart(2, '0')}`)

/** WIP overflow — null/<=0 wipLimit means unlimited (mirrors backend semantics). */
const isWipExceeded = computed(() => {
  const limit = props.column.wipLimit
  if (limit == null || limit <= 0) return false
  return props.cards.length > limit
})

const countLabel = computed(() => {
  const count = props.cards.length
  if (props.column.wipLimit && props.column.wipLimit > 0) {
    return `${count}/${props.column.wipLimit}`
  }
  return String(count)
})

function onCapture() {
  emit('capture', props.column)
}

function onEdit() {
  emit('edit', props.column)
}

function onToggleCollapse() {
  emit('toggle-collapse', props.column)
}

function onMoveLeft() {
  if (!props.canMoveLeft) return
  emit('move', props.column, 'left')
}

function onMoveRight() {
  if (!props.canMoveRight) return
  emit('move', props.column, 'right')
}

/**
 * Opens the composer. Idempotent despite the `toggle-add-card` action name —
 * the name is the DOM contract `useBoardKeyboardNav` clicks for the `n`
 * shortcut, and Legacy's `openCardForm` is open-only too. A second `n` on an
 * already-composing column must not close the draft out from under the user.
 */
function onAddCard() {
  emit('open-composer', props.column)
}

function onComposerSubmit(title: string) {
  emit('submit-card', props.column, title)
}

function onComposerCancel() {
  emit('cancel-composer')
}

function onCardClick(card: Card) {
  emit('card-click', card)
}

function onCardDragStart(card: Card, e: DragEvent) {
  emit('card-dragstart', card, e)
}

function onCardDragEnd() {
  emit('card-dragend')
}

function onCardDrop(card: Card, e: DragEvent) {
  emit('card-drop', card, props.column, e)
}

function onCardDragOver(card: Card, e: DragEvent) {
  emit('card-dragover', card, e)
}
</script>

<template>
  <section
    class="paper-board-column"
    :class="{
      'paper-board-column--drag-over': isDragOver,
      'paper-board-column--collapsed': collapsed,
    }"
    :data-column-id="column.id"
    :data-collapsed="collapsed"
    role="group"
    :aria-label="`Column ${column.name}`"
  >
    <header class="paper-board-column__header">
      <div
        class="paper-board-column__heading"
        data-action="drag-column-handle"
        draggable="true"
      >
        <span class="paper-board-column__serial tk-num">{{ serial }}</span>
        <h3 class="paper-board-column__name">{{ column.name }}</h3>
      </div>
      <div class="paper-board-column__meta">
        <span
          v-if="isWipExceeded"
          class="tagstamp paper-board-column__wip"
          data-testid="paper-column-wip-warning"
          :style="{ color: 'var(--overdue)' }"
        >OVERDUE</span>
        <span class="paper-board-column__count">{{ countLabel }}</span>
        <button
          type="button"
          class="paper-board-column__ctl paper-board-column__collapse"
          :aria-label="collapseLabel"
          :title="collapseLabel"
          :aria-expanded="!collapsed"
          :aria-controls="contentId"
          :data-action="collapsed ? 'expand-column' : 'collapse-column'"
          :data-testid="`paper-column-collapse-${column.id}`"
          @keydown.enter.stop
          @keydown.space.stop
          @click="onToggleCollapse"
        >
          <PaperIcon
            name="chevronDown"
            :class="{ 'paper-board-column__collapse-icon--collapsed': collapsed }"
          />
        </button>
        <button
          type="button"
          class="paper-board-column__ctl paper-board-column__ctl--flip"
          :aria-label="t('boardDetail.column.moveLeft')"
          :title="t('boardDetail.column.moveLeft')"
          :disabled="!canMoveLeft"
          data-testid="paper-column-move-left"
          @click="onMoveLeft"
        >
          <PaperIcon name="chevronRight" />
        </button>
        <button
          type="button"
          class="paper-board-column__ctl"
          :aria-label="t('boardDetail.column.moveRight')"
          :title="t('boardDetail.column.moveRight')"
          :disabled="!canMoveRight"
          data-testid="paper-column-move-right"
          @click="onMoveRight"
        >
          <PaperIcon name="chevronRight" />
        </button>
        <button
          type="button"
          class="paper-board-column__ctl"
          :aria-label="t('boardDetail.column.settingsAria', { column: column.name })"
          :title="t('boardDetail.column.settings')"
          data-action="edit-column"
          data-testid="paper-column-edit"
          @click="onEdit"
        >
          <PaperIcon name="settings" />
        </button>
      </div>
    </header>

    <div
      :id="contentId"
      class="paper-board-column__content"
      :hidden="collapsed"
    >
      <div v-if="!collapsed" class="paper-board-column__cards" data-testid="paper-column-cards">
        <PaperBoardCard
          v-for="card in cards"
          :key="card.id"
          :card="card"
          :variant="cardVariant"
          :selected="card.id === selectedCardId"
          @click="onCardClick"
          @dragstart="onCardDragStart"
          @dragend="onCardDragEnd"
          @dragover="onCardDragOver(card, $event)"
          @drop="onCardDrop(card, $event)"
        />

        <p
          v-if="cards.length === 0"
          class="paper-board-column__empty"
          data-testid="paper-column-empty"
        >
          — empty —
        </p>
      </div>

      <footer class="paper-board-column__footer">
        <PaperHLBtn
          variant="primary"
          class="paper-board-column__add-card"
          :label="t('boardDetail.card.add')"
          :aria-label="t('boardDetail.card.addAria', { column: column.name })"
          data-action="toggle-add-card"
          data-testid="paper-column-add-card"
          @click="onAddCard"
        />

        <PaperCardComposer
          v-if="composerOpen"
          :column-id="column.id"
          :busy="composerBusy"
          :error="composerError"
          @submit="onComposerSubmit"
          @cancel="onComposerCancel"
        />

        <button
          type="button"
          class="paper-board-column__capture"
          :aria-label="t('boardDetail.card.captureAria', { column: column.name })"
          :data-action="`capture-column-${column.id}`"
          data-testid="paper-column-capture"
          @click="onCapture"
        >
          {{ t('boardDetail.card.capture') }}
        </button>
      </footer>
    </div>
  </section>
</template>

<style scoped>
.paper-board-column {
  flex: 0 0 280px;
  width: 280px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  background: var(--paper-2);
  border: 1px solid var(--line-soft);
  border-radius: var(--r-2);
  box-shadow: var(--shadow-press);
  font-family: var(--sans);
  color: var(--ink);
  min-height: 240px;
  transition: border-color var(--d-quick) var(--ease-paper);
}

.paper-board-column--drag-over {
  border-color: var(--ember);
  box-shadow: 0 0 0 1px var(--ember), var(--shadow-press);
}

.paper-board-column--collapsed {
  min-height: 96px;
}

.paper-board-column__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 0 4px 10px;
  border-bottom: 1px solid var(--line);
}

.paper-board-column__heading {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
}

.paper-board-column__serial {
  font-family: var(--mono);
  font-size: 10.5px;
  color: var(--faint);
  letter-spacing: .14em;
  text-transform: uppercase;
}

.paper-board-column__name {
  margin: 0;
  font-family: var(--serif);
  font-weight: 500;
  font-size: 16px;
  line-height: 1.18;
  letter-spacing: -.005em;
  color: var(--ink-deep);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.paper-board-column__meta {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  flex: none;
}

.paper-board-column__count {
  display: inline-grid;
  place-items: center;
  min-width: 24px;
  padding: 1px 6px;
  font-family: var(--mono);
  font-size: 10.5px;
  color: var(--mute);
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: var(--r-1);
  letter-spacing: .04em;
}

.paper-board-column__ctl {
  display: inline-grid;
  place-items: center;
  padding: 2px;
  color: var(--faint);
  background: transparent;
  border: none;
  border-radius: var(--r-1);
  cursor: pointer;
  transition: color var(--d-quick) var(--ease-paper);
}

.paper-board-column__ctl:hover:not(:disabled) {
  color: var(--ink);
}

.paper-board-column__ctl:disabled {
  opacity: 0.3;
  cursor: default;
}

.paper-board-column__ctl--flip {
  transform: scaleX(-1);
}

.paper-board-column__collapse-icon--collapsed {
  transform: rotate(-90deg);
}

.paper-board-column__wip {
  font-family: var(--mono);
  font-size: 9px;
  letter-spacing: .22em;
  text-transform: uppercase;
  font-weight: 600;
  border: 1px solid currentColor;
  padding: 2px 6px;
  border-radius: 1px;
  line-height: 1;
}

.paper-board-column__cards {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 4px;
  min-height: 64px;
}

.paper-board-column__content {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  gap: 10px;
}

.paper-board-column__content[hidden] {
  display: none;
}

.paper-board-column__empty {
  margin: 0;
  padding: 16px 4px;
  text-align: center;
  font-family: var(--mono);
  font-size: 10.5px;
  color: var(--faint);
  letter-spacing: .14em;
  text-transform: uppercase;
  border-top: 1px dashed var(--line-soft);
  border-bottom: 1px dashed var(--line-soft);
}

.paper-board-column__footer {
  margin-top: auto;
  padding-top: 6px;
  border-top: 1px dashed var(--line-soft);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.paper-board-column__footer :deep(.paper-board-column__add-card) {
  width: 100%;
  justify-content: center;
  font-family: var(--mono);
  font-size: 11px;
  letter-spacing: .12em;
  text-transform: uppercase;
}

/* `+ capture` is the secondary lane: smaller, muted, no button chrome. */
.paper-board-column__capture {
  align-self: center;
  padding: 2px 4px;
  background: transparent;
  border: none;
  color: var(--faint);
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: .12em;
  text-transform: uppercase;
  cursor: pointer;
  text-decoration: underline;
  text-decoration-style: dotted;
  text-underline-offset: 3px;
  transition: color var(--d-quick) var(--ease-paper);
}

.paper-board-column__capture:hover {
  color: var(--mute);
}
</style>
