<script setup lang="ts">
import type { BoardPresenceMember } from '../../types/realtime'

defineProps<{
  boardName: string
  boardDescription: string | null
  isDemoBoard: boolean
  presenceMembers: BoardPresenceMember[]
  showFilterPanel: boolean
  filteredCardCount: number
  totalCardCount: number
}>()

defineEmits<{
  back: []
  toggleFilter: []
  showKeyboardHelp: []
  showLabelManager: []
  showStarterPackCatalog: []
  showBoardSettings: []
  toggleColumnForm: []
}>()
</script>

<template>
  <div class="td-board-toolbar">
    <div class="td-board-toolbar__left">
      <button
        @click="$emit('back')"
        class="td-board-toolbar__back-btn"
        aria-label="Back to boards"
      >
        <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M15 19l-7-7 7-7"
          />
        </svg>
      </button>
      <div>
        <div class="flex flex-wrap items-center gap-2">
          <h1 class="td-board-toolbar__title">
            {{ boardName }}
          </h1>
          <span
            v-if="isDemoBoard"
            class="td-board-toolbar__demo-badge"
          >
            Demo board
          </span>
        </div>
        <p v-if="boardDescription" class="td-board-toolbar__description">
          {{ boardDescription }}
        </p>
        <div class="td-board-toolbar__presence">
          <span class="td-board-toolbar__presence-label">Live</span>
          <span
            v-if="presenceMembers.length === 0"
            class="td-board-toolbar__presence-empty"
          >
            No active collaborators
          </span>
          <span
            v-for="member in presenceMembers"
            :key="member.userId"
            class="td-board-toolbar__presence-member"
            data-presence-user
          >
            <span>{{ member.displayName || member.userId.slice(0, 8) }}</span>
            <span v-if="member.editingCardId" class="td-board-toolbar__presence-editing">(editing)</span>
          </span>
        </div>
      </div>
    </div>

    <div class="td-board-toolbar__actions">
      <button
        @click="$emit('toggleFilter')"
        :class="[
          'td-board-toolbar__icon-btn',
          showFilterPanel ? 'td-board-toolbar__icon-btn--active' : ''
        ]"
        title="Filter Cards (Press f)"
      >
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-6.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z" />
        </svg>
        <span v-if="filteredCardCount < totalCardCount" class="td-board-toolbar__filter-count">
          {{ filteredCardCount }}
        </span>
      </button>
      <button
        @click="$emit('showKeyboardHelp')"
        class="td-board-toolbar__icon-btn"
        title="Keyboard Shortcuts (Press ?)"
      >
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8.228 9c.549-1.165 2.03-2 3.772-2 2.21 0 4 1.343 4 3 0 1.4-1.278 2.575-3.006 2.907-.542.104-.994.54-.994 1.093m0 3h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
      </button>
      <button
        @click="$emit('showLabelManager')"
        class="td-board-toolbar__text-btn"
        title="Manage Labels"
      >
        <svg class="w-5 h-5 inline-block mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
        </svg>
        Labels
      </button>
      <button
        @click="$emit('showStarterPackCatalog')"
        class="td-board-toolbar__text-btn"
        title="Browse Starter Packs"
      >
        <svg class="w-5 h-5 inline-block mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6.253v13m0-13C10.832 5.483 9.246 5 7.5 5 4.462 5 2 6.79 2 9v9c0-2.21 2.462-4 5.5-4 1.746 0 3.332.483 4.5 1.253m0-9C13.168 5.483 14.754 5 16.5 5c3.038 0 5.5 1.79 5.5 4v9c0-2.21-2.462-4-5.5-4-1.746 0-3.332.483-4.5 1.253" />
        </svg>
        Starter Packs
      </button>
      <button
        @click="$emit('showBoardSettings')"
        class="td-board-toolbar__icon-btn"
        title="Board Settings"
      >
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
        </svg>
      </button>
      <button
        @click="$emit('toggleColumnForm')"
        class="td-board-toolbar__primary-btn"
      >
        + Add Column
      </button>
    </div>
  </div>
</template>

<style scoped>
/* ── Board Toolbar — token-based layout ── */
.td-board-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--td-space-4);
}

