<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    modelValue?: string
    placeholder?: string
    disabled?: boolean
    readonly?: boolean
    error?: boolean
    rows?: number
    id?: string
  }>(),
  {
    modelValue: '',
    placeholder: '',
    disabled: false,
    readonly: false,
    error: false,
    rows: 3,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  blur: [event: FocusEvent]
  focus: [event: FocusEvent]
}>()

function handleInput(event: Event) {
  const target = event.target as HTMLTextAreaElement
  emit('update:modelValue', target.value)
}
</script>

<template>
  <textarea
    :id="props.id"
    :value="props.modelValue"
    :placeholder="props.placeholder"
    :disabled="props.disabled"
    :readonly="props.readonly"
    :rows="props.rows"
    :class="['td-textarea', { 'td-textarea--error': props.error }]"
    :aria-invalid="props.error || undefined"
    @input="handleInput"
    @blur="emit('blur', $event)"
    @focus="emit('focus', $event)"
  />
</template>

<style scoped>
.td-textarea {
  display: block;
  width: 100%;
  font-family: inherit;
  font-size: var(--td-font-base);
  line-height: 1.5;
  color: var(--td-text-primary);
  background: var(--td-surface-container-low);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-2) var(--td-space-3);
  resize: vertical;
  transition: border-color var(--td-transition-fast), box-shadow var(--td-transition-fast);
}

.td-textarea::placeholder {
  color: var(--td-text-tertiary);
}

.td-textarea:hover:not(:disabled):not(:focus) {
  border-color: var(--td-border-focus);
}

.td-textarea:focus {
  outline: none;
  border-color: var(--td-border-focus);
  box-shadow: var(--td-focus-ring);
}

.td-textarea--error {
  border-color: var(--td-color-error);
}

.td-textarea--error:focus {
  box-shadow: var(--td-focus-ring-error);
}

.td-textarea:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.td-textarea:read-only {
  background: var(--td-surface-container);
}
</style>
