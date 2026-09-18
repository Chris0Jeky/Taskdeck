<script setup lang="ts">
import { onScopeDispose, ref, watch } from 'vue'
import { useSessionStore } from '../../store/sessionStore'
import { MAX_COMPARISON_FILE_BYTES, useWorkspaceExperimentStore, WORKSPACE_COMPARISON_SCENARIOS } from '../../store/workspaceExperimentStore'

const experiment = useWorkspaceExperimentStore()
const session = useSessionStore()
const loading = ref(false)
const error = ref('')
const status = ref('')
let generation = 0
watch([() => session.userId, () => experiment.resetVersion], () => { generation++; loading.value = false; error.value = ''; status.value = '' }, { flush: 'sync' })
onScopeDispose(() => { generation++ })

async function importFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || loading.value || !session.userId) return
  const request = ++generation
  loading.value = true; error.value = ''; status.value = ''
  try {
    if (file.size > MAX_COMPARISON_FILE_BYTES) throw new Error('Choose a comparison file smaller than 2 MiB.')
    const json = await file.text()
    if (request !== generation) return
    const added = await experiment.importJson(json)
    if (request === generation) status.value = `Imported ${added} ${added === 1 ? 'observation' : 'observations'}. Exact duplicates were skipped.`
  } catch (failure) {
    if (request === generation) error.value = failure instanceof Error ? failure.message : 'Unable to import this file.'
  } finally { if (request === generation) loading.value = false }
}
</script>

<template>
  <section class="comparison-files" aria-label="Compare observations across releases">
    <h2>Bring observations forward</h2>
    <p>Export your notes to keep them across reloads and releases. Import a saved comparison file here to combine up to 500 observations. Files stay on your device; importing sends no notes to the server.</p>
    <label for="comparison-import">Import saved observations</label>
    <input id="comparison-import" type="file" accept=".json,application/json" :disabled="loading || !session.userId" @change="importFile" />
    <p v-if="loading" role="status">Reading and checking observations…</p>
    <p v-if="status" role="status">{{ status }}</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <div v-if="experiment.groups.length" class="comparison-files__table">
      <table>
        <caption>Descriptive results, grouped by scenario, experience, presentation, theme and both builds. Self-selected trials do not establish an A/B winner. Older files have no frontend attribution.</caption>
        <thead><tr><th scope="col">Trial conditions</th><th scope="col">Observed outcomes</th><th scope="col">Ease</th></tr></thead>
        <tbody><tr v-for="group in experiment.groups" :key="group.key">
          <th scope="row">{{ WORKSPACE_COMPARISON_SCENARIOS.find(item => item.id === group.trial.scenario)?.label }}<small>{{ group.trial.experience }} / {{ group.trial.presentation }} / {{ group.trial.theme }}</small><details><summary>Builds</summary><small>Backend: {{ group.trial.build || 'Unavailable' }}</small><small>Frontend: {{ group.trial.frontendBuild || 'Unattributed' }}</small></details></th>
          <td>{{ group.completed }} completed, {{ group.blocked }} blocked, {{ group.count - group.completed - group.blocked }} other · {{ group.count }} total</td>
          <td>{{ group.rated ? `${(group.easeTotal / group.rated).toFixed(1)}/5 (${group.rated} rated)` : 'Unrated' }}</td>
        </tr></tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
.comparison-files { display: grid; gap: 1rem; min-width: 0; }
input { max-width: 100%; font: inherit; }
.comparison-files__table { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; font-size: .85rem; }
caption { text-align: left; padding-bottom: 1rem; }
th,td { text-align: left; vertical-align: top; padding: .75rem; border-bottom: 1px solid var(--td-border-default); overflow-wrap: anywhere; }
small { display: block; font-weight: normal; margin-top: .5rem; }
[role=alert] { color: var(--td-color-error); }
</style>
