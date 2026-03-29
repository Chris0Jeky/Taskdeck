<script setup lang="ts">
export type IconButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type IconButtonSize = 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    variant?: IconButtonVariant
    size?: IconButtonSize
    disabled?: boolean
    loading?: boolean
    label: string
  }>(),
  {
    variant: 'ghost',
    size: 'md',
    disabled: false,
    loading: false,
  },
)

const emit = defineEmits<{
  click: [event: MouseEvent]
}>()

function handleClick(event: MouseEvent) {
  if (props.disabled || props.loading) {
    return
  }
  emit('click', event)
}
</script>

<template>
  <button
    type="button"
    :disabled="props.disabled || props.loading"
    :class="[
      'td-icon-btn',
      `td-icon-btn--${props.variant}`,
      `td-icon-btn--${props.size}`,
      { 'td-icon-btn--loading': props.loading },
    ]"
    :aria-label="props.label"
    :aria-disabled="props.disabled || props.loading"
    :aria-busy="props.loading"
    @click="handleClick"
  >
    <span v-if="props.loading" class="td-icon-btn__spinner" aria-hidden="true" />
    <span v-else class="td-icon-btn__icon">
      <slot />
    </span>
  </button>
</template>

<style scoped>
.td-icon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--td-radius-md);
  border: 1px solid transparent;
  background: transparent;
  cursor: pointer;
  transition: background var(--td-transition-fast), color var(--td-transition-fast),
    box-shadow var(--td-transition-fast);
  padding: 0;
  line-height: 1;
  user-select: none;
}

/* ── Sizes ── */
.td-icon-btn--sm {
  width: 1.75rem;
  height: 1.75rem;
  font-size: var(--td-font-sm);
}

.td-icon-btn--md {
  width: 2.25rem;
  height: 2.25rem;
  font-size: var(--td-font-base);
}

.td-icon-btn--lg {
  width: 2.75rem;
  height: 2.75rem;
  font-size: var(--td-font-lg);
}

/* ── Variants ── */
.td-icon-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-icon-btn--primary:hover:not(:disabled) {
  background: var(--td-color-primary-hover);
}

.td-icon-btn--secondary {
  background: var(--td-surface-container-high);
  color: var(--td-text-primary);
  border-color: var(--td-border-default);
}

.td-icon-btn--secondary:hover:not(:disabled) {
  background: var(--td-surface-bright);
}

.td-icon-btn--ghost {
  color: var(--td-text-secondary);
}

.td-icon-btn--ghost:hover:not(:disabled) {
  background: var(--td-surface-container-high);
  color: var(--td-text-primary);
}

.td-icon-btn--danger {
  color: var(--td-color-error);
}

.td-icon-btn--danger:hover:not(:disabled) {
  background: var(--td-color-error-light);
}

/* ── Focus ── */
.td-icon-btn:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-icon-btn--danger:focus-visible {
  box-shadow: var(--td-focus-ring-error);
}

/* ── Disabled ── */
.td-icon-btn:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

/* ── Loading ── */
.td-icon-btn--loading {
  cursor: wait;
}

.td-icon-btn__spinner {
  width: 1em;
  height: 1em;
  border: 2px solid currentColor;
  border-right-color: transparent;
  border-radius: 50%;
  animation: td-icon-spin 0.6s linear infinite;
}

.td-icon-btn__icon {
  display: flex;
  align-items: center;
  justify-content: center;
}

@keyframes td-icon-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
