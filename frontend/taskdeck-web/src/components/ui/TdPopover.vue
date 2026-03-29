<script setup lang="ts">
import { ref, watch, nextTick, onUnmounted } from 'vue'
import { registerEscapeHandler } from '../../composables/useEscapeStack'

const props = withDefaults(
  defineProps<{
    open: boolean
    align?: 'left' | 'right' | 'center'
    position?: 'top' | 'bottom'
  }>(),
  {
    align: 'left',
    position: 'bottom',
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

  const container = panelRef.value.closest('.td-popover')
  if (container && container.contains(event.target as Node)) {
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
      panelRef.value?.focus()
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
  previouslyFocusedElement?.focus()
  previouslyFocusedElement = null
})
</script>

<template>
  <div class="td-popover">
    <div class="td-popover__trigger">
      <slot name="trigger" />
    </div>

    <Transition name="td-popover">
      <div
        v-if="props.open"
        ref="panelRef"
        :class="[
          'td-popover__panel',
          `td-popover__panel--${props.align}`,
          `td-popover__panel--${props.position}`,
        ]"
        tabindex="-1"
      >
        <slot />
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.td-popover {
  position: relative;
  display: inline-block;
}

.td-popover__trigger {
  display: inline-flex;
}

.td-popover__panel {
  position: absolute;
  z-index: 50;
  min-width: 12rem;
  background: var(--td-surface-container-high);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  box-shadow: var(--td-shadow-md);
  padding: var(--td-space-3);
}

.td-popover__panel:focus {
  outline: none;
}

/* ── Vertical Position ── */
.td-popover__panel--bottom {
  top: calc(100% + var(--td-space-1));
}

.td-popover__panel--top {
  bottom: calc(100% + var(--td-space-1));
}

/* ── Horizontal Align ── */
.td-popover__panel--left {
  left: 0;
}

.td-popover__panel--right {
  right: 0;
}

.td-popover__panel--center {
  left: 50%;
  transform: translateX(-50%);
}

/* ── Transition ── */
.td-popover-enter-active,
.td-popover-leave-active {
  transition: opacity var(--td-transition-fast), transform var(--td-transition-fast);
}

.td-popover-enter-from,
.td-popover-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}

.td-popover__panel--top.td-popover-enter-from,
.td-popover__panel--top.td-popover-leave-to {
  transform: translateY(4px);
}
</style>
