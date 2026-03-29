<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    width?: string
    height?: string
    rounded?: boolean
    circle?: boolean
  }>(),
  {
    width: '100%',
    height: '1rem',
    rounded: true,
    circle: false,
  },
)
</script>

<template>
  <div
    class="td-skeleton"
    :class="{
      'td-skeleton--rounded': props.rounded && !props.circle,
      'td-skeleton--circle': props.circle,
    }"
    :style="{ width: props.width, height: props.height }"
    aria-hidden="true"
  />
</template>

<style scoped>
.td-skeleton {
  background: var(--td-surface-container-high);
  animation: td-skeleton-pulse 1.5s ease-in-out infinite;
}

.td-skeleton--rounded {
  border-radius: var(--td-radius-md);
}

.td-skeleton--circle {
  border-radius: 50%;
}

@keyframes td-skeleton-pulse {
  0%, 100% {
    opacity: 1;
  }
  50% {
    opacity: 0.4;
  }
}
</style>
