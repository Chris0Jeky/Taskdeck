<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import ToastContainer from './components/common/ToastContainer.vue'
import PaperToastContainer from './components/paper/PaperToastContainer.vue'
import SessionTimeoutWarning from './components/common/SessionTimeoutWarning.vue'
import ErrorBoundary from './components/ErrorBoundary.vue'
import { useSessionStore } from './store/sessionStore'
import { useFeatureFlagStore } from './store/featureFlagStore'
import { usePaperThemeStore } from './store/paperThemeStore'

const AppShell = defineAsyncComponent(() => import('./components/shell/AppShell.vue'))

const route = useRoute()
const session = useSessionStore()
const featureFlags = useFeatureFlagStore()
const paperTheme = usePaperThemeStore()

paperTheme.apply()

const showShell = computed(() => {
  return route.meta.requiresShell === true
})

onMounted(() => {
  session.restoreSession()
  featureFlags.restore()
})
</script>

<template>
  <div id="app">
    <a href="#td-main-content" class="td-skip-link">Skip to main content</a>
    <!-- Shell layout for workspace routes -->
    <ErrorBoundary v-if="showShell">
      <AppShell />
    </ErrorBoundary>
    <!-- Direct render for public routes (login/register) -->
    <ErrorBoundary v-else>
      <router-view />
    </ErrorBoundary>
    <PaperToastContainer v-if="paperTheme.isOn" />
    <ToastContainer v-else />
    <SessionTimeoutWarning />
  </div>
</template>

<style scoped>
#app {
  min-height: 100vh;
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
