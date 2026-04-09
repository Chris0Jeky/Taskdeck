<script setup lang="ts">
import { ref } from 'vue'

const props = withDefaults(
  defineProps<{
    text: string
    position?: 'top' | 'bottom' | 'left' | 'right'
    delay?: number
  }>(),
  {
    position: 'top',
    delay: 300,
  },
)

const visible = ref(false)
let showTimeout: ReturnType<typeof setTimeout> | null = null

function handleMouseEnter() {
  showTimeout = setTimeout(() => {
    visible.value = true
  }, props.delay)
}

function handleMouseLeave() {
  if (showTimeout) {
    clearTimeout(showTimeout)
    showTimeout = null
  }
  visible.value = false
}

function handleFocus() {
  visible.value = true
}

function handleBlur() {
  visible.value = false
}
</script>

<template>
  <div
    class="td-tooltip-wrapper"
    role="presentation"
    @mouseenter="handleMouseEnter"
    @mouseleave="handleMouseLeave"
    @focusin="handleFocus"
    @focusout="handleBlur"
  >
    <slot />
    <Transition name="td-tooltip">
      <div
        v-if="visible"
        :class="['td-tooltip', `td-tooltip--${props.position}`]"
        role="tooltip"
      >
        {{ props.text }}
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.td-tooltip-wrapper {
  position: relative;
  display: inline-flex;
}

.td-tooltip {
  position: absolute;
  z-index: 70;
  max-width: 15rem;
  padding: var(--td-space-1) var(--td-space-2);
  font-size: var(--td-font-xs);
  font-weight: 500;
  color: var(--td-text-primary);
  background: var(--td-surface-container-highest);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  box-shadow: var(--td-shadow-sm);
  white-space: nowrap;
  pointer-events: none;
}

/* ── Positions ── */
.td-tooltip--top {
  bottom: calc(100% + 6px);
  left: 50%;
  transform: translateX(-50%);
}

.td-tooltip--bottom {
  top: calc(100% + 6px);
  left: 50%;
  transform: translateX(-50%);
}

.td-tooltip--left {
  right: calc(100% + 6px);
  top: 50%;
  transform: translateY(-50%);
}

.td-tooltip--right {
  left: calc(100% + 6px);
  top: 50%;
  transform: translateY(-50%);
}

/* ── Transition ── */
.td-tooltip-enter-active,
.td-tooltip-leave-active {
  transition: opacity var(--td-transition-fast);
}

.td-tooltip-enter-from,
.td-tooltip-leave-to {
  opacity: 0;
}
</style>
