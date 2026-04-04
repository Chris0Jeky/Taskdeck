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
      console.error('Failed to import trace:', err)
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
      console.error('Failed to import scenario:', err)
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
  <div class="max-w-5xl mx-auto p-6">
    <h1 class="text-2xl font-bold mb-1 text-zinc-100">Dev Tools</h1>
    <p class="text-sm text-zinc-400 mb-6">Internal tooling for trace replay and scenario authoring.</p>

    <!-- Tab bar -->
    <div class="flex gap-2 mb-6 border-b border-zinc-700 pb-2">
      <button
        :class="[
          'px-4 py-2 rounded-t text-sm font-medium',
          activeTab === 'trace'
            ? 'bg-zinc-700 text-zinc-100'
            : 'text-zinc-400 hover:text-zinc-200',
        ]"
        @click="activeTab = 'trace'"
      >
        Trace Recorder & Replay
      </button>
      <button
        :class="[
          'px-4 py-2 rounded-t text-sm font-medium',
          activeTab === 'scenario'
            ? 'bg-zinc-700 text-zinc-100'
            : 'text-zinc-400 hover:text-zinc-200',
        ]"
        @click="activeTab = 'scenario'"
      >
        Scenario Editor
      </button>
    </div>

    <!-- Trace Tab -->
    <div v-if="activeTab === 'trace'">
      <!-- Recorder controls -->
      <div class="bg-zinc-800 rounded-lg p-4 mb-6">
        <h2 class="text-lg font-semibold text-zinc-200 mb-3">Trace Recorder</h2>
        <div class="flex items-center gap-3 mb-3">
          <input
            v-model="traceName"
            type="text"
            aria-label="Trace name"
            placeholder="Trace name"
            class="bg-zinc-900 border border-zinc-600 rounded px-3 py-1.5 text-sm text-zinc-200 w-64"
            :disabled="recorder.isRecording.value"
          />
          <button
            v-if="!recorder.isRecording.value"
            class="px-4 py-1.5 bg-red-600 hover:bg-red-700 text-white text-sm rounded"
            @click="startRecording"
          >
            Start Recording
          </button>
          <button
            v-else
            class="px-4 py-1.5 bg-zinc-600 hover:bg-zinc-500 text-white text-sm rounded"
            @click="stopRecording"
          >
            Stop Recording ({{ recorder.actionCount.value }} actions)
          </button>
          <button
            class="px-3 py-1.5 bg-zinc-700 hover:bg-zinc-600 text-zinc-300 text-sm rounded"
            @click="importTrace"
          >
            Import Trace
          </button>
        </div>
        <p v-if="recorder.isRecording.value" class="text-xs text-amber-400">
          Recording in progress. Use the app normally; actions will be captured.
        </p>
        <p v-if="traceError" class="text-xs text-red-400 mt-1">{{ traceError }}</p>
      </div>

      <!-- Completed traces -->
      <div v-if="completedTraces.length > 0">
        <h2 class="text-lg font-semibold text-zinc-200 mb-3">Recorded Traces</h2>
        <div class="space-y-3">
          <div
            v-for="(trace, index) in completedTraces"
            :key="trace.id"
            class="bg-zinc-800 rounded-lg p-4"
          >
            <div class="flex items-center justify-between mb-2">
              <div>
                <span class="font-medium text-zinc-200">{{ trace.name }}</span>
                <span class="text-xs text-zinc-400 ml-2">
                  {{ trace.actions.length }} actions, {{ formatDuration(trace.durationMs) }}
                </span>
              </div>
              <div class="flex gap-2">
                <button
                  v-if="replayTraceIndex !== index"
                  class="px-3 py-1 bg-emerald-700 hover:bg-emerald-600 text-white text-xs rounded"
                  @click="startReplay(index)"
                >
                  Replay
                </button>
                <template v-else>
                  <button
                    v-if="replayState?.status === 'playing'"
                    class="px-3 py-1 bg-amber-600 hover:bg-amber-500 text-white text-xs rounded"
                    @click="pauseReplay"
                  >
                    Pause
                  </button>
                  <button
                    v-else-if="replayState?.status === 'paused'"
                    class="px-3 py-1 bg-emerald-700 hover:bg-emerald-600 text-white text-xs rounded"
                    @click="resumeReplay"
                  >
                    Resume
                  </button>
                  <button
                    class="px-3 py-1 bg-zinc-600 hover:bg-zinc-500 text-white text-xs rounded"
                    @click="stopReplay"
                  >
                    Stop
                  </button>
                </template>
                <button
                  class="px-3 py-1 bg-zinc-700 hover:bg-zinc-600 text-zinc-300 text-xs rounded"
                  @click="exportTrace(trace)"
                >
                  Export
                </button>
                <button
                  class="px-3 py-1 bg-zinc-700 hover:bg-zinc-600 text-red-400 text-xs rounded"
                  @click="deleteTrace(index)"
                >
                  Delete
                </button>
              </div>
            </div>

            <!-- Replay progress bar -->
            <div v-if="replayTraceIndex === index && replayState" class="mb-2">
              <div class="w-full bg-zinc-700 rounded-full h-2">
                <div
                  class="bg-emerald-500 h-2 rounded-full transition-all"
                  :style="{ width: `${replayProgress}%` }"
                />
              </div>
              <div class="flex justify-between text-xs text-zinc-400 mt-1">
                <span>{{ replayState.currentIndex }}/{{ replayState.totalActions }}</span>
                <span>{{ replayState.status }}</span>
              </div>
            </div>

            <!-- Action list -->
            <details class="mt-2">
              <summary class="text-xs text-zinc-400 cursor-pointer hover:text-zinc-300">
                Show actions
              </summary>
              <div class="mt-2 max-h-60 overflow-y-auto">
                <table class="w-full text-xs">
                  <thead>
                    <tr class="text-zinc-500 border-b border-zinc-700">
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
                        'border-b border-zinc-800',
                        replayTraceIndex === index && replayState && ai < replayState.currentIndex
                          ? 'text-zinc-300'
                          : 'text-zinc-500',
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

      <p v-else class="text-sm text-zinc-500">
        No traces recorded yet. Start a recording or import a trace file.
      </p>
    </div>

    <!-- Scenario Tab -->
    <div v-if="activeTab === 'scenario'">
      <!-- Scenario metadata -->
      <div class="bg-zinc-800 rounded-lg p-4 mb-6">
        <h2 class="text-lg font-semibold text-zinc-200 mb-3">Scenario Editor</h2>
        <div class="grid grid-cols-2 gap-4 mb-4">
          <div>
            <label for="scenario-name" class="block text-xs text-zinc-400 mb-1">Name</label>
            <input
              id="scenario-name"
              v-model="scenario.name"
              type="text"
              placeholder="Scenario name"
              class="w-full bg-zinc-900 border border-zinc-600 rounded px-3 py-1.5 text-sm text-zinc-200"
            />
          </div>
          <div>
            <label for="scenario-tags" class="block text-xs text-zinc-400 mb-1">Tags (comma-separated)</label>
            <input
              id="scenario-tags"
              :value="(scenario.tags ?? []).join(', ')"
              type="text"
              placeholder="demo, onboarding"
              class="w-full bg-zinc-900 border border-zinc-600 rounded px-3 py-1.5 text-sm text-zinc-200"
              @input="scenario.tags = ($event.target as HTMLInputElement).value.split(',').map(t => t.trim()).filter(Boolean)"
            />
          </div>
        </div>
        <div class="mb-4">
          <label for="scenario-description" class="block text-xs text-zinc-400 mb-1">Description</label>
          <textarea
            id="scenario-description"
            v-model="scenario.description"
            placeholder="What does this scenario demonstrate?"
            rows="2"
            class="w-full bg-zinc-900 border border-zinc-600 rounded px-3 py-1.5 text-sm text-zinc-200"
          />
        </div>

        <!-- Actions row -->
        <div class="flex items-center gap-2 flex-wrap">
          <span class="text-xs text-zinc-400">Add step:</span>
          <button
            v-for="st in STEP_TYPES"
            :key="st"
            class="px-2 py-1 bg-zinc-700 hover:bg-zinc-600 text-zinc-300 text-xs rounded"
            @click="addStep(st)"
          >
            {{ st }}
          </button>
          <div class="flex-1" />
          <button
            class="px-3 py-1 bg-zinc-700 hover:bg-zinc-600 text-zinc-300 text-xs rounded"
            @click="toggleJsonView"
          >
            {{ scenarioJsonView ? 'Form View' : 'JSON View' }}
          </button>
          <button
            class="px-3 py-1 bg-blue-700 hover:bg-blue-600 text-white text-xs rounded"
            @click="validateCurrentScenario"
          >
            Validate
          </button>
          <button
            class="px-3 py-1 bg-zinc-700 hover:bg-zinc-600 text-zinc-300 text-xs rounded"
            @click="importScenario"
          >
            Import
          </button>
          <button
            class="px-3 py-1 bg-emerald-700 hover:bg-emerald-600 text-white text-xs rounded"
            @click="exportScenario"
          >
            Export
          </button>
          <button
            class="px-3 py-1 bg-zinc-700 hover:bg-zinc-600 text-red-400 text-xs rounded"
            @click="resetScenario"
          >
            Reset
          </button>
        </div>
      </div>

      <!-- Validation errors -->
      <div v-if="scenarioErrors.length > 0" class="bg-red-900/30 border border-red-700 rounded-lg p-3 mb-4">
        <h3 class="text-sm font-medium text-red-400 mb-2">Validation Errors</h3>
        <ul class="list-disc list-inside text-xs text-red-300 space-y-1">
          <li v-for="(err, ei) in scenarioErrors" :key="ei">
            <span v-if="err.path" class="font-mono text-red-400">{{ err.path }}:</span>
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
          class="w-full bg-zinc-900 border border-zinc-600 rounded px-3 py-2 text-xs font-mono text-zinc-200"
        />
      </div>

      <!-- Steps form view -->
      <div v-else>
        <div v-if="scenario.steps.length === 0" class="text-sm text-zinc-500 py-8 text-center">
          No steps yet. Add a step using the buttons above.
        </div>
        <div v-else class="space-y-3">
          <div
            v-for="(step, si) in scenario.steps"
            :key="step.id"
            class="bg-zinc-800 rounded-lg p-4 border-l-4"
            :class="{
              'border-blue-500': step.type === 'navigate',
              'border-amber-500': step.type === 'click',
              'border-emerald-500': step.type === 'fill',
              'border-zinc-500': step.type === 'wait',
              'border-purple-500': step.type === 'assert',
              'border-cyan-500': step.type === 'api-seed',
              'border-orange-500': step.type === 'store-dispatch',
            }"
          >
            <div class="flex items-center justify-between mb-2">
              <div class="flex items-center gap-2">
                <span class="text-xs font-mono text-zinc-500">#{{ si + 1 }}</span>
                <span class="text-xs font-medium text-zinc-300 bg-zinc-700 px-2 py-0.5 rounded">{{ step.type }}</span>
              </div>
              <div class="flex gap-1">
                <button
                  class="px-2 py-0.5 text-zinc-400 hover:text-zinc-200 text-xs"
                  :disabled="si === 0"
                  @click="moveStep(si, 'up')"
                >
                  Up
                </button>
                <button
                  class="px-2 py-0.5 text-zinc-400 hover:text-zinc-200 text-xs"
                  :disabled="si === scenario.steps.length - 1"
                  @click="moveStep(si, 'down')"
                >
                  Down
                </button>
                <button
                  class="px-2 py-0.5 text-red-400 hover:text-red-300 text-xs"
                  @click="removeStep(si)"
                >
                  Remove
                </button>
              </div>
            </div>

            <div class="grid grid-cols-2 gap-3">
              <div>
                <label :for="`step-${si}-desc`" class="block text-xs text-zinc-400 mb-1">Description</label>
                <input
                  :id="`step-${si}-desc`"
                  v-model="step.description"
                  type="text"
                  placeholder="What does this step do?"
                  class="w-full bg-zinc-900 border border-zinc-600 rounded px-2 py-1 text-xs text-zinc-200"
                />
              </div>
              <div>
                <label :for="`step-${si}-delay`" class="block text-xs text-zinc-400 mb-1">Delay (ms)</label>
                <input
                  :id="`step-${si}-delay`"
                  v-model.number="step.delayMs"
                  type="number"
                  min="0"
                  class="w-full bg-zinc-900 border border-zinc-600 rounded px-2 py-1 text-xs text-zinc-200"
                />
              </div>
            </div>

            <!-- Dynamic params -->
            <div class="mt-2 grid grid-cols-2 gap-3">
              <div v-for="key in getStepParamKeys(step)" :key="key">
                <label :for="`step-${si}-param-${key}`" class="block text-xs text-zinc-400 mb-1">{{ key }}</label>
                <input
                  :id="`step-${si}-param-${key}`"
                  :value="(step.params as unknown as Record<string, unknown>)[key]"
                  :type="typeof (step.params as unknown as Record<string, unknown>)[key] === 'number' ? 'number' : 'text'"
                  class="w-full bg-zinc-900 border border-zinc-600 rounded px-2 py-1 text-xs text-zinc-200"
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
