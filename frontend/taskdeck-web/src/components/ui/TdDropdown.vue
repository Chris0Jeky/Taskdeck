<script setup lang="ts">
import { ref, watch, nextTick, onUnmounted } from 'vue'
import { registerEscapeHandler } from '../../composables/useEscapeStack'

const props = withDefaults(
  defineProps<{
    open: boolean
    align?: 'left' | 'right'
  }>(),
  {
    align: 'left',
  },
)

const emit = defineEmits<{
  close: []
}>()

const panelRef = ref<HTMLElement | null>(null)
let previouslyFocusedElement: HTMLElement | null = null
let unregisterEscape: (() => void) | null = null

function requestClose() {
  emit('close')
}

function handleClickOutside(event: MouseEvent) {
  if (!panelRef.value) {
    return
  }

  const target = event.target as Node
  // Check if the click is inside the dropdown container (includes trigger)
  const container = panelRef.value.closest('.td-dropdown')
  if (container && container.contains(target)) {
    return
  }

  requestClose()
}

watch(
  () => props.open,
  async (isOpen) => {
    if (isOpen) {
      previouslyFocusedElement = document.activeElement as HTMLElement | null
      unregisterEscape = registerEscapeHandler(requestClose)
      await nextTick()
      document.addEventListener('click', handleClickOutside, true)
      // Focus the first focusable element in the panel
      const firstFocusable = panelRef.value?.querySelector<HTMLElement>(
        'button:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])',
      )
      firstFocusable?.focus()
    } else {
      unregisterEscape?.()
      unregisterEscape = null
      document.removeEventListener('click', handleClickOutside, true)
      previouslyFocusedElement?.focus()
      previouslyFocusedElement = null
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  unregisterEscape?.()
  unregisterEscape = null
  document.removeEventListener('click', handleClickOutside, true)
})
</script>

<template>
  <div class="td-dropdown">
    <div class="td-dropdown__trigger">
      <slot name="trigger" />
    </div>

    <Transition name="td-dropdown">
      <div
        v-if="props.open"
        ref="panelRef"
        :class="['td-dropdown__panel', `td-dropdown__panel--${props.align}`]"
        role="menu"
      >
        <slot />
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.td-dropdown {
  position: relative;
  display: inline-block;
}

.td-dropdown__trigger {
  display: inline-flex;
}

.td-dropdown__panel {
  position: absolute;
  top: calc(100% + var(--td-space-1));
  z-index: 50;
  min-width: 10rem;
  background: var(--td-surface-container-high);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  box-shadow: var(--td-shadow-md);
  padding: var(--td-space-1) 0;
  display: flex;
  flex-direction: column;
}

.td-dropdown__panel--left {
  left: 0;
}

.td-dropdown__panel--right {
  right: 0;
}

/* ── Transition ── */
.td-dropdown-enter-active,
.td-dropdown-leave-active {
  transition: opacity var(--td-transition-fast), transform var(--td-transition-fast);
}

.td-dropdown-enter-from,
.td-dropdown-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
</style>
