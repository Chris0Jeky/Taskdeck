<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { filterInputAssistOptions, type InputAssistOption } from '../../utils/inputAssist'

const props = withDefaults(defineProps<{
  modelValue: string
  options: InputAssistOption[]
  placeholder?: string
  ariaLabel?: string
  noResultsText?: string
  disabled?: boolean
}>(), {
  placeholder: '',
  ariaLabel: 'Input assist field',
  noResultsText: 'No matching options.',
  disabled: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
  select: [option: InputAssistOption]
}>()

const componentId = `td-input-assist-${Math.random().toString(36).slice(2, 10)}`
const inputRef = ref<HTMLInputElement | null>(null)
const panelOpen = ref(false)
const activeIndex = ref(0)
let blurCloseTimer: ReturnType<typeof setTimeout> | null = null

const filteredOptions = computed(() => filterInputAssistOptions(props.options, props.modelValue))
const listboxId = `${componentId}-listbox`

const activeDescendant = computed(() => {
  if (!panelOpen.value || filteredOptions.value.length === 0) {
    return undefined
  }
  return `${componentId}-option-${activeIndex.value}`
})

watch(filteredOptions, (options) => {
  if (options.length === 0) {
    activeIndex.value = 0
    return
  }

  if (activeIndex.value >= options.length) {
    activeIndex.value = 0
  }
})

function openPanel() {
  if (props.disabled) {
    return
  }

  panelOpen.value = true
}

function closePanel() {
  panelOpen.value = false
  activeIndex.value = 0
}

function setModelValue(value: string) {
  emit('update:modelValue', value)
}

function findExactMatch(value: string): InputAssistOption | null {
  const normalizedInput = value.trim().toLowerCase()
  if (!normalizedInput) {
    return null
  }

  const byValue = props.options.find((option) => option.value.trim().toLowerCase() === normalizedInput)
  if (byValue) {
    return byValue
  }

  return props.options.find((option) => {
    return option.label.trim().toLowerCase() === normalizedInput
  })
  ?? null
}

function selectOption(option: InputAssistOption) {
  setModelValue(option.value)
  emit('select', option)
  closePanel()
  inputRef.value?.focus()
}

function selectActiveOption() {
  const option = filteredOptions.value[activeIndex.value]
  if (!option) {
    return
  }
  selectOption(option)
}

function onInput(event: Event) {
  const target = event.target as HTMLInputElement
  const exactMatch = findExactMatch(target.value)
  if (exactMatch) {
    selectOption(exactMatch)
    return
  }

  setModelValue(target.value)
  openPanel()
}

function onBlur() {
  blurCloseTimer = setTimeout(() => {
    closePanel()
    blurCloseTimer = null
  }, 120)
}

function onFocus() {
  if (blurCloseTimer) {
    clearTimeout(blurCloseTimer)
    blurCloseTimer = null
  }

  openPanel()
}

function onKeydown(event: KeyboardEvent) {
  if (props.disabled) {
    return
  }

  if (event.key === 'Escape') {
    if (panelOpen.value) {
      event.preventDefault()
      event.stopPropagation()
      closePanel()
    }
    return
  }

  if (event.key === 'ArrowDown') {
    event.preventDefault()
    openPanel()
    if (filteredOptions.value.length === 0) {
      return
    }
    activeIndex.value = (activeIndex.value + 1) % filteredOptions.value.length
    return
  }

  if (event.key === 'ArrowUp') {
    event.preventDefault()
    openPanel()
    if (filteredOptions.value.length === 0) {
      return
    }
    activeIndex.value = (activeIndex.value - 1 + filteredOptions.value.length) % filteredOptions.value.length
    return
  }

  if (event.key === 'Enter' && panelOpen.value && filteredOptions.value.length > 0) {
    event.preventDefault()
    selectActiveOption()
  }
}
</script>

<template>
  <div class="td-input-assist">
    <input
      ref="inputRef"
      :value="modelValue"
      type="text"
      class="td-input td-input-assist__input"
      :placeholder="placeholder"
      :aria-label="ariaLabel"
      role="combobox"
      aria-autocomplete="list"
      :aria-expanded="panelOpen"
      :aria-controls="listboxId"
      :aria-activedescendant="activeDescendant"
      :disabled="disabled"
      @focus="onFocus"
      @blur="onBlur"
      @input="onInput"
      @keydown="onKeydown"
    />

    <div
      v-if="panelOpen"
      :id="listboxId"
      class="td-input-assist__panel"
      role="listbox"
    >
      <button
        v-for="(option, index) in filteredOptions"
        :id="`${componentId}-option-${index}`"
        :key="option.value"
        type="button"
        class="td-input-assist__option"
        :class="{ 'td-input-assist__option--active': index === activeIndex }"
        role="option"
        :aria-selected="index === activeIndex"
        @mouseenter="activeIndex = index"
        @mousedown.prevent="selectOption(option)"
      >
        <span class="td-input-assist__option-label">{{ option.label }}</span>
        <span v-if="option.helperText" class="td-input-assist__option-helper">{{ option.helperText }}</span>
      </button>

      <div v-if="filteredOptions.length === 0" class="td-input-assist__empty">
        {{ noResultsText }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-input-assist {
  position: relative;
  width: 100%;
}

.td-input-assist__input {
  width: 100%;
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
}

.td-input-assist__input:focus {
  outline: none;
  border-color: var(--td-border-focus);
  box-shadow: var(--td-focus-ring);
}

.td-input-assist__panel {
  position: absolute;
  z-index: 20;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  max-height: 260px;
  overflow-y: auto;
  border: 1px solid var(--td-border-default);
  background: var(--td-surface-primary);
  border-radius: var(--td-radius-md);
  box-shadow: var(--td-shadow-md);
  padding: var(--td-space-1);
}

.td-input-assist__option {
  width: 100%;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  border: none;
  background: transparent;
  border-radius: var(--td-radius-sm);
  padding: var(--td-space-2);
  text-align: left;
  cursor: pointer;
}

.td-input-assist__option:hover,
.td-input-assist__option--active {
  background: var(--td-surface-tertiary);
}

.td-input-assist__option-label {
  font-size: var(--td-font-sm);
  color: var(--td-text-primary);
}

.td-input-assist__option-helper {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
}

.td-input-assist__empty {
  padding: var(--td-space-2) var(--td-space-3);
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
}
</style>
