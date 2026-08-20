<script setup lang="ts">
import { ref, computed } from 'vue'
import { useTraceRecorder } from '../composables/useTraceRecorder'
import { createReplayEngine, type TraceReplayEngine } from '../utils/traceReplay'
import {
  validateScenario,
  createBlankScenario,
  createBlankStep,
  parseScenarioJson,
  type Scenario,
  type ScenarioStep,
  type ScenarioStepType,
  type ValidationError,
} from '../utils/scenarioSchema'
import type { Trace, ReplayState } from '../types/trace'
import { logError } from '../utils/errorReporting'

// --- Tab state ---
const activeTab = ref<'trace' | 'scenario'>('trace')

// --- Trace Recorder ---
const recorder = useTraceRecorder()
const traceName = ref('New Trace')
const completedTraces = ref<Trace[]>([])

function startRecording() {
  recorder.start(traceName.value || 'Untitled Trace')
}

function stopRecording() {
  const trace = recorder.stop()
  if (trace) {
    completedTraces.value.push(trace)
  }
}

function deleteTrace(index: number) {
  completedTraces.value.splice(index, 1)
  if (replayTraceIndex.value === index) {
    stopReplay()
    replayTraceIndex.value = -1
  }
}

function exportTrace(trace: Trace) {
  const blob = new Blob([JSON.stringify(trace, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${trace.name.replace(/\s+/g, '-').toLowerCase()}.trace.json`
  a.click()
  URL.revokeObjectURL(url)
}

function importTrace() {
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.json'
  input.onchange = async (e) => {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file) return
    try {
      const text = await file.text()
      const parsed = JSON.parse(text) as Trace
      if (parsed.id && parsed.actions && Array.isArray(parsed.actions)) {
        completedTraces.value.push(parsed)
      }
    } catch (err) {
      logError('Failed to import trace:', err)
      traceError.value = 'Failed to import trace file.'
    }
  }
  input.click()
}

// --- Trace Replay ---
const replayTraceIndex = ref(-1)
const replayState = ref<ReplayState | null>(null)
const traceError = ref<string | null>(null)
let replayEngine: TraceReplayEngine | null = null

function startReplay(index: number) {
  stopReplay()
  const trace = completedTraces.value[index]
  if (!trace) return

  replayTraceIndex.value = index
  replayEngine = createReplayEngine(trace)
  replayEngine.onStateChange((state) => {
    replayState.value = { ...state }
  })
  replayEngine.onAction((action, idx) => {
    // Log action during replay for analysis
    console.log(`[replay] Step ${idx + 1}: ${action.label} (${action.type})`)
  })
  replayEngine.play()
}

function pauseReplay() {
  replayEngine?.pause()
}

function resumeReplay() {
  replayEngine?.play()
}

function stopReplay() {
  replayEngine?.dispose()
  replayEngine = null
  replayState.value = null
  replayTraceIndex.value = -1
}

const replayProgress = computed(() => {
  if (!replayState.value) return 0
  if (replayState.value.totalActions === 0) return 100
  return Math.round((replayState.value.currentIndex / replayState.value.totalActions) * 100)
})

// --- Scenario Editor ---
const scenario = ref<Scenario>(createBlankScenario())
const scenarioErrors = ref<ValidationError[]>([])
const scenarioJsonView = ref(false)
const scenarioJsonText = ref('')

const STEP_TYPES: ScenarioStepType[] = [
  'navigate', 'click', 'fill', 'wait', 'assert', 'api-seed', 'store-dispatch',
]

function addStep(type: ScenarioStepType) {
  scenario.value.steps.push(createBlankStep(type))
  scenario.value.updatedAt = new Date().toISOString()
}

function removeStep(index: number) {
  scenario.value.steps.splice(index, 1)
  scenario.value.updatedAt = new Date().toISOString()
}

function moveStep(index: number, direction: 'up' | 'down') {
  const target = direction === 'up' ? index - 1 : index + 1
  if (target < 0 || target >= scenario.value.steps.length) return
  const steps = scenario.value.steps
  const temp = steps[index]
  steps[index] = steps[target]
  steps[target] = temp
  scenario.value.updatedAt = new Date().toISOString()
}

function validateCurrentScenario() {
  scenarioErrors.value = validateScenario(scenario.value)
}

function exportScenario() {
  validateCurrentScenario()
  if (scenarioErrors.value.length > 0) return

  const blob = new Blob([JSON.stringify(scenario.value, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${scenario.value.name.replace(/\s+/g, '-').toLowerCase() || 'scenario'}.scenario.json`
  a.click()
  URL.revokeObjectURL(url)
}

function importScenario() {
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.json'
  input.onchange = async (e) => {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file) return
    try {
      const text = await file.text()
      const result = parseScenarioJson(text)
      if (result.scenario) {
        scenario.value = result.scenario
        scenarioErrors.value = []
      } else {
        scenarioErrors.value = result.errors
      }
    } catch (err) {
      logError('Failed to import scenario:', err)
      scenarioErrors.value = [{ path: '', message: 'Failed to read file.' }]
    }
  }
  input.click()
}

function resetScenario() {
  scenario.value = createBlankScenario()
  scenarioErrors.value = []
}

function toggleJsonView() {
  if (!scenarioJsonView.value) {
    scenarioJsonText.value = JSON.stringify(scenario.value, null, 2)
  } else {
    // Try to apply JSON edits back to form
    const result = parseScenarioJson(scenarioJsonText.value)
    if (result.scenario) {
      scenario.value = result.scenario
      scenarioErrors.value = []
    } else {
      scenarioErrors.value = result.errors
      return // Don't close JSON view if parse failed
    }
  }
  scenarioJsonView.value = !scenarioJsonView.value
}

function getStepParamKeys(step: ScenarioStep): string[] {
  return Object.keys(step.params)
}

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(1)}s`
}
</script>

<template>
  <div class="paper-devtools max-w-5xl mx-auto p-6">
    <h1 class="tk-h2 paper-devtools__title mb-1">Dev Tools</h1>
    <p class="tk-lede paper-devtools__subtitle mb-6">Internal tooling for trace replay and scenario authoring.</p>

    <!-- Tab bar -->
    <div class="flex gap-2 mb-6 border-b paper-dt-line pb-2">
      <button
        :class="[
          'px-4 py-2 rounded-t text-sm font-medium',
          activeTab === 'trace'
            ? 'paper-dt-raise paper-dt-ink-deep'
            : 'paper-dt-mute paper-dt-hover-ink',
        ]"
        @click="activeTab = 'trace'"
      >
        Trace Recorder & Replay
      </button>
      <button
        :class="[
          'px-4 py-2 rounded-t text-sm font-medium',
          activeTab === 'scenario'
            ? 'paper-dt-raise paper-dt-ink-deep'
            : 'paper-dt-mute paper-dt-hover-ink',
        ]"
        @click="activeTab = 'scenario'"
      >
        Scenario Editor
      </button>
    </div>

    <!-- Trace Tab -->
    <div v-if="activeTab === 'trace'">
      <!-- Recorder controls -->
      <div class="paper-dt-card rounded-lg p-4 mb-6">
        <h2 class="text-lg font-semibold paper-dt-ink mb-3">Trace Recorder</h2>
        <div class="flex items-center gap-3 mb-3">
          <input
            v-model="traceName"
            type="text"
            aria-label="Trace name"
            placeholder="Trace name"
            class="paper-dt-field border paper-dt-line rounded px-3 py-1.5 text-sm paper-dt-ink w-64"
            :disabled="recorder.isRecording.value"
          />
          <button
            v-if="!recorder.isRecording.value"
            class="px-4 py-1.5 paper-dt-accent-ember paper-dt-hover-accent paper-dt-on-accent text-sm rounded"
            @click="startRecording"
          >
            Start Recording
          </button>
          <button
            v-else
            class="px-4 py-1.5 paper-dt-raise paper-dt-hover-raise paper-dt-on-accent text-sm rounded"
            @click="stopRecording"
          >
            Stop Recording ({{ recorder.actionCount.value }} actions)
          </button>
          <button
            class="px-3 py-1.5 paper-dt-raise paper-dt-hover-raise paper-dt-ink-2 text-sm rounded"
            @click="importTrace"
          >
            Import Trace
          </button>
        </div>
        <p v-if="recorder.isRecording.value" class="text-xs paper-dt-warn">
          Recording in progress. Use the app normally; actions will be captured.
        </p>
        <p v-if="traceError" class="text-xs paper-dt-danger mt-1">{{ traceError }}</p>
      </div>

      <!-- Completed traces -->
      <div v-if="completedTraces.length > 0">
        <h2 class="text-lg font-semibold paper-dt-ink mb-3">Recorded Traces</h2>
        <div class="space-y-3">
          <div
            v-for="(trace, index) in completedTraces"
            :key="trace.id"
            class="paper-dt-card rounded-lg p-4"
          >
            <div class="flex items-center justify-between mb-2">
              <div>
                <span class="font-medium paper-dt-ink">{{ trace.name }}</span>
                <span class="text-xs paper-dt-mute ml-2">
                  {{ trace.actions.length }} actions, {{ formatDuration(trace.durationMs) }}
                </span>
              </div>
              <div class="flex gap-2">
                <button
                  v-if="replayTraceIndex !== index"
                  class="px-3 py-1 paper-dt-accent-applied paper-dt-hover-accent paper-dt-on-accent text-xs rounded"
                  @click="startReplay(index)"
                >
                  Replay
                </button>
                <template v-else>
                  <button
                    v-if="replayState?.status === 'playing'"
                    class="px-3 py-1 paper-dt-accent-warn paper-dt-hover-accent paper-dt-on-accent text-xs rounded"
                    @click="pauseReplay"
                  >
                    Pause
                  </button>
                  <button
                    v-else-if="replayState?.status === 'paused'"
                    class="px-3 py-1 paper-dt-accent-applied paper-dt-hover-accent paper-dt-on-accent text-xs rounded"
                    @click="resumeReplay"
                  >
                    Resume
                  </button>
                  <button
                    class="px-3 py-1 paper-dt-raise paper-dt-hover-raise paper-dt-on-accent text-xs rounded"
                    @click="stopReplay"
                  >
                    Stop
                  </button>
                </template>
                <button
                  class="px-3 py-1 paper-dt-raise paper-dt-hover-raise paper-dt-ink-2 text-xs rounded"
                  @click="exportTrace(trace)"
                >
                  Export
                </button>
                <button
                  class="px-3 py-1 paper-dt-raise paper-dt-hover-raise paper-dt-danger text-xs rounded"
                  @click="deleteTrace(index)"
                >
                  Delete
                </button>
              </div>
            </div>

            <!-- Replay progress bar -->
            <div v-if="replayTraceIndex === index && replayState" class="mb-2">
              <div class="w-full paper-dt-raise rounded-full h-2">
                <div
                  class="paper-devtools__progress-fill paper-dt-accent-applied h-2 rounded-full transition-all"
                />
              </div>
              <div class="flex justify-between text-xs paper-dt-mute mt-1">
                <span>{{ replayState.currentIndex }}/{{ replayState.totalActions }}</span>
                <span>{{ replayState.status }}</span>
              </div>
            </div>

            <!-- Action list -->
            <details class="mt-2">
              <summary class="text-xs paper-dt-mute cursor-pointer paper-dt-hover-ink">
                Show actions
              </summary>
              <div class="mt-2 max-h-60 overflow-y-auto">
                <table class="w-full text-xs">
                  <thead>
                    <tr class="paper-dt-faint border-b paper-dt-line">
                      <th class="text-left py-1 px-2">#</th>
                      <th class="text-left py-1 px-2">Type</th>
                      <th class="text-left py-1 px-2">Label</th>
                      <th class="text-right py-1 px-2">Offset</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr
                      v-for="(action, ai) in trace.actions"
                      :key="action.id"
                      :class="[
                        'border-b paper-dt-line',
                        replayTraceIndex === index && replayState && ai < replayState.currentIndex
                          ? 'paper-dt-ink-2'
                          : 'paper-dt-faint',
                      ]"
                    >
                      <td class="py-1 px-2">{{ ai + 1 }}</td>
                      <td class="py-1 px-2">{{ action.type }}</td>
                      <td class="py-1 px-2">{{ action.label }}</td>
                      <td class="py-1 px-2 text-right">{{ formatDuration(action.offsetMs) }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </details>
          </div>
        </div>
      </div>

      <p v-else class="text-sm paper-dt-faint">
        No traces recorded yet. Start a recording or import a trace file.
      </p>
    </div>

    <!-- Scenario Tab -->
    <div v-if="activeTab === 'scenario'">
      <!-- Scenario metadata -->
      <div class="paper-dt-card rounded-lg p-4 mb-6">
        <h2 class="text-lg font-semibold paper-dt-ink mb-3">Scenario Editor</h2>
        <div class="grid grid-cols-2 gap-4 mb-4">
          <div>
            <label for="scenario-name" class="block text-xs paper-dt-mute mb-1">Name</label>
            <input
              id="scenario-name"
              v-model="scenario.name"
              type="text"
              placeholder="Scenario name"
              class="w-full paper-dt-field border paper-dt-line rounded px-3 py-1.5 text-sm paper-dt-ink"
            />
          </div>
          <div>
            <label for="scenario-tags" class="block text-xs paper-dt-mute mb-1">Tags (comma-separated)</label>
            <input
              id="scenario-tags"
              :value="(scenario.tags ?? []).join(', ')"
              type="text"
              placeholder="demo, onboarding"
              class="w-full paper-dt-field border paper-dt-line rounded px-3 py-1.5 text-sm paper-dt-ink"
              @input="scenario.tags = ($event.target as HTMLInputElement).value.split(',').map(t => t.trim()).filter(Boolean)"
            />
          </div>
        </div>
        <div class="mb-4">
          <label for="scenario-description" class="block text-xs paper-dt-mute mb-1">Description</label>
          <textarea
            id="scenario-description"
            v-model="scenario.description"
            placeholder="What does this scenario demonstrate?"
            rows="2"
            class="w-full paper-dt-field border paper-dt-line rounded px-3 py-1.5 text-sm paper-dt-ink"
          />
        </div>

        <!-- Actions row -->
        <div class="flex items-center gap-2 flex-wrap">
          <span class="text-xs paper-dt-mute">Add step:</span>
          <button
            v-for="st in STEP_TYPES"
            :key="st"
            class="px-2 py-1 paper-dt-raise paper-dt-hover-raise paper-dt-ink-2 text-xs rounded"
            @click="addStep(st)"
          >
            {{ st }}
          </button>
          <div class="flex-1" />
          <button
            class="px-3 py-1 paper-dt-raise paper-dt-hover-raise paper-dt-ink-2 text-xs rounded"
            @click="toggleJsonView"
          >
            {{ scenarioJsonView ? 'Form View' : 'JSON View' }}
          </button>
          <button
            class="px-3 py-1 paper-dt-accent-ember paper-dt-hover-accent paper-dt-on-accent text-xs rounded"
            @click="validateCurrentScenario"
          >
            Validate
          </button>
          <button
            class="px-3 py-1 paper-dt-raise paper-dt-hover-raise paper-dt-ink-2 text-xs rounded"
            @click="importScenario"
          >
            Import
          </button>
          <button
            class="px-3 py-1 paper-dt-accent-applied paper-dt-hover-accent paper-dt-on-accent text-xs rounded"
            @click="exportScenario"
          >
            Export
          </button>
          <button
            class="px-3 py-1 paper-dt-raise paper-dt-hover-raise paper-dt-danger text-xs rounded"
            @click="resetScenario"
          >
            Reset
          </button>
        </div>
      </div>

      <!-- Validation errors -->
      <div v-if="scenarioErrors.length > 0" class="paper-dt-danger-wash border paper-dt-line-danger rounded-lg p-3 mb-4">
        <h3 class="text-sm font-medium paper-dt-danger mb-2">Validation Errors</h3>
        <ul class="list-disc list-inside text-xs paper-dt-danger space-y-1">
          <li v-for="(err, ei) in scenarioErrors" :key="ei">
            <span v-if="err.path" class="font-mono paper-dt-danger">{{ err.path }}:</span>
            {{ err.message }}
          </li>
        </ul>
      </div>

      <!-- JSON view -->
      <div v-if="scenarioJsonView" class="mb-6">
        <textarea
          v-model="scenarioJsonText"
          aria-label="Scenario JSON"
          rows="20"
          class="w-full paper-dt-field border paper-dt-line rounded px-3 py-2 text-xs font-mono paper-dt-ink"
        />
      </div>

      <!-- Steps form view -->
      <div v-else>
        <div v-if="scenario.steps.length === 0" class="text-sm paper-dt-faint py-8 text-center">
          No steps yet. Add a step using the buttons above.
        </div>
        <div v-else class="space-y-3">
          <div
            v-for="(step, si) in scenario.steps"
            :key="step.id"
            class="paper-dt-step-card rounded-lg p-4 border-l-4"
            :class="{
              'paper-dt-line-ember': step.type === 'navigate',
              'paper-dt-line-warn': step.type === 'click',
              'paper-dt-line-applied': step.type === 'fill',
              'paper-dt-line': step.type === 'wait',
              'paper-dt-line-ink': step.type === 'assert',
              'paper-dt-line-mute': step.type === 'api-seed',
              'paper-dt-line-ember-deep': step.type === 'store-dispatch',
            }"
          >
            <div class="flex items-center justify-between mb-2">
              <div class="flex items-center gap-2">
                <span class="text-xs font-mono paper-dt-faint">#{{ si + 1 }}</span>
                <span class="text-xs font-medium paper-dt-ink-2 paper-dt-raise px-2 py-0.5 rounded">{{ step.type }}</span>
              </div>
              <div class="flex gap-1">
                <button
                  class="px-2 py-0.5 paper-dt-mute paper-dt-hover-ink text-xs"
                  :disabled="si === 0"
                  @click="moveStep(si, 'up')"
                >
                  Up
                </button>
                <button
                  class="px-2 py-0.5 paper-dt-mute paper-dt-hover-ink text-xs"
                  :disabled="si === scenario.steps.length - 1"
                  @click="moveStep(si, 'down')"
                >
                  Down
                </button>
                <button
                  class="px-2 py-0.5 paper-dt-danger paper-dt-hover-danger text-xs"
                  @click="removeStep(si)"
                >
                  Remove
                </button>
              </div>
            </div>

            <div class="grid grid-cols-2 gap-3">
              <div>
                <label :for="`step-${si}-desc`" class="block text-xs paper-dt-mute mb-1">Description</label>
                <input
                  :id="`step-${si}-desc`"
                  v-model="step.description"
                  type="text"
                  placeholder="What does this step do?"
                  class="w-full paper-dt-field border paper-dt-line rounded px-2 py-1 text-xs paper-dt-ink"
                />
              </div>
              <div>
                <label :for="`step-${si}-delay`" class="block text-xs paper-dt-mute mb-1">Delay (ms)</label>
                <input
                  :id="`step-${si}-delay`"
                  v-model.number="step.delayMs"
                  type="number"
                  min="0"
                  class="w-full paper-dt-field border paper-dt-line rounded px-2 py-1 text-xs paper-dt-ink"
                />
              </div>
            </div>

            <!-- Dynamic params -->
            <div class="mt-2 grid grid-cols-2 gap-3">
              <div v-for="key in getStepParamKeys(step)" :key="key">
                <label :for="`step-${si}-param-${key}`" class="block text-xs paper-dt-mute mb-1">{{ key }}</label>
                <input
                  :id="`step-${si}-param-${key}`"
                  :value="(step.params as unknown as Record<string, unknown>)[key]"
                  :type="typeof (step.params as unknown as Record<string, unknown>)[key] === 'number' ? 'number' : 'text'"
                  class="w-full paper-dt-field border paper-dt-line rounded px-2 py-1 text-xs paper-dt-ink"
                  @input="(step.params as unknown as Record<string, unknown>)[key] = typeof (step.params as unknown as Record<string, unknown>)[key] === 'number' ? Number(($event.target as HTMLInputElement).value) : ($event.target as HTMLInputElement).value"
                />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — DevToolsView ──
   Internal tooling (flag `devTools`).  This view was written in raw Tailwind
   `zinc-*` dark utilities plus Tailwind-palette accents, so it rendered a
   slate-grey panel inside the Paper shell and never followed Paper night.
   The colour utilities are replaced by the scoped `paper-dt-*` skin classes
   below, which read Paper tokens; every layout utility is untouched.  Tokens
   live under `.paper` / `.paper-night`, so var() fallbacks keep the surface
   legible outside the Paper shell. */

.paper-devtools {
  font-family: var(--sans, system-ui, sans-serif);
  /* Legacy ("off") mode: Paper vars are scoped to .paper/.paper-night, so a root
     that sets --ink must paint --paper alongside it or the near-black fallback
     lands on AppShell's Obsidian surface. No-op inside the Paper shell. */
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

.paper-devtools__title { margin: 0; font-size: var(--t-h2, 32px); }
.paper-devtools__subtitle { margin: 0; color: var(--ink-2, #3a352d); }

.paper-devtools__progress-fill {
  width: v-bind("replayProgress + '%'");
}

/* ── Ink ladder ── */
.paper-dt-ink-deep { color: var(--ink-deep, #0a0908); }
.paper-dt-ink { color: var(--ink, #1a1814); }
.paper-dt-ink-2 { color: var(--ink-2, #3a352d); }
.paper-dt-mute { color: var(--mute, #635c4e); }
.paper-dt-faint { color: var(--faint, #6c6557); }
.paper-dt-on-accent { color: var(--td-on-ember, #fefaf6); }
.paper-dt-danger { color: var(--ember-deep, #7a2e15); }
.paper-dt-warn { color: var(--overdue, #8c4a26); }

.paper-dt-hover-ink:hover { color: var(--ink-deep, #0a0908); }
.paper-dt-hover-danger:hover { color: var(--ember, #a8421f); }

/* ── Substrate ── */
.paper-dt-card {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-dt-field {
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

.paper-dt-raise { background: var(--paper-2, #ebe5d8); }
.paper-dt-hover-raise:hover { background: var(--paper-edge, #e3dac8); }
.paper-dt-danger-wash { background: var(--ember-bloom, #a8421f1a); }

/* ── Accents ── */
.paper-dt-accent-ember { background: var(--ember, #a8421f); }
.paper-dt-accent-applied { background: var(--applied, #4a6b3f); }
.paper-dt-accent-warn { background: var(--overdue, #8c4a26); }
.paper-dt-hover-accent:hover { filter: brightness(1.1); }

/* ── Hairlines ── */
.paper-dt-line { border-color: var(--line, #d8d0bf); }
.paper-dt-line-danger { border-color: var(--ember, #a8421f); }
.paper-dt-line-applied { border-color: var(--applied, #4a6b3f); }
.paper-dt-line-warn { border-color: var(--overdue, #8c4a26); }
.paper-dt-line-ember { border-color: var(--ember, #a8421f); }
.paper-dt-line-ember-deep { border-color: var(--ember-deep, #7a2e15); }
.paper-dt-line-ink { border-color: var(--ink-2, #3a352d); }
.paper-dt-line-mute { border-color: var(--mute, #635c4e); }

/* Scenario step card: keeps the original left-accent rule (Tailwind supplies
   the 4px width, the accent class the colour) so no shorthand border fights it. */
.paper-dt-step-card {
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  border-left-style: solid;
}

/* Inputs and textareas keep their Tailwind box metrics but take Paper focus. */
.paper-devtools input:focus,
.paper-devtools textarea:focus,
.paper-devtools select:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}
</style>
