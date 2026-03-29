<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    color?: string
    removable?: boolean
  }>(),
  {
    color: '',
    removable: false,
  },
)

const emit = defineEmits<{
  remove: []
}>()
</script>

<template>
  <span
    class="td-tag"
    :style="props.color ? { '--td-tag-color': props.color } : undefined"
    :class="{ 'td-tag--custom': !!props.color }"
  >
    <span class="td-tag__label">
      <slot />
    </span>
    <button
      v-if="props.removable"
      class="td-tag__remove"
      aria-label="Remove tag"
      @click="emit('remove')"
    >
      <svg width="10" height="10" viewBox="0 0 10 10" fill="none" aria-hidden="true">
        <path d="M2.5 2.5L7.5 7.5M7.5 2.5L2.5 7.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      </svg>
    </button>
  </span>
</template>

<style scoped>
.td-tag {
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-1);
  font-size: var(--td-font-xs);
  font-weight: 500;
  padding: 2px var(--td-space-2);
  border-radius: var(--td-radius-sm);
  background: var(--td-surface-container-high);
  color: var(--td-text-secondary);
  border: 1px solid var(--td-border-ghost);
  white-space: nowrap;
  user-select: none;
}

.td-tag--custom {
  background: color-mix(in srgb, var(--td-tag-color) 15%, transparent);
  color: var(--td-tag-color);
  border-color: color-mix(in srgb, var(--td-tag-color) 30%, transparent);
}

.td-tag__label {
  line-height: 1.4;
}

.td-tag__remove {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  color: inherit;
  cursor: pointer;
  padding: 0;
  border-radius: var(--td-radius-sm);
  opacity: 0.6;
  transition: opacity var(--td-transition-fast);
}

.td-tag__remove:hover {
  opacity: 1;
}

.td-tag__remove:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}
</style>
