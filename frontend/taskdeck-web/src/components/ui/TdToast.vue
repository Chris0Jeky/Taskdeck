<script setup lang="ts">
export type ToastVariant = 'info' | 'success' | 'warning' | 'error'

const props = withDefaults(
  defineProps<{
    variant?: ToastVariant
    message: string
    dismissible?: boolean
  }>(),
  {
    variant: 'info',
    dismissible: true,
  },
)

const emit = defineEmits<{
  dismiss: []
}>()
</script>

<template>
  <div :class="['td-toast', `td-toast--${props.variant}`]" role="status" aria-live="polite">
    <span class="td-toast__message">{{ props.message }}</span>
    <button
      v-if="props.dismissible"
      class="td-toast__dismiss"
      aria-label="Dismiss"
      @click="emit('dismiss')"
    >
      <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
        <path d="M3.5 3.5L10.5 10.5M10.5 3.5L3.5 10.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      </svg>
    </button>
  </div>
</template>

<style scoped>
.td-toast {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  padding: var(--td-space-2) var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 1px solid;
  font-size: var(--td-font-sm);
  box-shadow: var(--td-shadow-sm);
  min-width: 16rem;
  max-width: 28rem;
}

.td-toast--info {
  background: var(--td-color-info-light);
  color: var(--td-color-info);
  border-color: var(--td-color-info);
}

.td-toast--success {
  background: var(--td-color-success-light);
  color: var(--td-color-success);
  border-color: var(--td-color-success);
}

.td-toast--warning {
  background: var(--td-color-warning-light);
  color: var(--td-color-warning);
  border-color: var(--td-color-warning);
}

.td-toast--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
  border-color: var(--td-color-error);
}

.td-toast__message {
  flex: 1;
  line-height: 1.4;
}

.td-toast__dismiss {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: none;
  color: inherit;
  cursor: pointer;
  padding: 2px;
  border-radius: var(--td-radius-sm);
  opacity: 0.7;
  transition: opacity var(--td-transition-fast);
}

.td-toast__dismiss:hover {
  opacity: 1;
}

.td-toast__dismiss:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}
</style>
