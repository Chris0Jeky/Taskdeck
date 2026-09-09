<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useWorkspaceLayoutStore } from '../../store/workspaceLayoutStore'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import {
  useWorkspaceExperimentStore,
  WORKSPACE_COMPARISON_SCENARIOS,
  WORKSPACE_COMPLETION_OUTCOMES,
  type WorkspaceCompletionOutcome,
  type WorkspaceScenario,
} from '../../store/workspaceExperimentStore'
import { useProductVersion } from '../../composables/useProductVersion'

const layout = useWorkspaceLayoutStore()
const theme = usePaperThemeStore()
const experiment = useWorkspaceExperimentStore()
const { version, displayVersion, ensureLoaded } = useProductVersion()
onMounted(() => { void ensureLoaded() })
const scenario = ref<WorkspaceScenario | null>(null)
const completionOutcome = ref<WorkspaceCompletionOutcome | null>(null)
const ease = ref<number | null>(null)
const note = ref('')
const saved = ref(false)
const versions = [
  { id: 'classic', name: 'Classic', headline: 'The familiar workspace.', detail: 'The existing Taskdeck home and navigation. A useful baseline for every comparison.', glyph: '▦' },
  { id: 'studio', name: 'Studio', headline: 'A place to make progress.', detail: 'An airy daily desk, project spaces and a place for loose thoughts. Assistance stays beside the work.', glyph: '◒' },
  { id: 'companion', name: 'Companion', headline: 'Think through the work together.', detail: 'Real contextual conversations and inspectable proposals, with projects and memory close by.', glyph: '✧' },
  { id: 'unified', name: 'Unified', headline: 'One workspace. Your pace.', detail: 'A familiar sidebar, adjustable detail, Thinking Decks, quiet insights and maintainable memory.', glyph: '▱' },
] as const

function record() {
  if (!scenario.value || !completionOutcome.value) {
    saved.value = false
    return
  }
  saved.value = experiment.record({
    experience: layout.experience,
    presentation: layout.presentation,
    theme: theme.mode,
    build: version.value,
    scenario: scenario.value,
    completionOutcome: completionOutcome.value,
    ease: ease.value,
    note: note.value,
  })
  if (saved.value) {
    scenario.value = null
    completionOutcome.value = null
    ease.value = null
    note.value = ''
  }
}

