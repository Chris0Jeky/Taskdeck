<script setup lang="ts">
/* eslint-disable vuejs-accessibility/no-static-element-interactions -- the article and aria-hidden drag glyph are pointer drag boundaries; named-button activation and board keyboard movement remain separate */
import { computed, ref } from 'vue'
import { useBoardProposalMarker } from '../../composables/useBoardProposalMarker'
import type { WorkspacePresentation } from '../../store/workspaceLayoutStore'
import type { Card, Label } from '../../types/board'
import { formatCalendarDate, isCalendarDateOverdue } from '../../utils/dueDates'

/**
 * PaperBoardCard — index card / tag-ribbon card in the Paper kanban surface.
 *
 * Variant `index` (default) is the dense, ledger-style index card from the
 * Paper handoff (`design_handoff_taskdeck_paper/paper/surface-board.jsx`,
 * CardA). Variant `ribbon` adds a 4px coloured ribbon down the left edge keyed
 * to the card's first label (or a status tone if provided).
 *
 * The component is purely presentational — drag/drop and click handling are
 * driven by parent listeners. No store coupling here.
 */
export type PaperBoardCardVariant = 'index' | 'ribbon'
export type PaperBoardPresentation = WorkspacePresentation | 'classic'

const props = withDefaults(
  defineProps<{
    card: Card
    presentation?: PaperBoardPresentation
    variant?: PaperBoardCardVariant
    /** Subtask completion ratio shown in the metadata strip. */
    subtasks?: { done: number; total: number } | null
    /**
     * Status tone — when provided, drives ribbon colour and may surface as a
     * tagstamp. Mirrors the `stamp` keys from the JSX spec.
     */
    tone?: 'proposed' | 'applied' | 'overdue' | null
    /**
     * Highlight ring (selected via keyboard navigation). Adds focus styling.
     */
    selected?: boolean
  }>(),
  {
    variant: 'index',
    presentation: 'classic',
    subtasks: null,
    tone: null,
    selected: false,
  },
)

const emit = defineEmits<{
  (event: 'click', card: Card): void
  (event: 'dragstart', card: Card, e: DragEvent): void
  (event: 'dragend'): void
}>()

// Disclosure is local view state. A presentation switch never replaces the card
// or reaches its mutation handlers, and returning to Zen retains the disclosure.
const detailsExpanded = ref(false)
const isZen = computed(() => props.presentation === 'zen')
const showSecondaryDetails = computed(() => !isZen.value || detailsExpanded.value)

/** Mono serial — `C-` plus first 8 hex chars of card.id (or full id if short). */
const serial = computed(() => {
  const raw = (props.card.id ?? '').replace(/-/g, '')
  const head = raw.slice(0, 8) || 'unknown'
  return `C-${head}`
})

/** First label drives the ribbon colour for variant B. */
const primaryLabel = computed<Label | null>(() => {
  return props.card.labels?.[0] ?? null
})

const ribbonColor = computed(() => {
  if (props.tone === 'proposed') return 'var(--ember)'
  if (props.tone === 'applied') return 'var(--applied)'
  if (props.tone === 'overdue') return 'var(--overdue)'
  return primaryLabel.value?.colorHex ?? 'var(--ink-deep)'
})

/**
 * Lightweight relative-time helper — mirrors the `30/05`, `wk 18`, `2d` style
 * in the JSX spec. We only care about a stable, terse string for the metadata
 * strip; full localisation is intentionally out of scope.
 */
function formatRelative(iso: string | null | undefined): string {
  if (!iso) return ''
  const t = Date.parse(iso)
  if (Number.isNaN(t)) return ''
  const diffMs = Date.now() - t
  const sec = Math.round(diffMs / 1000)
  const min = Math.round(sec / 60)
  const hr = Math.round(min / 60)
  const day = Math.round(hr / 24)
  if (sec < 60) return 'now'
  if (min < 60) return `${min}m`
  if (hr < 24) return `${hr}h`
  if (day < 7) return `${day}d`
  if (day < 30) return `${Math.round(day / 7)}w`
  if (day < 365) return `${Math.round(day / 30)}mo`
  return `${Math.round(day / 365)}y`
}

