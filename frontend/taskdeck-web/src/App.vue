<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import ToastContainer from './components/common/ToastContainer.vue'
import AppShell from './components/shell/AppShell.vue'
import { useSessionStore } from './store/sessionStore'
import { useFeatureFlagStore } from './store/featureFlagStore'

const route = useRoute()
const session = useSessionStore()
const featureFlags = useFeatureFlagStore()

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
    <!-- Shell layout for workspace routes -->
    <AppShell v-if="showShell" />
    <!-- Direct render for public routes (login/register) -->
    <router-view v-else />
    <ToastContainer />
  </div>
</template>

<style scoped>
#app {
  min-height: 100vh;
}
</style>
