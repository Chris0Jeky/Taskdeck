<script setup lang="ts">
import { computed } from 'vue'
import type { Card, Column } from '../../types/board'
import PaperBoardCard, { type PaperBoardCardVariant } from './PaperBoardCard.vue'
import PaperHLBtn from '../../components/paper/PaperHLBtn.vue'

/**
 * PaperBoardColumn — Paper-styled kanban column.
 *
 * Surface: 280px wide, --paper-2 background, hairline border, 12px padding.
 * Header: mono serial (`§ 04`) + serif name + count badge. WIP-limit warning
 * surfaces as an overdue tagstamp on the column header.
 * Footer: hairline `+ capture` ghost button.
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
    /** Card visual variant — propagated to every card in the column. */
    cardVariant?: PaperBoardCardVariant
    selectedCardId?: string | null
    /** When true the column shows the drop-target highlight. */
    isDragOver?: boolean
  }>(),
  {
    cardVariant: 'index',
    selectedCardId: null,
    isDragOver: false,
  },
)

const emit = defineEmits<{
  (event: 'capture', column: Column): void
  (event: 'card-click', card: Card): void
  (event: 'card-dragstart', card: Card, e: DragEvent): void
  (event: 'card-dragend'): void
}>()

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

function onCardClick(card: Card) {
  emit('card-click', card)
}

function onCardDragStart(card: Card, e: DragEvent) {
  emit('card-dragstart', card, e)
}

function onCardDragEnd() {
  emit('card-dragend')
}
</script>

<template>
  <section
    class="paper-board-column"
    :class="{ 'paper-board-column--drag-over': isDragOver }"
    :data-column-id="column.id"
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
      </div>
    </header>

    <div class="paper-board-column__cards" data-testid="paper-column-cards">
      <PaperBoardCard
        v-for="card in cards"
        :key="card.id"
        :card="card"
        :variant="cardVariant"
        :selected="card.id === selectedCardId"
        @click="onCardClick"
        @dragstart="onCardDragStart"
        @dragend="onCardDragEnd"
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
        variant="ghost"
        label="+ capture"
        :data-action="`capture-column-${column.id}`"
        @click="onCapture"
      />
    </footer>
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
  gap: 6px;
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
}

.paper-board-column__footer :deep(.pbtn) {
  width: 100%;
  justify-content: center;
  font-family: var(--mono);
  font-size: 11px;
  letter-spacing: .12em;
  text-transform: uppercase;
  color: var(--mute);
}
</style>
