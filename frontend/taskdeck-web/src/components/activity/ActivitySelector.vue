<script setup lang="ts">
import { useSessionStore } from '../../store/sessionStore'
import type { ViewMode, DiscoverableEntityType, SelectorOption } from '../../composables/useActivityQuery'

const session = useSessionStore()

defineProps<{
  viewMode: ViewMode
  selectedBoardId: string
  selectedEntityType: DiscoverableEntityType | ''
  selectedEntityBoardId: string
  selectedEntityId: string
  limit: number
  loadingEntitySource: boolean
  boardOptions: SelectorOption[]
  requiresEntityBoardContext: boolean
  entityOptions: SelectorOption[]
  canFetch: boolean
  selectedIdForCopy: string
  selectedIdLabel: string
}>()

const emit = defineEmits<{
  'update:viewMode': [value: ViewMode]
  'update:selectedBoardId': [value: string]
  'update:selectedEntityType': [value: DiscoverableEntityType | '']
  'update:selectedEntityBoardId': [value: string]
  'update:selectedEntityId': [value: string]
  'update:limit': [value: number]
  'fetch': []
  'copyId': []
}>()
</script>

<template>
  <div class="td-activity__controls">
    <div class="td-form-row">
      <select
        id="activity-view-mode"
        :value="viewMode"
        class="td-input"
        aria-label="Activity view mode"
        @change="emit('update:viewMode', ($event.target as HTMLSelectElement).value as ViewMode)"
      >
        <option value="board">Board History</option>
        <option value="entity">Entity History</option>
        <option value="user">User History</option>
      </select>

      <select
        v-if="viewMode === 'board'"
        id="activity-board-select"
        :value="selectedBoardId"
        class="td-input"
        aria-label="Select board"
        @change="emit('update:selectedBoardId', ($event.target as HTMLSelectElement).value)"
      >
        <option value="" disabled>Select board...</option>
        <option v-for="board in boardOptions" :key="board.id" :value="board.id">
          {{ board.label }}
        </option>
      </select>

      <template v-if="viewMode === 'entity'">
        <select
          id="activity-entity-type"
          :value="selectedEntityType"
          class="td-input"
          aria-label="Select entity type"
          @change="emit('update:selectedEntityType', ($event.target as HTMLSelectElement).value as DiscoverableEntityType | '')"
        >
          <option value="" disabled>Select entity type...</option>
          <option value="Board">Board</option>
          <option value="Column">Column</option>
          <option value="Card">Card</option>
          <option value="Label">Label</option>
        </select>

        <select
          v-if="requiresEntityBoardContext"
          id="activity-entity-board-select"
          :value="selectedEntityBoardId"
          class="td-input"
          aria-label="Select board context"
          @change="emit('update:selectedEntityBoardId', ($event.target as HTMLSelectElement).value)"
        >
          <option value="" disabled>Select board context...</option>
          <option v-for="board in boardOptions" :key="board.id" :value="board.id">
            {{ board.label }}
          </option>
        </select>

        <select
          v-if="selectedEntityType"
          id="activity-entity-select"
          :value="selectedEntityId"
          class="td-input"
          :disabled="loadingEntitySource || entityOptions.length === 0"
          aria-label="Select entity"
          @change="emit('update:selectedEntityId', ($event.target as HTMLSelectElement).value)"
        >
          <option value="" disabled>Select entity...</option>
          <option v-for="option in entityOptions" :key="option.id" :value="option.id">
            {{ option.secondary ? `${option.label} (${option.secondary})` : option.label }}
          </option>
        </select>
      </template>

      <div v-if="viewMode === 'user'" class="td-current-user">
        Current user: <strong>{{ session.username || 'me' }}</strong>
      </div>

      <select
        :value="limit"
        class="td-input td-input--sm"
        aria-label="Activity limit"
        @change="emit('update:limit', Number(($event.target as HTMLSelectElement).value))"
      >
        <option :value="25">25</option>
        <option :value="50">50</option>
        <option :value="100">100</option>
      </select>

      <button class="td-btn td-btn--primary td-btn--sm" :disabled="!canFetch" @click="emit('fetch')">
        Fetch
      </button>
    </div>

    <div v-if="loadingEntitySource" class="td-helper">Loading entities for selected board...</div>
    <div
      v-else-if="viewMode === 'entity' && selectedEntityType && entityOptions.length === 0"
      class="td-helper"
    >
      No entities match this selection yet. Try another board context or switch back to board history for a broader
      view.
    </div>

    <div v-if="selectedIdForCopy" class="td-id-affordance">
      <span class="td-id-affordance__label">{{ selectedIdLabel }}</span>
      <code class="td-id-affordance__value">{{ selectedIdForCopy }}</code>
      <button
        class="td-btn td-btn--ghost td-btn--sm"
        :aria-label="`Copy ${selectedIdLabel} to clipboard`"
        @click="emit('copyId')"
      >
        Copy Raw ID
      </button>
    </div>
  </div>
</template>

<style scoped>
.td-activity__controls { background: var(--td-surface-primary); border-radius: var(--td-radius-lg); padding: var(--td-space-4); margin-bottom: var(--td-space-4); border: 1px solid var(--td-border-default); }
.td-form-row { display: flex; gap: var(--td-space-2); flex-wrap: wrap; align-items: center; }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); min-width: 180px; }
.td-input--sm { min-width: 80px; max-width: 90px; }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-current-user { font-size: var(--td-font-sm); color: var(--td-text-secondary); padding: var(--td-space-1) var(--td-space-2); background: var(--td-surface-secondary); border-radius: var(--td-radius-sm); }
.td-helper { margin-top: var(--td-space-2); color: var(--td-text-secondary); font-size: var(--td-font-sm); }
.td-id-affordance { margin-top: var(--td-space-3); display: flex; align-items: center; gap: var(--td-space-2); flex-wrap: wrap; }
.td-id-affordance__label { font-size: var(--td-font-xs); color: var(--td-text-tertiary); text-transform: uppercase; letter-spacing: 0.04em; }
.td-id-affordance__value { font-size: var(--td-font-xs); background: var(--td-surface-secondary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-sm); padding: 0 var(--td-space-2); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover { background: var(--td-color-primary-hover); }
.td-btn--ghost { background: var(--td-surface-secondary); color: var(--td-text-secondary); border: 1px solid var(--td-border-default); }
.td-btn--ghost:hover { background: var(--td-surface-tertiary); }
</style>
