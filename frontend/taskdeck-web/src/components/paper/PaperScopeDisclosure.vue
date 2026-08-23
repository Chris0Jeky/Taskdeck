<script setup lang="ts">
defineProps<{
  label: string
  clearLabel: string
}>()

defineEmits<{
  (event: 'clear'): void
}>()
</script>

<template>
  <p class="paper-scope-disclosure" data-testid="paper-scope-disclosure" role="status">
    <span>{{ label }}</span>
    <button
      type="button"
      class="paper-scope-disclosure__clear"
      data-testid="paper-scope-clear"
      @click="$emit('clear')"
    >{{ clearLabel }}</button>
  </p>
</template>

<style scoped>
.paper-scope-disclosure {
  display: inline-flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin: 10px 0 0;
  padding: 5px 8px;
  border: 1px solid var(--line);
  border-radius: 999px;
  background: var(--paper-card);
  color: var(--ink-2);
  font-family: var(--mono);
  font-size: 11px;
  line-height: 1.35;
}

.paper-scope-disclosure__clear {
  border: 0;
  padding: 0;
  background: transparent;
  color: var(--ember);
  cursor: pointer;
  font: inherit;
  text-decoration: underline;
  text-underline-offset: 2px;
}

.paper-scope-disclosure__clear:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 2px;
}
</style>
