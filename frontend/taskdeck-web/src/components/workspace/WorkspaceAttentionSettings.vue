<script setup lang="ts">
import { onMounted } from 'vue'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceAttentionStore } from '../../store/workspaceAttentionStore'
import { isDemoMode } from '../../utils/demoMode'
const session = useSessionStore()
const attention = useWorkspaceAttentionStore()
onMounted(() => { void attention.load() })
</script>

<template>
  <section v-if="!isDemoMode && !session.isDemo" class="attention-settings" aria-label="Optional reminders">
    <h2>Occasional reminders</h2>
    <p>Show a quiet link to an existing question while you browse a board. At most two per UTC day, at least two hours apart across your devices. No new questions or model calls are made.</p>
    <p>Reminders stay hidden while typing, using a dialog, working in Focus or Zen, or away from this tab.</p>
    <label><input type="checkbox" :checked="attention.settings?.enabled ?? false" :disabled="attention.busy || !attention.settings"
      @change="attention.save(($event.target as HTMLInputElement).checked)"> Enable occasional reminders</label>
    <p v-if="attention.error" role="status">{{ attention.error }}</p>
    <button v-if="!attention.settings && !attention.busy" type="button" @click="attention.load">Reload reminder preference</button>
  </section>
</template>

<style scoped>
.attention-settings { display: grid; gap: .65rem; padding: 1rem; border: 1px solid var(--td-border-default); border-radius: .6rem; background: var(--td-surface-container); color: var(--td-text-primary); }
h2,p { margin: 0; } label { display: flex; align-items: center; gap: .5rem; } input { width: 1.2rem; height: 1.2rem; } button { justify-self: start; }
</style>
