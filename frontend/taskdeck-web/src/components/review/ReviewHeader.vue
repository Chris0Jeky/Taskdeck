<script setup lang="ts">
import InputAssistField from '../common/InputAssistField.vue'
import type { InputAssistOption } from '../../utils/inputAssist'

defineProps<{
  activeBoardFilter: string
  activeBoardName: string
  boardFilterInput: string
  boardOptions: InputAssistOption[]
  loadingBoards: boolean
  showCompleted: boolean
  proposalsLoading: boolean
  dismissableCount: number
}>()

const emit = defineEmits<{
  (e: 'update:boardFilterInput', value: string): void
  (e: 'update:showCompleted', value: boolean): void
  (e: 'select-board', option: InputAssistOption): void
  (e: 'clear-board-filter'): void
  (e: 'dismiss-applied'): void
  (e: 'refresh'): void
  (e: 'open-inbox'): void
  (e: 'navigate', path: string): void
}>()

function onBoardFilterInput(value: string) {
  emit('update:boardFilterInput', value)
}
</script>

<template>
  <header class="td-panel td-review__hero">
    <div class="td-review__hero-copy">
      <span class="td-review__eyebrow" aria-hidden="true">Review</span>
      <h1 class="td-page-title">Review</h1>
      <p class="td-review__subtitle">
        Nothing changes on a board until you approve it here.
      </p>
      <p v-if="activeBoardFilter" class="td-review__board-filter">
        Showing proposals for <strong>{{ activeBoardName }}</strong>.
        <button class="td-btn td-btn--link td-btn--sm" @click="$emit('clear-board-filter')">Show all boards</button>
      </p>
    </div>

    <div class="td-review__board-selector">
      <InputAssistField
        :model-value="boardFilterInput"
        :options="boardOptions"
        aria-label="Filter by board"
        placeholder="Filter proposals by board..."
        no-results-text="No matching boards."
        :disabled="loadingBoards"
        @update:model-value="onBoardFilterInput"
        @select="(option: InputAssistOption) => $emit('select-board', option)"
      />
    </div>

    <div class="td-review__hero-actions">
      <label class="td-review__toggle">
        <input
          :checked="showCompleted"
          type="checkbox"
          class="td-review__toggle-input"
          @change="$emit('update:showCompleted', ($event.target as HTMLInputElement).checked)"
        />
        <span class="td-review__toggle-label">Show completed</span>
      </label>
      <button
        class="td-btn td-btn--secondary"
        :disabled="dismissableCount === 0"
        @click="$emit('dismiss-applied')"
      >
        Clear completed ({{ dismissableCount }})
      </button>
      <button class="td-btn td-btn--primary" :disabled="proposalsLoading" @click="$emit('refresh')">
        {{ proposalsLoading ? 'Refreshing...' : 'Refresh Review' }}
      </button>
      <button class="td-btn td-btn--secondary" @click="$emit('open-inbox')">Open Inbox</button>
      <button class="td-btn td-btn--secondary" @click="$emit('navigate', '/workspace/automations/queue')">
        Open Queue (Advanced)
      </button>
      <button class="td-btn td-btn--secondary" @click="$emit('navigate', '/workspace/automations/chat')">
        Open Chat (Advanced)
      </button>
    </div>
  </header>
</template>

<style scoped>
.td-review__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-6);
  align-items: flex-start;
}

.td-review__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-review__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

.td-review__subtitle {
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-review__board-filter {
  margin: 0;
  color: var(--td-color-primary);
  font-size: var(--td-font-sm);
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
}

.td-review__board-selector {
  max-width: 320px;
}

.td-review__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  justify-content: flex-end;
  align-items: center;
}

.td-review__toggle {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  cursor: pointer;
  user-select: none;
}

.td-review__toggle-input {
  accent-color: var(--td-color-primary);
  width: 16px;
  height: 16px;
  cursor: pointer;
}

.td-review__toggle-label {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-secondary);
  white-space: nowrap;
}

@media (max-width: 900px) {
  .td-review__hero {
    flex-direction: column;
  }

  .td-review__hero-actions {
    justify-content: flex-start;
  }
}

@media (max-width: 640px) {
  .td-review__hero {
    gap: var(--td-space-4);
    padding: var(--td-space-4);
  }

  .td-review__board-selector {
    max-width: 100%;
    width: 100%;
  }

  .td-review__hero-actions {
    flex-direction: column;
    width: 100%;
  }

  .td-review__hero-actions .td-btn {
    width: 100%;
    min-height: 44px;
    justify-content: center;
  }

  .td-review__board-filter {
    flex-direction: column;
    gap: var(--td-space-1);
  }
}
</style>
