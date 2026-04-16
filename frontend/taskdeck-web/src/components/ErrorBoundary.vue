<script setup lang="ts">
/**
 * ErrorBoundary catches render and lifecycle errors in its descendant tree
 * via Vue's `onErrorCaptured` hook and renders a fallback UI in place of the
 * crashed subtree. It is deliberately styled with plain inline CSS so that
 * it cannot itself crash when design tokens or stylesheets are unavailable.
 *
 * Scope caveat: Vue's `errorCaptured` does NOT catch async promise rejections
 * that originate outside a render/lifecycle call stack. Those are handled
 * globally via `app.config.errorHandler` and `window` listeners installed in
 * `main.ts`.
 */
import { onErrorCaptured, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const props = withDefaults(
  defineProps<{
    /** Reset fallback state automatically when the route changes. */
    resetOnRouteChange?: boolean
  }>(),
  {
    resetOnRouteChange: true,
  },
)

const emit = defineEmits<{
  error: [error: unknown, info: string]
  reset: []
}>()

const crashedError = ref<unknown>(null)
const crashInfo = ref<string>('')

// useRoute/useRouter can be undefined in isolated unit tests where no router
// is installed. Guard against that so the boundary can still be mounted in
// minimal test harnesses.
const route = (() => {
  try {
    return useRoute()
  } catch {
    return null
  }
})()
const router = (() => {
  try {
    return useRouter()
  } catch {
    return null
  }
})()

function formatError(err: unknown): string {
  if (err instanceof Error) {
    return err.message || err.name || 'Unknown error'
  }
  if (typeof err === 'string') return err
  try {
    return JSON.stringify(err)
  } catch {
    return 'Unknown error'
  }
}

function getStack(err: unknown): string | null {
  if (err instanceof Error && typeof err.stack === 'string') {
    return err.stack
  }
  return null
}

function reset() {
  crashedError.value = null
  crashInfo.value = ''
  emit('reset')
}

function reload() {
  if (typeof window !== 'undefined') {
    window.location.reload()
  }
}

function goHome() {
  if (router) {
    void router.push('/')
    reset()
    return
  }
  if (typeof window !== 'undefined') {
    window.location.assign('/')
  }
}

onErrorCaptured((err, _instance, info) => {
  crashedError.value = err
  crashInfo.value = info

  // Always log so errors are not silently swallowed.
  console.error('[ErrorBoundary] caught error', err, info)

  // Forward to Sentry if the host page has installed it (no hard dependency).
  const sentry = (globalThis as unknown as { Sentry?: { captureException?: (e: unknown) => void } }).Sentry
  if (sentry && typeof sentry.captureException === 'function') {
    try {
      sentry.captureException(err)
    } catch {
      // Never let reporting failures bubble out of the boundary.
    }
  }

  emit('error', err, info)

  // Stop propagation so ancestors do not unmount the whole app.
  return false
})

// Reset the boundary automatically when the route changes so a crash on one
// view does not permanently lock the user out of others.
if (route) {
  watch(
    () => route.fullPath,
    (next, prev) => {
      if (props.resetOnRouteChange && crashedError.value !== null && next !== prev) {
        reset()
      }
    },
  )
}

// Expose for tests / parent components.
defineExpose({ reset })

const isDev = (() => {
  try {
    return Boolean(import.meta.env?.DEV)
  } catch {
    return false
  }
})()
</script>

<template>
  <slot v-if="crashedError === null" />
  <div
    v-else
    class="td-error-boundary"
    role="alert"
    aria-live="assertive"
    data-testid="error-boundary-fallback"
  >
    <div class="td-error-boundary__card">
      <h2 class="td-error-boundary__title">Something went wrong</h2>
      <p class="td-error-boundary__message">
        A part of the app crashed unexpectedly. Your session is still active — you can
        reload the page or return home to continue working.
      </p>
      <div class="td-error-boundary__actions">
        <button
          type="button"
          class="td-error-boundary__btn td-error-boundary__btn--primary"
          @click="reload"
        >
          Reload page
        </button>
        <button
          type="button"
          class="td-error-boundary__btn td-error-boundary__btn--secondary"
          @click="goHome"
        >
          Go to home
        </button>
        <button
          type="button"
          class="td-error-boundary__btn td-error-boundary__btn--ghost"
          @click="reset"
        >
          Dismiss
        </button>
      </div>
      <details v-if="isDev" class="td-error-boundary__details">
        <summary>Error details (dev only)</summary>
        <p class="td-error-boundary__info">{{ crashInfo }}</p>
        <pre class="td-error-boundary__stack">{{ formatError(crashedError) }}</pre>
        <pre v-if="getStack(crashedError)" class="td-error-boundary__stack">{{ getStack(crashedError) }}</pre>
      </details>
    </div>
  </div>
</template>

<style scoped>
/*
 * Inline, dependency-free styling. The boundary must render correctly even
 * when the global stylesheet or design tokens are unavailable (for example
 * if the stylesheet itself is the source of the crash). Avoid var(--td-*).
 */
.td-error-boundary {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 40vh;
  padding: 24px;
  box-sizing: border-box;
}

.td-error-boundary__card {
  max-width: 36rem;
  width: 100%;
  padding: 24px;
  border: 1px solid #f1b0b7;
  background: #fff5f5;
  color: #6e1f2a;
  border-radius: 8px;
  font-family: system-ui, -apple-system, 'Segoe UI', sans-serif;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
}

.td-error-boundary__title {
  margin: 0 0 8px;
  font-size: 1.125rem;
  font-weight: 700;
}

.td-error-boundary__message {
  margin: 0 0 16px;
  font-size: 0.95rem;
  line-height: 1.5;
  color: #4a1720;
}

.td-error-boundary__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.td-error-boundary__btn {
  appearance: none;
  border: 1px solid transparent;
  padding: 8px 14px;
  font-size: 0.9rem;
  font-weight: 600;
  border-radius: 6px;
  cursor: pointer;
  font-family: inherit;
}

.td-error-boundary__btn--primary {
  background: #b02a37;
  color: #fff;
}

.td-error-boundary__btn--primary:hover {
  background: #951f2c;
}

.td-error-boundary__btn--secondary {
  background: #fff;
  color: #6e1f2a;
  border-color: #d9a1a7;
}

.td-error-boundary__btn--secondary:hover {
  background: #fbe7e9;
}

.td-error-boundary__btn--ghost {
  background: transparent;
  color: #6e1f2a;
}

.td-error-boundary__btn--ghost:hover {
  background: #fbe7e9;
}

.td-error-boundary__details {
  margin-top: 16px;
  font-size: 0.85rem;
}

.td-error-boundary__info {
  margin: 8px 0;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}

.td-error-boundary__stack {
  white-space: pre-wrap;
  word-break: break-word;
  background: #fde2e5;
  padding: 8px;
  border-radius: 4px;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.8rem;
  max-height: 240px;
  overflow: auto;
}
</style>
