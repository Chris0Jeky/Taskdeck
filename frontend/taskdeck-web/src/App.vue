<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import ToastContainer from './components/common/ToastContainer.vue'
import PaperToastContainer from './components/paper/PaperToastContainer.vue'
import SessionTimeoutWarning from './components/common/SessionTimeoutWarning.vue'
import ErrorBoundary from './components/ErrorBoundary.vue'
import { useSessionStore } from './store/sessionStore'
import { installStaleBundleRecovery, clearStaleBundleRecoveryMarker } from './pwa/staleBundleRecovery'
import { useFeatureFlagStore } from './store/featureFlagStore'
import { usePaperThemeStore } from './store/paperThemeStore'

const AppShell = defineAsyncComponent(() => import('./components/shell/AppShell.vue'))

const route = useRoute()
const session = useSessionStore()
const featureFlags = useFeatureFlagStore()
const paperTheme = usePaperThemeStore()
const sessionReady = ref(false)
const restoreIsSlow = ref(false)
const SLOW_RESTORE_NOTICE_MS = 5_000

paperTheme.apply()

const showShell = computed(() => {
  return route.meta.requiresShell === true
})

// The API-cache migration can activate a replacement worker under a running page, so
// a lazy route chunk from the previous build may no longer be fetchable.
const stopStaleBundleRecovery = installStaleBundleRecovery()
onUnmounted(stopStaleBundleRecovery)

onMounted(async () => {
  clearStaleBundleRecoveryMarker()
  const slowRestoreNotice = setTimeout(() => { restoreIsSlow.value = true }, SLOW_RESTORE_NOTICE_MS)
  try {
    await session.restoreSession()
  } finally {
    clearTimeout(slowRestoreNotice)
  }
  featureFlags.restore()
  sessionReady.value = true
})
</script>

<template>
  <div id="app">
    <a href="#td-main-content" class="td-skip-link">Skip to main content</a>
    <!-- Shell layout for workspace routes -->
    <ErrorBoundary v-if="sessionReady && showShell">
      <AppShell />
    </ErrorBoundary>
    <!-- Direct render for public routes (login/register) -->
    <ErrorBoundary v-else-if="sessionReady">
      <router-view />
    </ErrorBoundary>
    <!-- Session restoration enumerates and clears CacheStorage, which can be slow
         or, on a stalled implementation, never settle. Say so rather than showing
         an unexplained blank page with no way forward. -->
    <div v-else class="td-session-restoring" role="status" aria-live="polite">
      <p>Restoring your session…</p>
      <p v-if="restoreIsSlow" class="td-session-restoring__hint">
        This is taking longer than usual. Reload the page if it does not continue.
      </p>
    </div>
    <PaperToastContainer v-if="paperTheme.isOn" />
    <ToastContainer v-else />
    <SessionTimeoutWarning />
  </div>
</template>

<style scoped>
#app {
  min-height: 100vh;
}

.td-session-restoring {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  align-items: center;
  justify-content: center;
  min-height: 60vh;
  padding: var(--td-space-4);
  text-align: center;
}

.td-session-restoring__hint {
  font-size: 0.875rem;
  opacity: 0.75;
}

/* Skip-to-content link — visually hidden until focused */
.td-skip-link {
  position: absolute;
  top: -100%;
  left: var(--td-space-4);
  z-index: 100;
  padding: var(--td-space-2) var(--td-space-4);
  background: var(--td-color-ember);
  color: var(--td-text-inverse);
  border-radius: var(--td-radius-md);
  font-weight: 700;
  font-size: var(--td-font-sm);
  text-decoration: none;
  white-space: nowrap;
}

.td-skip-link:focus {
  top: var(--td-space-2);
  outline: 2px solid var(--td-color-ember);
  outline-offset: 2px;
}
</style>
