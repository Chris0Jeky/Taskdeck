<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    modelValue?: string
    disabled?: boolean
    error?: boolean
    id?: string
    placeholder?: string
  }>(),
  {
    modelValue: '',
    disabled: false,
    error: false,
    placeholder: '',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  blur: [event: FocusEvent]
  focus: [event: FocusEvent]
}>()

function handleChange(event: Event) {
  const target = event.target as HTMLSelectElement
  emit('update:modelValue', target.value)
}
</script>

<template>
  <div :class="['td-select-wrapper', { 'td-select-wrapper--error': props.error }]">
    <select
      :id="props.id"
      :value="props.modelValue"
      :disabled="props.disabled"
      :class="['td-select', { 'td-select--error': props.error, 'td-select--placeholder': !props.modelValue && props.placeholder }]"
      :aria-invalid="props.error || undefined"
      @change="handleChange"
      @blur="emit('blur', $event)"
      @focus="emit('focus', $event)"
    >
      <option v-if="props.placeholder" value="" disabled>{{ props.placeholder }}</option>
      <slot />
    </select>
    <span class="td-select-chevron" aria-hidden="true">
      <svg width="12" height="12" viewBox="0 0 12 12" fill="none">
        <path d="M3 4.5L6 7.5L9 4.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
      </svg>
    </span>
  </div>
</template>

<style scoped>
.td-select-wrapper {
  position: relative;
  display: block;
  width: 100%;
}

.td-select {
  display: block;
  width: 100%;
  font-family: inherit;
  font-size: var(--td-font-base);
  line-height: 1.5;
  color: var(--td-text-primary);
  background: var(--td-surface-container-low);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-2) var(--td-space-8) var(--td-space-2) var(--td-space-3);
  appearance: none;
  cursor: pointer;
  transition: border-color var(--td-transition-fast), box-shadow var(--td-transition-fast);
}

.td-select--placeholder {
  color: var(--td-text-tertiary);
}

.td-select:hover:not(:disabled):not(:focus) {
  border-color: var(--td-border-focus);
}

.td-select:focus {
  outline: none;
  border-color: var(--td-border-focus);
  box-shadow: var(--td-focus-ring);
}

.td-select--error {
  border-color: var(--td-color-error);
}

.td-select--error:focus {
  box-shadow: var(--td-focus-ring-error);
}

.td-select:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.td-select-chevron {
  position: absolute;
  right: var(--td-space-3);
  top: 50%;
  transform: translateY(-50%);
  color: var(--td-text-tertiary);
  pointer-events: none;
  display: flex;
  align-items: center;
}
</style>
