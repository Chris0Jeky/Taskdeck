<script setup lang="ts">
import { computed, onUnmounted, reactive, watch } from 'vue'
import PaperTagstamp from './PaperTagstamp.vue'
import type { PaperTagstampTone } from './PaperTagstamp.vue'
import { useToastStore, type Toast } from '../../store/toastStore'

/**
 * PaperToastContainer — bottom-right paper toast stack.  Mirrors
 * `ToastSurface` in `design_handoff_taskdeck_paper/paper/surface-misc.jsx`.
 *
 * Renders the same toasts that the existing toast store produces — App.vue
 * picks between this and the legacy `ToastContainer.vue` based on
 * `paperThemeStore.isOn`.  Each card is a hairline paper-card with three
 * regions:
 *
 *   ┌─────────────┬────────────────────────────────┬────────────────────┐
 *   │  tagstamp   │  TITLE · message body          │  action · countdown│
 *   └─────────────┴────────────────────────────────┴────────────────────┘
 *
 * Behaviour notes:
 *   - The countdown is computed locally from `toast.duration`.
 *   - Hovering/focus pauses both the visual countdown and store removal timer.
 *   - The "undo"/action link emits `action(toast.id)` and runs the toast's
 *     `action.handler` if one was provided through the store options bag.
 */

const toastStore = useToastStore()

type Tone = 'applied' | 'proposed' | 'captured' | 'overdue' | 'undo'

type ToastDescriptor = {
  tone: Tone
  tagstamp: PaperTagstampTone
  glyph: string
  label: string
}

/** Map the existing toast `type` field to the Paper tone palette. */
function describe(toast: Toast): ToastDescriptor {
  switch (toast.type) {
    case 'success':
      return { tone: 'applied', tagstamp: 'applied', glyph: '✓', label: 'Applied' }
    case 'error':
      return { tone: 'overdue', tagstamp: 'overdue', glyph: '‼', label: 'Overdue' }
    case 'warning':
      return { tone: 'proposed', tagstamp: 'ember', glyph: '◆', label: 'Proposed' }
    case 'info':
    default:
      return { tone: 'captured', tagstamp: 'mute', glyph: '✎', label: 'Captured' }
  }
}

// ── Countdown ─────────────────────────────────────────────────────────────
//
// The store owns removal semantics; this local state mirrors the same pause
// and resume lifecycle so the visible countdown remains aligned.
//
// `state[id]` carries: { remaining, paused, deadline }
type CountdownState = {
  remaining: number
  paused: boolean
  deadline: number
  hover: boolean
  focusWithin: boolean
}
const state = reactive<Record<string, CountdownState>>({})
let intervalHandle: ReturnType<typeof setInterval> | null = null

function ensureCountdown(toast: Toast) {
  if (state[toast.id]) return
  state[toast.id] = {
    remaining: toast.duration,
    paused: false,
    deadline: Date.now() + toast.duration,
    hover: false,
    focusWithin: false,
  }
}

function tick() {
  const now = Date.now()
  for (const toast of toastStore.toasts) {
    const c = state[toast.id]
    if (!c || c.paused) continue
    c.remaining = Math.max(0, c.deadline - now)
  }
}

function ensureInterval() {
  if (intervalHandle !== null) return
  intervalHandle = setInterval(tick, 100)
}

function clearInterval_() {
  if (intervalHandle !== null) {
    clearInterval(intervalHandle)
    intervalHandle = null
  }
}

watch(
  () => toastStore.toasts.length,
  (count) => {
    if (count > 0) ensureInterval()
    else clearInterval_()
  },
  { immediate: true },
)

watch(
  () => toastStore.toasts.map((t) => t.id),
  (ids, prev) => {
    for (const toast of toastStore.toasts) {
      ensureCountdown(toast)
    }
    if (prev) {
      for (const oldId of prev) {
        if (!ids.includes(oldId)) delete state[oldId]
      }
    }
  },
  { immediate: true, deep: true },
)

onUnmounted(() => {
  clearInterval_()
})

// ── Hover pause / resume ──────────────────────────────────────────────────

function pauseTimer(id: string) {
  const c = state[id]
  if (!c || c.paused) return
  toastStore.pause(id)
  c.paused = true
  c.remaining = Math.max(0, c.deadline - Date.now())
}

function resumeTimer(id: string) {
  const c = state[id]
  if (!c || !c.paused) return
  toastStore.resume(id)
  c.paused = false
  c.deadline = Date.now() + c.remaining
}

function syncPauseState(id: string) {
  const c = state[id]
  if (!c) return

  if (c.hover || c.focusWithin) {
    pauseTimer(id)
    return
  }

  resumeTimer(id)
}

function setHover(id: string, isHovering: boolean) {
  const c = state[id]
  if (!c) return
  c.hover = isHovering
  syncPauseState(id)
}

function setFocusWithin(id: string, event: FocusEvent, isFocused: boolean) {
  const c = state[id]
  if (!c) return

  if (!isFocused) {
    const nextTarget = event.relatedTarget
    if (nextTarget instanceof Node && (event.currentTarget as HTMLElement).contains(nextTarget)) {
      return
    }
  }

  c.focusWithin = isFocused
  syncPauseState(id)
}

// ── Action ────────────────────────────────────────────────────────────────

const emit = defineEmits<{
  action: [toastId: string]
}>()

function handleAction(toast: Toast) {
  toast.action?.handler()
  emit('action', toast.id)
  toastStore.remove(toast.id)
}