.td-board-toolbar__left {
  display: flex;
  align-items: center;
  gap: var(--td-space-5);
}

/* ── Back button ── */
.td-board-toolbar__back-btn {
  color: var(--td-text-tertiary);
  transition: color var(--td-transition-fast);
}

.td-board-toolbar__back-btn:hover {
  color: var(--td-text-primary);
}

.td-board-toolbar__back-btn:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
  border-radius: var(--td-radius-md);
}

/* ── Board title ── */
.td-board-toolbar__title {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-2xl);
  font-weight: 800;
  color: var(--td-text-primary);
  letter-spacing: -0.02em;
}

/* ── Demo badge ── */
.td-board-toolbar__demo-badge {
  display: inline-flex;
  align-items: center;
  border-radius: 9999px;
  border: 1px solid var(--td-color-primary-light);
  background: var(--td-color-ember-dim);
  padding: 1px var(--td-space-2);
  font-size: var(--td-font-xs);
  font-weight: 600;
  color: var(--td-color-primary);
}

/* ── Description ── */
.td-board-toolbar__description {
  font-size: var(--td-font-sm);
  color: var(--td-text-muted);
  margin-top: var(--td-space-1);
}

/* ── Presence ── */
.td-board-toolbar__presence {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--td-space-2);
  margin-top: var(--td-space-3);
}

.td-board-toolbar__presence-label {
  font-size: var(--td-font-xs);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--td-text-tertiary);
}

.td-board-toolbar__presence-empty {
  display: inline-flex;
  align-items: center;
  border-radius: 9999px;
  background: var(--td-surface-container-highest);
  padding: 1px var(--td-space-2);
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
}

.td-board-toolbar__presence-member {
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-1);
  border-radius: 9999px;
  background: var(--td-color-ember-dim);
  padding: 1px var(--td-space-2);
  font-size: var(--td-font-xs);
  color: var(--td-color-primary);
}

.td-board-toolbar__presence-editing {
  font-weight: 500;
  color: var(--td-color-warning);
}

/* ── Actions row ── */
.td-board-toolbar__actions {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
}

/* ── Icon button (filter, help, settings) ── */
.td-board-toolbar__icon-btn {
  position: relative;
  padding: var(--td-space-2);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  color: var(--td-text-tertiary);
  transition:
    background-color var(--td-transition-fast),
    color var(--td-transition-fast),
    border-color var(--td-transition-fast);
}

.td-board-toolbar__icon-btn:hover {
  background: var(--td-surface-bright);
  color: var(--td-text-secondary);
}

.td-board-toolbar__icon-btn:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

.td-board-toolbar__icon-btn--active {
  background: var(--td-color-ember-dim);
  color: var(--td-color-primary);
  border-color: var(--td-color-primary);
}

/* ── Filter count indicator ── */
.td-board-toolbar__filter-count {
  position: absolute;
  top: -4px;
  right: -4px;
  display: flex;
  height: 1.25rem;
  width: 1.25rem;
  align-items: center;
  justify-content: center;
  border-radius: 9999px;
  background: var(--td-color-primary);
  font-size: 10px;
  font-weight: 700;
  color: var(--td-text-inverse);
}

/* ── Text button (Labels, Starter Packs) ── */
.td-board-toolbar__text-btn {
  padding: var(--td-space-2) var(--td-space-5);
  font-size: var(--td-font-sm);
  font-weight: 500;
  color: var(--td-text-muted);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  transition:
    background-color var(--td-transition-fast),
    color var(--td-transition-fast);
}

.td-board-toolbar__text-btn:hover {
  background: var(--td-surface-bright);
  color: var(--td-text-primary);
}

.td-board-toolbar__text-btn:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

/* ── Primary action button ── */
.td-board-toolbar__primary-btn {
  padding: var(--td-space-2) var(--td-space-5);
  font-size: var(--td-font-sm);
  font-weight: 600;
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
  border-radius: var(--td-radius-lg);
  transition:
    background-color var(--td-transition-fast),
    filter var(--td-transition-fast);
}

.td-board-toolbar__primary-btn:hover {
  background: var(--td-color-primary-hover);
}

.td-board-toolbar__primary-btn:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}
</style>