const ageLabel = computed(() => formatRelative(props.card.updatedAt ?? props.card.createdAt))

const isOverdue = computed(() => props.tone === 'overdue')

const formattedDueDate = computed(() => formatCalendarDate(
  props.card.dueDate,
  { year: 'numeric', month: 'numeric', day: 'numeric' },
  'en-US',
))

const isDueDateOverdue = computed(() => isCalendarDateOverdue(props.card.dueDate))

const openerLabel = computed(() => {
  const base = `Card ${props.card.title}`
  if (!formattedDueDate.value) return base
  return `${base}, due ${formattedDueDate.value}${isDueDateOverdue.value ? ', overdue' : ''}`
})

const tagstampTone = computed(() => {
  if (props.tone === 'proposed') return 'ember'
  if (props.tone === 'applied') return 'applied'
  if (props.tone === 'overdue') return 'overdue'
  if (isDueDateOverdue.value) return 'overdue'
  return null
})

const tagstampLabel = computed(() => {
  if (props.tone) return props.tone.toUpperCase()
  return isDueDateOverdue.value ? 'OVERDUE' : ''
})

function isDragHandleTarget(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[data-action="drag-card-handle"]') !== null
}

function onClick() {
  emit('click', props.card)
}

function onDragStart(e: DragEvent) {
  if (!isDragHandleTarget(e.target)) {
    e.preventDefault()
    return
  }

  e.stopPropagation()
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', props.card.id)
  }
  emit('dragstart', props.card, e)
}

function onDragEnd() {
  emit('dragend')
}

function onDragHandleMouseDown() {
  window.getSelection()?.removeAllRanges()
}
const proposalMarker = useBoardProposalMarker('card', () => props.card.id)
</script>

<template>
  <article
    :class="[
      'paper-board-card',
      `paper-board-card--${variant}`,
      selected ? 'paper-board-card--selected' : '',
    ]"
    :data-card-id="card.id"
    :data-proposal-change="proposalMarker ? true : undefined"
    :data-variant="variant"
    :data-tone="tone || undefined"
    :data-presentation="presentation"
    draggable="false"
    @dragstart="onDragStart"
    @dragend="onDragEnd"
  >
    <span v-if="proposalMarker" class="td-proposal-marker">{{ proposalMarker }}</span>
    <button
      type="button"
      class="paper-board-card__open"
      data-action="open-card"
      :aria-label="openerLabel"
      @click="onClick"
      @keydown.enter.stop.prevent="onClick"
      @keydown.space.stop.prevent="onClick"
    />

    <span
      v-if="variant === 'ribbon'"
      class="paper-board-card__ribbon"
      :style="{ background: ribbonColor }"
      aria-hidden="true"
    />

    <div class="paper-board-card__body">
      <header class="paper-board-card__header">
        <span v-show="showSecondaryDetails" class="tk-serial paper-board-card__serial">{{ serial }}</span>
        <span class="paper-board-card__header-actions">
          <span
            v-if="tagstampTone"
            class="tagstamp paper-board-card__tagstamp"
            :data-tone="tagstampTone"
            :style="{ color:
              tagstampTone === 'ember' ? 'var(--ember)' :
              tagstampTone === 'applied' ? 'var(--applied)' :
              'var(--overdue)' }"
          >{{ tagstampLabel }}</span>
          <span
            class="paper-board-card__drag-handle"
            data-action="drag-card-handle"
            draggable="true"
            :title="`Drag ${card.title}`"
            aria-hidden="true"
            @pointerdown="onDragHandleMouseDown"
          >
            <span aria-hidden="true">⋮⋮</span>
          </span>
        </span>
      </header>

      <h4 class="paper-board-card__title">{{ card.title }}</h4>

      <p v-if="presentation !== 'classic' && card.isBlocked" class="paper-board-card__blocked">
        <strong>Blocked</strong><span v-if="card.blockReason"> · {{ card.blockReason }}</span>
      </p>

      <p
        v-if="card.description"
        v-show="showSecondaryDetails"
        class="paper-board-card__excerpt tk-body"
      >{{ card.description }}</p>

      <footer class="paper-board-card__meta">
        <span
          v-for="label in card.labels"
          v-show="showSecondaryDetails"
          :key="label.id"
          class="paper-board-card__label"
          :style="{ color: label.colorHex }"
        >· {{ label.name }}</span>
        <span class="paper-board-card__spacer" />
        <span
          v-if="formattedDueDate"
          class="paper-board-card__due-date"
          :class="{ 'paper-board-card__due-date--overdue': isDueDateOverdue }"
        >Due {{ formattedDueDate }}</span>
        <span v-if="subtasks" class="paper-board-card__subtasks">
          {{ subtasks.done }}/{{ subtasks.total }}
        </span>
        <span
          v-if="ageLabel"
          v-show="showSecondaryDetails"
          class="paper-board-card__age"
          :title="presentation === 'control' ? `Updated ${card.updatedAt}` : undefined"
          :style="{ color: isOverdue ? 'var(--overdue)' : 'var(--mute)' }"
        >{{ ageLabel }}</span>
      </footer>
      <button v-if="isZen && (card.description || card.labels.length || ageLabel)" type="button"
        class="paper-board-card__disclosure" :aria-expanded="detailsExpanded"
        :aria-label="`${detailsExpanded ? 'Hide' : 'Show'} details for ${card.title}`"
        @click.stop="detailsExpanded = !detailsExpanded" @keydown.enter.stop @keydown.space.stop>
        {{ detailsExpanded ? 'Less detail' : 'More detail' }} <span aria-hidden="true">{{ detailsExpanded ? '−' : '+' }}</span>
      </button>
      <span v-if="presentation === 'control'" class="paper-board-card__operation-note">{{ card.labels.length }} {{ card.labels.length === 1 ? 'label' : 'labels' }} · {{ card.isBlocked ? 'Needs unblocking' : 'Not blocked' }}</span>
    </div>
  </article>
