<script setup lang="ts">
defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    modelValue?: string
  }>(),
  {
    modelValue: '',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

type DatePickerInput = HTMLInputElement & {
  showPicker?: () => void
}

function handleInput(event: Event) {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}

function openPicker(event: MouseEvent) {
  const input = event.currentTarget as DatePickerInput
  if (input.disabled || typeof input.showPicker !== 'function') return

  try {
    input.showPicker()
  } catch {
    // Browsers require showPicker() to remain inside a user gesture. If that
    // gesture is rejected, the native date field remains fully usable.
  }
}
</script>

<template>
  <input
    v-bind="$attrs"
    type="date"
    :value="props.modelValue"
    @input="handleInput"
    @click="openPicker"
  />
</template>