// ── Display helpers ───────────────────────────────────────────────────────

function countdownLabel(toast: Toast): string {
  const c = state[toast.id]
  if (!c) return ''
  const seconds = Math.ceil(c.remaining / 1000)
  return `${seconds}s`
}

function progress(toast: Toast): number {
  const c = state[toast.id]
  if (!c || toast.duration <= 0) return 0
  return Math.max(0, Math.min(1, c.remaining / toast.duration))
}

// Expose the visible toasts as a stable computed for the template — the store
// keeps newest at the end; we want newest on top of the visual stack.
const visibleToasts = computed(() => [...toastStore.toasts].reverse())
</script>

<template>
  <div
    class="paper-toast-stack"
    aria-live="polite"
    aria-atomic="false"
    role="status"
    data-paper-toast-stack
  >
    <TransitionGroup name="paper-toast">
      <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- hover/focus pause is a UX affordance; the toast itself is purely informational and the action (when present) is on a real <button> -->
      <article
        v-for="toast in visibleToasts"
        :key="toast.id"
        :data-toast-id="toast.id"
        :data-tone="describe(toast).tone"
        :class="[
          'paper-toast',
          'card-lift',
          `paper-toast--${describe(toast).tone}`,
        ]"
        :role="toast.type === 'error' ? 'alert' : undefined"
        @mouseenter="setHover(toast.id, true)"
        @mouseleave="setHover(toast.id, false)"
        @focusin="setFocusWithin(toast.id, $event, true)"
        @focusout="setFocusWithin(toast.id, $event, false)"
      >
        <div class="paper-toast__glyph" aria-hidden="true">
          {{ describe(toast).glyph }}
        </div>
        <div class="paper-toast__body">
          <div class="paper-toast__head">
            <PaperTagstamp :tone="describe(toast).tagstamp">
              {{ describe(toast).label }}
            </PaperTagstamp>
            <span v-if="toast.title" class="paper-toast__title">{{ toast.title }}</span>
          </div>
          <p class="paper-toast__msg">{{ toast.message }}</p>
        </div>
        <div class="paper-toast__action">
          <button
            v-if="toast.action"
            type="button"
            class="paper-toast__undo"
            @click="handleAction(toast)"
          >
            <span class="paper-toast__undo-label">{{ toast.action.label }}</span>
            <span v-if="toast.action.hint" class="paper-toast__undo-hint">{{ toast.action.hint }}</span>
          </button>
          <span v-else class="paper-toast__countdown" aria-hidden="true">
            {{ countdownLabel(toast) }}
          </span>
          <span
            v-if="!toast.action"
            class="paper-toast__bar"
            aria-hidden="true"
            :style="{ '--p': progress(toast) }"
          />
        </div>
      </article>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.paper-toast-stack {
  position: fixed;
  right: 24px;
  bottom: 24px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  z-index: 70;
  pointer-events: none;
}

.paper-toast {
  pointer-events: auto;
  width: 320px;
  height: 56px;
  display: grid;
  grid-template-columns: 44px 1fr auto;
  align-items: stretch;
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: 4px;
  font-family: var(--sans);
  color: var(--ink);
  overflow: hidden;
}

.paper-toast--proposed {
  background: var(--ember-tint);
  border-color: var(--ember);
}

.paper-toast--overdue {
  background: var(--overdue-tint);
  border-color: var(--overdue);
}

.paper-toast__glyph {
  display: grid;
  place-items: center;
  border-right: 1px solid var(--line-soft);
  font-family: var(--serif);
  font-style: italic;
  font-size: 18px;
  color: var(--ink-deep);
}

.paper-toast--applied .paper-toast__glyph { color: var(--applied); }
.paper-toast--proposed .paper-toast__glyph { color: var(--ember); }
.paper-toast--overdue .paper-toast__glyph { color: var(--overdue); }
.paper-toast--undo .paper-toast__glyph { color: var(--mute); }

.paper-toast__body {
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  min-width: 0;
}

.paper-toast__head {
  display: flex;
  align-items: baseline;
  gap: 8px;
}

.paper-toast__title {
  font-family: var(--serif);
  font-size: 13.5px;
  font-weight: 500;
  color: var(--ink-deep);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.paper-toast__msg {
  margin: 2px 0 0;
  font-size: 12.5px;
  color: var(--ink-2);
  line-height: 1.4;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.paper-toast__action {
  display: flex;
  align-items: center;
  justify-content: center;
  border-left: 1px solid var(--line-soft);
  padding: 0 14px;
  position: relative;
}

.paper-toast__undo {
  background: transparent;
  border: none;
  padding: 4px 0;
  font-family: var(--mono);
  font-size: 10.5px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--ember);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border-bottom: 1px solid currentColor;
}

.paper-toast__undo-hint {
  color: var(--mute);
  border-bottom: none;
}

.paper-toast__countdown {
  font-family: var(--mono);
  font-size: 10.5px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--mute);
}

.paper-toast__bar {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: 2px;
  background: var(--ember);
  transform-origin: left center;
  transform: scaleX(var(--p, 1));
  transition: transform 100ms linear;
}

/* TransitionGroup names */
.paper-toast-enter-active,
.paper-toast-leave-active {
  transition: opacity 220ms ease, transform 220ms ease;
}
.paper-toast-enter-from {
  opacity: 0;
  transform: translateY(8px);
}
.paper-toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
.paper-toast-move {
  transition: transform 220ms ease;
}
</style>
