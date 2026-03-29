<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    modelValue?: string
    type?: 'text' | 'email' | 'password' | 'number' | 'search' | 'url' | 'tel'
    placeholder?: string
    disabled?: boolean
    readonly?: boolean
    error?: boolean
    id?: string
  }>(),
  {
    modelValue: '',
    type: 'text',
    placeholder: '',
    disabled: false,
    readonly: false,
    error: false,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  blur: [event: FocusEvent]
  focus: [event: FocusEvent]
}>()

function handleInput(event: Event) {
  const target = event.target as HTMLInputElement
  emit('update:modelValue', target.value)
}
</script>

<template>
  <input
    :id="props.id"
    :type="props.type"
    :value="props.modelValue"
    :placeholder="props.placeholder"
    :disabled="props.disabled"
    :readonly="props.readonly"
    :class="['td-input', { 'td-input--error': props.error }]"
    :aria-invalid="props.error || undefined"
    @input="handleInput"
    @blur="emit('blur', $event)"
    @focus="emit('focus', $event)"
  />
</template>

<style scoped>
.td-input {
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
  transition: border-color var(--td-transition-fast), box-shadow var(--td-transition-fast);
}

.td-input::placeholder {
  color: var(--td-text-tertiary);
}

.td-input:hover:not(:disabled):not(:focus) {
  border-color: var(--td-border-focus);
}

.td-input:focus {
  outline: none;
  border-color: var(--td-border-focus);
  box-shadow: var(--td-focus-ring);
}

.td-input--error {
  border-color: var(--td-color-error);
}

.td-input--error:focus {
  box-shadow: var(--td-focus-ring-error);
}

.td-input:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.td-input:read-only {
  background: var(--td-surface-container);
}
</style>
