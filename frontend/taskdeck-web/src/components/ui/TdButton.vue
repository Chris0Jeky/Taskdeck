<script setup lang="ts">
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type ButtonSize = 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    variant?: ButtonVariant
    size?: ButtonSize
    disabled?: boolean
    loading?: boolean
    type?: 'button' | 'submit' | 'reset'
  }>(),
  {
    variant: 'primary',
    size: 'md',
    disabled: false,
    loading: false,
    type: 'button',
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
    :type="props.type"
    :disabled="props.disabled || props.loading"
    :class="[
      'td-btn',
      `td-btn--${props.variant}`,
      `td-btn--${props.size}`,
      { 'td-btn--loading': props.loading },
    ]"
    :aria-disabled="props.disabled || props.loading"
    :aria-busy="props.loading"
    @click="handleClick"
  >
    <span v-if="props.loading" class="td-btn__spinner" aria-hidden="true" />
    <span :class="{ 'td-btn__content--hidden': props.loading }">
      <slot />
    </span>
  </button>
</template>

<style scoped>
.td-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--td-space-2);
  border-radius: var(--td-radius-md);
  border: 1px solid transparent;
  font-family: inherit;
  font-weight: 500;
  cursor: pointer;
  transition: background var(--td-transition-fast), color var(--td-transition-fast),
    border-color var(--td-transition-fast), box-shadow var(--td-transition-fast);
  position: relative;
  white-space: nowrap;
  user-select: none;
}

/* ── Sizes ── */
.td-btn--sm {
  font-size: var(--td-font-sm);
  padding: var(--td-space-1) var(--td-space-2);
}

.td-btn--md {
  font-size: var(--td-font-base);
  padding: var(--td-space-2) var(--td-space-4);
}

.td-btn--lg {
  font-size: var(--td-font-lg);
  padding: var(--td-space-3) var(--td-space-5);
}

/* ── Variants ── */
.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--primary:hover:not(:disabled) {
  background: var(--td-color-primary-hover);
}

.td-btn--primary:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-btn--secondary {
  background: var(--td-surface-container-high);
  color: var(--td-text-primary);
  border-color: var(--td-border-default);
}

.td-btn--secondary:hover:not(:disabled) {
  background: var(--td-surface-bright);
}

.td-btn--secondary:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-btn--ghost {
  background: transparent;
  color: var(--td-text-secondary);
}

.td-btn--ghost:hover:not(:disabled) {
  background: var(--td-surface-container-high);
  color: var(--td-text-primary);
}

.td-btn--ghost:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-btn--danger {
  background: var(--td-color-error);
  color: var(--td-text-inverse);
}

.td-btn--danger:hover:not(:disabled) {
  background: var(--td-color-error);
  filter: brightness(0.9);
}

.td-btn--danger:focus-visible {
  box-shadow: var(--td-focus-ring-error);
  outline: none;
}

/* ── Disabled ── */
.td-btn:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

/* ── Loading ── */
.td-btn--loading {
  cursor: wait;
}

.td-btn__spinner {
  width: 1em;
  height: 1em;
  border: 2px solid currentColor;
  border-right-color: transparent;
  border-radius: 50%;
  animation: td-spin 0.6s linear infinite;
  position: absolute;
}

.td-btn__content--hidden {
  visibility: hidden;
}

@keyframes td-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
