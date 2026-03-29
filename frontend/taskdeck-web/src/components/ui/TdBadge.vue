<script setup lang="ts">
export type BadgeVariant = 'default' | 'primary' | 'success' | 'warning' | 'error' | 'info'

const props = withDefaults(
  defineProps<{
    variant?: BadgeVariant
    size?: 'sm' | 'md'
  }>(),
  {
    variant: 'default',
    size: 'md',
  },
)
</script>

<template>
  <span :class="['td-badge', `td-badge--${props.variant}`, `td-badge--${props.size}`]">
    <slot />
  </span>
</template>

<style scoped>
.td-badge {
  display: inline-flex;
  align-items: center;
  font-weight: 600;
  white-space: nowrap;
  border-radius: 9999px;
  user-select: none;
}

/* ── Sizes ── */
.td-badge--sm {
  font-size: var(--td-font-xs);
  padding: 1px var(--td-space-1);
}

.td-badge--md {
  font-size: var(--td-font-sm);
  padding: var(--td-space-1) var(--td-space-2);
}

/* ── Variants ── */
.td-badge--default {
  background: var(--td-surface-container-high);
  color: var(--td-text-secondary);
}

.td-badge--primary {
  background: var(--td-color-primary-light);
  color: var(--td-color-primary);
}

.td-badge--success {
  background: var(--td-color-success-light);
  color: var(--td-color-success);
}

.td-badge--warning {
  background: var(--td-color-warning-light);
  color: var(--td-color-warning);
}

.td-badge--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

.td-badge--info {
  background: var(--td-color-info-light);
  color: var(--td-color-info);
}
</style>
