<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    label?: string
    error?: string
    hint?: string
    fieldId?: string
    required?: boolean
  }>(),
  {
    label: '',
    error: '',
    hint: '',
    fieldId: '',
    required: false,
  },
)
</script>

<template>
  <div class="td-field">
    <label v-if="props.label" :for="props.fieldId || undefined" class="td-field__label">
      {{ props.label }}
      <span v-if="props.required" class="td-field__required" aria-hidden="true">*</span>
    </label>
    <div class="td-field__control">
      <slot />
    </div>
    <p v-if="props.hint && !props.error" class="td-field__hint">
      {{ props.hint }}
    </p>
    <p v-if="props.error" class="td-field__error" role="alert">
      {{ props.error }}
    </p>
  </div>
</template>

<style scoped>
.td-field {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-field__label {
  font-size: var(--td-font-sm);
  font-weight: 500;
  color: var(--td-text-primary);
}

.td-field__required {
  color: var(--td-color-error);
  margin-left: 2px;
}

.td-field__control {
  display: flex;
  flex-direction: column;
}

.td-field__hint {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  margin: 0;
}

.td-field__error {
  font-size: var(--td-font-xs);
  color: var(--td-color-error);
  margin: 0;
}
</style>
