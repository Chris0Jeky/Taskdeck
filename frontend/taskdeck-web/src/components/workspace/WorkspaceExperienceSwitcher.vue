<script setup lang="ts">
import { useWorkspaceLayoutStore, workspaceExperiences, workspacePresentations } from '../../store/workspaceLayoutStore'
const layout = useWorkspaceLayoutStore()
withDefaults(defineProps<{ compact?: boolean }>(), { compact: false })
const experienceLabels = { classic: 'Classic', studio: 'Studio', companion: 'Companion', unified: 'Unified' }
const presentationLabels = { zen: 'Zen', studio: 'Studio', control: 'Control' }
</script>

<template>
  <div class="workspace-switcher" :class="{ 'workspace-switcher--compact': compact }">
    <label class="workspace-switcher__field">
      <span>Experience</span>
      <select :value="layout.experience" aria-label="Workspace experience"
        @change="layout.setExperience(($event.target as HTMLSelectElement).value as typeof layout.experience)">
        <option v-for="experience in workspaceExperiences" :key="experience" :value="experience">{{ experienceLabels[experience] }}</option>
      </select>
    </label>
    <label class="workspace-switcher__field">
      <span>Presentation</span>
      <select :value="layout.presentation" aria-label="Workspace presentation"
        @change="layout.setPresentation(($event.target as HTMLSelectElement).value as typeof layout.presentation)">
        <option v-for="presentation in workspacePresentations" :key="presentation" :value="presentation">{{ presentationLabels[presentation] }}</option>
      </select>
    </label>
  </div>
</template>

<style scoped>
.workspace-switcher { display: flex; gap: 16px; flex-wrap: wrap; color: var(--td-text-primary); }
.workspace-switcher__field { display: flex; flex-direction: column; gap: 6px; font-size: 12px; font-weight: 600; }
.workspace-switcher select { min-height: 36px; padding: 6px 28px 6px 10px; border: 1px solid var(--td-border-default); border-radius: 8px; background: var(--td-surface-container-low); color: var(--td-text-primary); font: inherit; cursor: pointer; }
.workspace-switcher select:focus-visible { outline: 2px solid var(--td-color-ember); outline-offset: 3px; }
.workspace-switcher--compact { gap: 10px; }
.workspace-switcher--compact .workspace-switcher__field { flex-direction: row; align-items: center; font-size: 11px; }
@media (max-width: 720px) { .workspace-switcher--compact .workspace-switcher__field { flex-direction: column; align-items: start; } }
</style>