</template>

<style scoped>
.paper-board-card {
  position: relative;
  display: block;
  width: 100%;
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: var(--r-2);
  box-shadow: var(--shadow-card);
  font-family: var(--sans);
  color: var(--ink);
  cursor: pointer;
  transition:
    box-shadow var(--d-quick) var(--ease-paper),
    border-color var(--d-quick) var(--ease-paper),
    transform var(--d-quick) var(--ease-press);
  overflow: hidden;
}

/* 1px lift on hover via shadow + inset highlight, no scale. */
.paper-board-card:hover {
  box-shadow:
    0 1px 0 var(--line),
    inset 0 1px 0 #ffffff80,
    var(--shadow-lift);
  border-color: var(--ink-2);
}

.paper-board-card--selected,
.paper-board-card:has(.paper-board-card__open:focus-visible) {
  outline: 2px solid var(--ember);
  outline-offset: 1px;
  border-color: var(--ember);
}

.paper-board-card__open {
  position: absolute;
  inset: 0;
  z-index: 1;
  width: 100%;
  padding: 0;
  border: 0;
  border-radius: inherit;
  background: transparent;
  cursor: pointer;
}

.paper-board-card__ribbon {
  position: absolute;
  top: 0;
  left: 0;
  bottom: 0;
  width: 4px;
  background: var(--ink-deep);
}

.paper-board-card--ribbon .paper-board-card__body {
  padding-left: 14px;
}

.paper-board-card__body {
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.paper-board-card__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  margin-bottom: 2px;
}

.paper-board-card__serial {
  font-family: var(--mono);
  font-size: 10.5px;
  color: var(--faint);
  letter-spacing: .04em;
}

.paper-board-card__header-actions {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.paper-board-card__drag-handle {
  position: relative;
  z-index: 2;
  display: inline-grid;
  place-items: center;
  width: 24px;
  height: 24px;
  padding: 0;
  border: 1px solid var(--line);
  border-radius: var(--r-1);
  background: transparent;
  color: var(--mute);
  cursor: grab;
  font-family: var(--mono);
  font-size: 11px;
  line-height: 1;
}

.paper-board-card__drag-handle:active {
  cursor: grabbing;
}

.paper-board-card__drag-handle:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 1px;
}