function download() {
  const url = URL.createObjectURL(new Blob([experiment.exportJson()], { type: 'application/json' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'taskdeck-workspace-comparison.json'
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
</script>

<template>
  <div class="experience-lab">
    <header><p class="experience-lab__eyebrow">ON YOUR TERMS</p><h1>Find your way of working.</h1><p>Four experiences, the same projects. Switch freely and compare them with a real task.</p></header>
    <div class="experience-lab__versions">
      <article v-for="version in versions" :key="version.id" :class="{ 'is-selected': layout.experience === version.id }">
        <span class="experience-lab__glyph" aria-hidden="true">{{ version.glyph }}</span><h2>{{ version.name }}</h2><h3>{{ version.headline }}</h3><p>{{ version.detail }}</p>
        <button type="button" :aria-pressed="layout.experience === version.id" @click="layout.setExperience(version.id)">{{ layout.experience === version.id ? 'Selected' : `Try ${version.name}` }}</button>
      </article>
    </div>
    <p><RouterLink to="/workspace/home">Open your workspace ↗</RouterLink> · <RouterLink to="/workspace/settings/appearance">Choose a theme and detail level</RouterLink></p>
    <section class="experience-lab__protocol"><h2>Same task, different perspective.</h2><ol><li>Capture a rough thought and follow it through Review to a board.</li><li>Open a card’s Thinking Deck and leave a thread for next time.</li><li>Check quiet insights and answer a useful question in Memory.</li><li>Switch experience. See what becomes easier to find or harder to understand.</li></ol><p>These are personal comparison notes. They are not a statistical A/B result. Nothing is assigned automatically or sent as telemetry.</p></section>
    <form class="experience-lab__notes" @submit.prevent="record">
      <h2>Keep an observation</h2><p>Recording {{ layout.experience }} / {{ layout.presentation }} / {{ theme.mode }} · backend version {{ displayVersion || 'unavailable' }}. Notes stay in this session; export them before reloading or signing out.</p>
      <label for="experience-scenario">What scenario did you try?</label><select id="experience-scenario" v-model="scenario" required><option :value="null" disabled>Select a scenario</option><option v-for="item in WORKSPACE_COMPARISON_SCENARIOS" :key="item.id" :value="item.id">{{ item.label }}</option></select>
      <label for="experience-outcome">What happened?</label><select id="experience-outcome" v-model="completionOutcome" required><option :value="null" disabled>Select an outcome</option><option v-for="item in WORKSPACE_COMPLETION_OUTCOMES" :key="item.id" :value="item.id">{{ item.label }}</option></select>
      <label for="experience-ease">How easy was it to continue your work? (optional)</label><select id="experience-ease" v-model="ease"><option :value="null">Leave unrated</option><option :value="1">1 — Difficult</option><option :value="2">2 — Some friction</option><option :value="3">3 — Reasonable</option><option :value="4">4 — Easy</option><option :value="5">5 — Effortless</option></select>
      <label for="experience-note">What helped, or got in the way?</label><textarea id="experience-note" v-model="note" rows="3" maxlength="2000" />
      <button type="submit" :disabled="!scenario || !completionOutcome">Record observation</button><span v-if="saved" role="status">Observation recorded.</span>
    </form>
    <section v-if="experiment.trials.length" class="experience-lab__results"><h2>Your observations</h2><ul><li v-for="(trial, index) in experiment.trials" :key="index"><strong>{{ trial.experience }} · {{ WORKSPACE_COMPLETION_OUTCOMES.find(item => item.id === trial.completionOutcome)?.label }}</strong><span>{{ WORKSPACE_COMPARISON_SCENARIOS.find(item => item.id === trial.scenario)?.label }} · {{ trial.ease === null ? 'Ease not rated' : `${trial.ease}/5` }} · {{ trial.build || 'Build unavailable' }}</span><p>{{ trial.note || 'No note added.' }}</p></li></ul><button type="button" @click="download">Export observations</button><button type="button" @click="experiment.clear">Clear observations</button></section>
  </div>
</template>

<style scoped>
.experience-lab{max-width:1250px;margin:auto;color:var(--ink,var(--td-text-primary));display:grid;gap:24px;font-family:var(--sans,system-ui,sans-serif)}h1{font:450 clamp(30px,4vw,48px)/1.15 var(--serif,Georgia,serif);margin:0 0 18px}h2{font-size:23px;margin:0 0 12px}h3{font-size:14px;font-weight:550}.experience-lab p{font-size:14px;line-height:1.7;color:var(--ink-muted,var(--td-text-secondary))}.experience-lab a{color:inherit}.experience-lab__eyebrow{letter-spacing:.14em;font-size:11px!important}.experience-lab__versions{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(235px,100%),1fr));gap:16px}.experience-lab__versions article{background:var(--paper-card,var(--td-surface-raised));border:1px solid var(--line,var(--td-border-default));border-radius:16px;padding:24px;display:flex;flex-direction:column;align-items:flex-start;gap:6px}.experience-lab__versions .is-selected{outline:2px solid var(--olive,var(--td-text-primary));outline-offset:2px}.experience-lab__versions article p{flex:1}.experience-lab__glyph{font-size:35px;margin-bottom:14px}.experience-lab button{border:1px solid var(--line,var(--td-border-default));background:var(--paper-card,var(--td-surface-raised));color:inherit;border-radius:8px;padding:11px 17px;min-height:44px;font:inherit;font-size:13px;cursor:pointer}.experience-lab button[aria-pressed=true]{background:var(--ink,var(--td-text-primary));color:var(--paper,var(--td-surface-base))}.experience-lab__protocol,.experience-lab__notes,.experience-lab__results{border-top:1px solid var(--line,var(--td-border-default));padding-top:24px}.experience-lab ol{padding-left:23px;line-height:2;font-size:14px}.experience-lab__notes{display:grid;gap:12px;max-width:760px}.experience-lab label{font-size:13px}.experience-lab select,.experience-lab textarea{color:inherit;background:var(--paper-card,var(--td-surface-raised));border:1px solid var(--line,var(--td-border-default));border-radius:8px;padding:12px;font:inherit;width:100%}.experience-lab__notes button{justify-self:start}.experience-lab__results ul{padding:0;list-style:none;display:grid;gap:12px}.experience-lab__results li{padding:18px;border:1px solid var(--line,var(--td-border-default));border-radius:10px}.experience-lab__results li span{display:block;font-size:12px;margin-top:6px}.experience-lab__results button{margin-right:12px}.experience-lab__notes>span{font-size:13px}
</style>
