<script setup lang="ts">
import PaperIcon from './PaperIcon.vue'

/**
 * PaperSubtaskLedger — checklist rendered as a `.rule-ledger`-style ledger.
 * Used by `PaperCardDetailView` for the subtasks list.  Each row is a flat
 * dashed hairline rule with a hand-stitched checkbox in the gutter.
 *
 * The `subtasks` prop is intentionally minimal — the real `Card` model in
 * `types/board.ts` does not yet expose subtasks, so this component takes a
 * generic shape that can be sourced from anywhere (provenance, card body
 * markdown, etc.).
 */
export type PaperSubtaskItem = {
  id: string
  label: string
  done: boolean
}

defineProps<{
  subtasks: PaperSubtaskItem[]
}>()

const emit = defineEmits<{
  toggle: [id: string]
}>()

function toggle(id: string) {
  emit('toggle', id)
}
</script>

<template>
  <ul class="paper-subtask-ledger rule-ledger" data-paper-subtask-ledger>
    <li
      v-for="task in subtasks"
      :key="task.id"
      class="paper-subtask-ledger__row"
      :data-done="task.done ? 'true' : 'false'"
    >
      <button
        type="button"
        class="paper-subtask-ledger__check"
        :aria-pressed="task.done"
        :aria-label="task.done ? `Mark '${task.label}' undone` : `Mark '${task.label}' done`"
        @click="toggle(task.id)"
      >
        <PaperIcon v-if="task.done" name="check" />
      </button>
      <span class="paper-subtask-ledger__label" :class="{ 'paper-subtask-ledger__label--done': task.done }">
        {{ task.label }}
      </span>
    </li>
  </ul>
</template>

<style scoped>
.paper-subtask-ledger {
  list-style: none;
  margin: 0;
  padding: 0;
  font-family: var(--sans);
}

.paper-subtask-ledger__row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 0;
  border-bottom: 1px solid var(--line-soft);
  font-size: 13px;
  color: var(--ink);
}

.paper-subtask-ledger__row:last-child {
  border-bottom: none;
}

.paper-subtask-ledger__check {
  width: 14px;
  height: 14px;
  border: 1.5px solid var(--ink-deep);
  border-radius: 2px;
  display: inline-grid;
  place-items: center;
  background: transparent;
  color: var(--applied);
  cursor: pointer;
  padding: 0;
  flex: none;
}

.paper-subtask-ledger__label {
  flex: 1;
  color: var(--ink);
}

.paper-subtask-ledger__label--done {
  text-decoration: line-through;
  text-decoration-color: var(--applied);
  color: var(--mute);
}
</style>
