<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    size?: 'sm' | 'md' | 'lg'
    label?: string
  }>(),
  {
    size: 'md',
    label: 'Loading',
  },
)
</script>

<template>
  <div :class="['td-spinner', `td-spinner--${props.size}`]" role="status">
    <svg
      class="td-spinner__svg"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <circle
        class="td-spinner__track"
        cx="12"
        cy="12"
        r="10"
        stroke="currentColor"
        stroke-width="3"
      />
      <path
        class="td-spinner__arc"
        d="M12 2a10 10 0 019.8 8"
        stroke="currentColor"
        stroke-width="3"
        stroke-linecap="round"
      />
    </svg>
    <span class="td-spinner__label">{{ props.label }}</span>
  </div>
</template>

<style scoped>
.td-spinner {
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-2);
  color: var(--td-text-secondary);
}

.td-spinner__svg {
  animation: td-spinner-rotate 0.8s linear infinite;
}

.td-spinner--sm .td-spinner__svg {
  width: 1rem;
  height: 1rem;
}

.td-spinner--md .td-spinner__svg {
  width: 1.5rem;
  height: 1.5rem;
}

.td-spinner--lg .td-spinner__svg {
  width: 2rem;
  height: 2rem;
}

.td-spinner__track {
  opacity: 0.2;
}

.td-spinner__arc {
  opacity: 1;
}

.td-spinner__label {
  font-size: var(--td-font-sm);
}

.td-spinner--sm .td-spinner__label {
  font-size: var(--td-font-xs);
}

.td-spinner--lg .td-spinner__label {
  font-size: var(--td-font-base);
}

@keyframes td-spinner-rotate {
  to {
    transform: rotate(360deg);
  }
}
</style>
