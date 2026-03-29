<script setup lang="ts">
export type AlertVariant = 'info' | 'success' | 'warning' | 'error'

const props = withDefaults(
  defineProps<{
    variant?: AlertVariant
    dismissible?: boolean
  }>(),
  {
    variant: 'info',
    dismissible: false,
  },
)

const emit = defineEmits<{
  dismiss: []
}>()
</script>

<template>
  <div :class="['td-inline-alert', `td-inline-alert--${props.variant}`]" role="alert">
    <div class="td-inline-alert__content">
      <slot />
    </div>
    <button
      v-if="props.dismissible"
      class="td-inline-alert__dismiss"
      aria-label="Dismiss alert"
      @click="emit('dismiss')"
    >
      <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
        <path d="M3.5 3.5L10.5 10.5M10.5 3.5L3.5 10.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      </svg>
    </button>
  </div>
</template>

<style scoped>
.td-inline-alert {
  display: flex;
  align-items: flex-start;
  gap: var(--td-space-2);
  padding: var(--td-space-2) var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 1px solid;
  font-size: var(--td-font-sm);
  line-height: 1.5;
}

.td-inline-alert--info {
  background: var(--td-color-info-light);
  color: var(--td-color-info);
  border-color: var(--td-color-info);
}

.td-inline-alert--success {
  background: var(--td-color-success-light);
  color: var(--td-color-success);
  border-color: var(--td-color-success);
}

.td-inline-alert--warning {
  background: var(--td-color-warning-light);
  color: var(--td-color-warning);
  border-color: var(--td-color-warning);
}

.td-inline-alert--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
  border-color: var(--td-color-error);
}

.td-inline-alert__content {
  flex: 1;
}

.td-inline-alert__dismiss {
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

.td-inline-alert__dismiss:hover {
  opacity: 1;
}

.td-inline-alert__dismiss:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}
</style>
