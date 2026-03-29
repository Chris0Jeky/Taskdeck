<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    title: string
    description?: string
  }>(),
  {
    description: '',
  },
)
</script>

<template>
  <div class="td-empty-state">
    <div v-if="$slots.icon" class="td-empty-state__icon">
      <slot name="icon" />
    </div>
    <h3 class="td-empty-state__title">{{ props.title }}</h3>
    <p v-if="props.description" class="td-empty-state__description">
      {{ props.description }}
    </p>
    <div v-if="$slots.action" class="td-empty-state__action">
      <slot name="action" />
    </div>
  </div>
</template>

<style scoped>
.td-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: var(--td-space-10) var(--td-space-5);
  gap: var(--td-space-3);
}

.td-empty-state__icon {
  color: var(--td-text-tertiary);
  font-size: 2.5rem;
  line-height: 1;
}

.td-empty-state__title {
  font-size: var(--td-font-lg);
  font-weight: 600;
  color: var(--td-text-primary);
  margin: 0;
}

.td-empty-state__description {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  margin: 0;
  max-width: 28rem;
}

.td-empty-state__action {
  margin-top: var(--td-space-2);
}
</style>