.paper-board-card__tagstamp {
  font-family: var(--mono);
  font-size: 9.5px;
  letter-spacing: .22em;
  text-transform: uppercase;
  font-weight: 600;
  border: 1px solid currentColor;
  padding: 2px 6px;
  border-radius: 1px;
  line-height: 1;
}

.paper-board-card__title {
  margin: 0;
  font-family: var(--serif);
  font-weight: 500;
  font-size: 14.5px;
  line-height: 1.18;
  letter-spacing: -.005em;
  color: var(--ink-deep);
}

.paper-board-card__excerpt {
  margin: 0;
  color: var(--ink-2);
  font-family: var(--sans);
  font-size: 12.5px;
  line-height: 1.4;
  display: -webkit-box;
  -webkit-line-clamp: 1;
  line-clamp: 1;
  -webkit-box-orient: vertical;
  overflow: hidden;
  text-overflow: ellipsis;
}

.paper-board-card__meta {
  margin-top: 8px;
  padding-top: 6px;
  border-top: 1px dashed var(--line-soft);
  display: flex;
  align-items: center;
  gap: 8px;
  font-family: var(--mono);
  font-size: 11px;
  color: var(--mute);
  letter-spacing: .02em;
}

.paper-board-card__label {
  letter-spacing: .14em;
  text-transform: uppercase;
  font-size: 9.5px;
}

.paper-board-card__spacer {
  flex: 1;
}

.paper-board-card__due-date,
.paper-board-card__subtasks,
.paper-board-card__age {
  font-variant-numeric: tabular-nums;
}

.paper-board-card__due-date {
  white-space: nowrap;
}

.paper-board-card__due-date--overdue {
  color: var(--overdue);
}
</style>

<style scoped>
.paper-board-card__blocked { margin: 4px 0; padding: 6px 8px; border-left: 2px solid var(--overdue); background: var(--overdue-tint); color: var(--overdue); font: 12px/1.5 var(--sans); overflow-wrap: anywhere; }
.paper-board-card__disclosure { position: relative; z-index: 2; display: flex; justify-content: space-between; width: 100%; padding: 8px 0 2px; border: 0; background: transparent; color: var(--mute); font: 11px var(--sans); text-align: left; cursor: pointer; }
.paper-board-card__disclosure:focus-visible { outline: 2px solid var(--ember); outline-offset: 2px; }
.paper-board-card__operation-note { font: 10px/1.4 var(--mono); color: var(--mute); }
.paper-board-card[data-presentation="zen"] { border-radius: 12px; box-shadow: none; }
.paper-board-card[data-presentation="zen"] .paper-board-card__body { padding: 18px; gap: 8px; }
.paper-board-card[data-presentation="zen"] .paper-board-card__title { font: 500 19px/1.4 var(--serif); }
.paper-board-card[data-presentation="zen"] .paper-board-card__header { justify-content: flex-end; }
.paper-board-card[data-presentation="zen"] .paper-board-card__excerpt { -webkit-line-clamp: 3; line-clamp: 3; }
.paper-board-card[data-presentation="zen"] .paper-board-card__meta { margin-top: 0; border-top: 0; padding-top: 0; }
.paper-board-card[data-presentation="control"] { border-radius: 2px; box-shadow: none; border-left: 3px solid var(--line); }
.paper-board-card[data-presentation="control"] .paper-board-card__body { padding: 7px 10px; gap: 2px; }
.paper-board-card[data-presentation="control"] .paper-board-card__title { font: 600 13px/1.35 var(--sans); }
.paper-board-card[data-presentation="control"] .paper-board-card__meta { margin-top: 3px; padding-top: 3px; gap: 5px; }
.paper-board-card[data-presentation="control"] .paper-board-card__blocked { margin: 2px 0; padding: 3px 6px; }
</style>
