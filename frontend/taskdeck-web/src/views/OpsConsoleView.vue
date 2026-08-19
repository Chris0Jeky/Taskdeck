<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import http from '../api/http'
import { opsApi } from '../api/opsApi'
import { useSessionStore } from '../store/sessionStore'
import { useToastStore } from '../store/toastStore'
import type { CommandTemplate, LogEntry } from '../types/ops'
import { normalizeCommandRunStatus } from '../utils/ops'
import { getErrorDisplay } from '../composables/useErrorMapper'
import InputAssistField from '../components/common/InputAssistField.vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import { buildInputAssistOptions } from '../utils/inputAssist'
import { normalizeBoardRole, toBoardRoleValue } from '../utils/roles'

const route = useRoute()
const router = useRouter()
const toast = useToastStore()
const session = useSessionStore()

const activeTab = ref<'cli' | 'endpoints' | 'logs'>('cli')

const templates = ref<CommandTemplate[]>([])
const selectedTemplate = ref('')
const cliParameters = ref('{}')
const cliOutput = ref<string[]>([])
const cliRunning = ref(false)
const lastRunId = ref<string | null>(null)

const endpointMethod = ref('GET')
const endpointPath = ref('/boards')
const endpointBody = ref('')
const endpointResponse = ref<string | null>(null)
const endpointStatus = ref<number | null>(null)
const endpointSending = ref(false)

const logLevel = ref('all')
const logSource = ref('all')
const logCorrelationId = ref('')
const autoRefreshLogs = ref(false)
const logLoading = ref(false)
const logEntries = ref<LogEntry[]>([])
let logRefreshTimer: ReturnType<typeof setInterval> | null = null

const httpMethods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']

const currentRoleLabel = computed(() => (
  session.defaultRole === null
    ? 'Unknown'
    : normalizeBoardRole(session.defaultRole)
))

const currentRoleValue = computed(() => (
  session.defaultRole === null ? 3 : session.defaultRole
))

function isTemplateRunnableForCurrentRole(template: CommandTemplate): boolean {
  return currentRoleValue.value <= toBoardRoleValue(template.requiredRole)
}

const runnableTemplates = computed(() => (
  templates.value.filter((template) => isTemplateRunnableForCurrentRole(template))
))

const restrictedTemplates = computed(() => (
  templates.value.filter((template) => !isTemplateRunnableForCurrentRole(template))
))

const templateOptions = computed(() => buildInputAssistOptions(
  templates.value.map((template) => ({
    value: template.name,
    label: template.name,
    helperText: `${template.requiredRole} role ${isTemplateRunnableForCurrentRole(template) ? '| runnable' : '| restricted'}`,
    keywords: [template.description, ...template.acceptedParameters],
  }))
))

const selectedTemplateMeta = computed(() => {
  return templates.value.find((template) => template.name === selectedTemplate.value) ?? null
})

const selectedTemplateIsRunnable = computed(() => {
  if (!selectedTemplateMeta.value) {
    return false
  }

  return isTemplateRunnableForCurrentRole(selectedTemplateMeta.value)
})

const logEmptyTitle = computed(() => {
  if (logCorrelationId.value.trim()) {
    return 'No logs for this correlation ID'
  }

  return 'No logs match the current filters'
})

const logEmptyBody = computed(() => {
  if (logCorrelationId.value.trim()) {
    return 'Check the correlation ID, clear the filter, or refresh after the underlying job runs again.'
  }

  return 'Try a broader level or source filter, or head back to Review if you were tracing a proposal decision.'
})

function stopLogAutoRefresh() {
  if (logRefreshTimer !== null) {
    clearInterval(logRefreshTimer)
    logRefreshTimer = null
  }
}

function startLogAutoRefresh() {
  stopLogAutoRefresh()
  if (!autoRefreshLogs.value) {
    return
  }

  logRefreshTimer = setInterval(() => {
    void loadLogs()
  }, 5000)
}

async function loadTemplates() {
  try {
    templates.value = await opsApi.getTemplates()
    if (!selectedTemplate.value && templates.value.length > 0) {
      selectedTemplate.value = (runnableTemplates.value[0] ?? templates.value[0])!.name
    }
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load command templates').message)
  }
}

function parseCliParameters(): Record<string, string> {
  const trimmed = cliParameters.value.trim()
  if (!trimmed) {
    return {}
  }

  const parsed = JSON.parse(trimmed) as Record<string, unknown>
  const asStringMap: Record<string, string> = {}
  for (const [key, value] of Object.entries(parsed)) {
    asStringMap[key] = String(value)
  }

  return asStringMap
}

async function handleCliRun() {
  if (!selectedTemplateMeta.value) {
    toast.error('Select a valid command template first')
    return
  }

  cliRunning.value = true
  try {
    const parameters = parseCliParameters()
    cliOutput.value.push(`> ${selectedTemplate.value}`)

    const run = await opsApi.runCommand({
      templateName: selectedTemplateMeta.value.name,
      parameters: Object.keys(parameters).length === 0 ? undefined : parameters,
    })

    lastRunId.value = run.id
    cliOutput.value.push(`Run ${run.id} status: ${normalizeCommandRunStatus(run.status)}`)
    if (run.outputPreview) {
      cliOutput.value.push(run.outputPreview)
    }
    if (run.errorMessage) {
      cliOutput.value.push(`Error: ${run.errorMessage}`)
    }

    const logs = await opsApi.getRunLogs(run.id)
    if (logs.length > 0) {
      cliOutput.value.push('--- logs ---')
      logs.forEach(log => cliOutput.value.push(`[${log.level}] ${log.source}: ${log.message}`))
    }

    cliOutput.value.push('')
  } catch (e: unknown) {
    const msg = getErrorDisplay(e, 'Failed to run template').message
    cliOutput.value.push(`Error: ${msg}`)
    cliOutput.value.push('')
    toast.error(msg)
  } finally {
    cliRunning.value = false
  }
}

function normalizeEndpointPath(path: string): string {
  const trimmed = path.trim()
  if (trimmed.startsWith('/api/')) {
    return trimmed.replace('/api', '')
  }

  return trimmed.startsWith('/') ? trimmed : `/${trimmed}`
}

async function handleEndpointSend() {
  endpointSending.value = true
  endpointResponse.value = null
  endpointStatus.value = null

  try {
    const method = endpointMethod.value.toUpperCase()
    const url = normalizeEndpointPath(endpointPath.value)
    const body = method === 'GET' || method === 'DELETE'
      ? undefined
      : (endpointBody.value.trim() ? JSON.parse(endpointBody.value) : {})

    const response = await http.request({
      method,
      url,
      data: body,
    })

    endpointStatus.value = response.status
    endpointResponse.value = JSON.stringify(response.data, null, 2)
  } catch (e: unknown) {
    const display = getErrorDisplay(e, 'Endpoint request failed')
    const maybeResponse = (typeof e === 'object' && e !== null)
      ? (e as { response?: { status?: number; data?: unknown } }).response
      : undefined

    endpointStatus.value = maybeResponse?.status ?? 500
    endpointResponse.value = JSON.stringify(maybeResponse?.data ?? { message: display.message }, null, 2)
  } finally {
    endpointSending.value = false
  }
}

async function loadLogs() {
  try {
    logLoading.value = true

    if (logCorrelationId.value.trim()) {
      logEntries.value = await opsApi.getCorrelationLogs(logCorrelationId.value.trim())
      return
    }

    logEntries.value = await opsApi.queryLogs({
      level: logLevel.value === 'all' ? undefined : logLevel.value,
      source: logSource.value === 'all' ? undefined : logSource.value,
      limit: 200,
    })
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load logs').message)
  } finally {
    logLoading.value = false
  }
}

function routeForTab(tab: 'cli' | 'endpoints' | 'logs'): string {
  if (tab === 'endpoints') {
    return '/workspace/ops/endpoints'
  }

  if (tab === 'logs') {
    return '/workspace/ops/logs'
  }

  return '/workspace/ops/cli'
}

function syncActiveTabFromRoute() {
  if (route.name === 'workspace-ops-endpoints') {
    activeTab.value = 'endpoints'
    return
  }

  if (route.name === 'workspace-ops-logs') {
    activeTab.value = 'logs'
    return
  }

  activeTab.value = 'cli'
}

function activateTab(tab: 'cli' | 'endpoints' | 'logs') {
  if (activeTab.value !== tab) {
    activeTab.value = tab
  }

  const nextPath = routeForTab(tab)
  if (route.path !== nextPath) {
    void router.push(nextPath)
  }
}

function clearLogFilters() {
  logLevel.value = 'all'
  logSource.value = 'all'
  logCorrelationId.value = ''
  void loadLogs()
}

onMounted(() => {
  syncActiveTabFromRoute()
  void loadTemplates()
})

watch(autoRefreshLogs, () => {
  startLogAutoRefresh()
})

watch(activeTab, (tab) => {
  if (tab === 'logs') {
    void loadLogs()
    startLogAutoRefresh()
  } else {
    stopLogAutoRefresh()
  }
})

watch(
  () => route.name,
  () => {
    syncActiveTabFromRoute()
  },
)

onBeforeUnmount(() => {
  stopLogAutoRefresh()
})

function openRoute(path: string) {
  void router.push(path)
}
</script>

<template>
  <div class="paper-ops">
    <header class="paper-ops__hero">
      <div class="paper-ops__hero-copy">
        <span class="tk-eyebrow paper-ops__eyebrow">Advanced</span>
        <h1 class="tk-h2 paper-ops__title">Ops Console</h1>
        <p class="tk-lede paper-ops__subtitle">
          Ops Console is the operator surface for direct commands, endpoint probing, and low-level logs. Most users
          should stay in Review, Inbox, and Boards unless they are diagnosing a system-level problem.
        </p>
      </div>

      <div class="paper-ops__hero-actions">
        <PaperHLBtn variant="ember" @click="openRoute('/workspace/review')">Open Review</PaperHLBtn>
        <PaperHLBtn @click="openRoute('/workspace/settings/preferences')">
          Open Settings
        </PaperHLBtn>
      </div>
    </header>

    <div class="paper-ops__role-context">
      <div class="paper-ops__role-title">Current role: {{ currentRoleLabel }}</div>
      <div class="paper-ops__role-body">
        Runnable templates:
        <span v-if="runnableTemplates.length > 0">{{ runnableTemplates.map(template => template.name).join(', ') }}</span>
        <span v-else>none</span>
      </div>
      <div v-if="restrictedTemplates.length > 0" class="paper-ops__role-hint">
        Restricted templates require a higher role. Open <strong>Workspace &gt; Settings</strong> to confirm your role,
        then ask an owner/admin for elevated access when needed.
      </div>
    </div>

    <div class="paper-ops__tabs">
      <button :class="['paper-ops__tab', { 'paper-ops__tab--active': activeTab === 'cli' }]" @click="activateTab('cli')">CLI Runner</button>
      <button :class="['paper-ops__tab', { 'paper-ops__tab--active': activeTab === 'endpoints' }]" @click="activateTab('endpoints')">Endpoint Explorer</button>
      <button :class="['paper-ops__tab', { 'paper-ops__tab--active': activeTab === 'logs' }]" @click="activateTab('logs')">Logs</button>
    </div>

    <div v-if="activeTab === 'cli'" class="paper-ops__panel">
      <div class="paper-ops__cli-toolbar">
        <InputAssistField
          v-model="selectedTemplate"
          :options="templateOptions"
          aria-label="Command template"
          placeholder="Select template"
          no-results-text="No matching templates."
        />
        <PaperHLBtn @click="loadTemplates">Reload Templates</PaperHLBtn>
      </div>
      <div v-if="selectedTemplateMeta" class="paper-ops__template-meta">
        <div class="paper-ops__template-title">{{ selectedTemplateMeta.description }}</div>
        <div class="paper-ops__template-details">
          Role: {{ selectedTemplateMeta.requiredRole }} |
          Access: {{ selectedTemplateIsRunnable ? 'Runnable for your role' : 'Restricted for your role' }} |
          Timeout: {{ selectedTemplateMeta.timeoutSeconds }}s |
          Params: {{ selectedTemplateMeta.acceptedParameters.length > 0 ? selectedTemplateMeta.acceptedParameters.join(', ') : 'None' }}
        </div>
      </div>

      <div class="paper-ops__form-group">
        <label for="cli-parameters" class="paper-ops__label">Parameters (JSON object)</label>
        <textarea id="cli-parameters" v-model="cliParameters" class="paper-ops__textarea" rows="3" placeholder='{"query":"board"}'></textarea>
      </div>

      <PaperHLBtn variant="ember" :disabled="cliRunning" @click="handleCliRun">
        {{ cliRunning ? 'Running...' : 'Run Template' }}
      </PaperHLBtn>
      <div v-if="selectedTemplateMeta && !selectedTemplateIsRunnable" class="paper-ops__cli-warning">
        This template is restricted for {{ currentRoleLabel }}. You can still run it to see full permission guidance.
      </div>

      <div class="paper-ops__cli-output">
        <div v-if="cliOutput.length === 0" class="paper-ops__cli-placeholder">Command output will appear here.</div>
        <div v-for="(line, i) in cliOutput" :key="i" class="paper-ops__cli-line">{{ line }}</div>
      </div>

      <div v-if="lastRunId" class="paper-ops__run-ref">Last run ID: {{ lastRunId }}</div>
    </div>

    <div v-if="activeTab === 'endpoints'" class="paper-ops__panel">
      <div class="paper-ops__endpoint-form">
        <select v-model="endpointMethod" class="paper-ops__input paper-ops__input--method" aria-label="HTTP method">
          <option v-for="m in httpMethods" :key="m" :value="m">{{ m }}</option>
        </select>
        <input v-model="endpointPath" type="text" aria-label="Request path" class="paper-ops__input paper-ops__input--path" placeholder="/boards" />
        <PaperHLBtn variant="ember" :disabled="endpointSending" @click="handleEndpointSend">
          {{ endpointSending ? 'Sending...' : 'Send' }}
        </PaperHLBtn>
      </div>
      <div v-if="endpointMethod !== 'GET'" class="paper-ops__form-group">
        <label for="endpoint-body" class="paper-ops__label">Request Body (JSON)</label>
        <textarea id="endpoint-body" v-model="endpointBody" class="paper-ops__textarea" rows="4" placeholder='{"name":"example"}'></textarea>
      </div>
      <div v-if="endpointResponse !== null" class="paper-ops__response-panel">
        <div class="paper-ops__response-header">
          <span>Response</span>
          <span :class="['paper-ops__status-code', endpointStatus && endpointStatus < 400 ? 'paper-ops__status-code--ok' : 'paper-ops__status-code--err']">
            {{ endpointStatus }}
          </span>
        </div>
        <pre class="paper-ops__response-body">{{ endpointResponse }}</pre>
      </div>
    </div>

    <div v-if="activeTab === 'logs'" class="paper-ops__panel">
      <div class="paper-ops__logs-toolbar">
        <select v-model="logLevel" class="paper-ops__input" aria-label="Log level filter">
          <option value="all">All levels</option>
          <option value="Info">Info</option>
          <option value="Warning">Warning</option>
          <option value="Error">Error</option>
        </select>
        <input v-model="logSource" class="paper-ops__input" aria-label="Source filter" placeholder="Source filter (or all)" />
        <input v-model="logCorrelationId" class="paper-ops__input" aria-label="Correlation ID" placeholder="Correlation ID (optional)" />
        <PaperHLBtn :disabled="logLoading" @click="loadLogs">Refresh</PaperHLBtn>
        <label class="paper-ops__autorefresh">
          <input v-model="autoRefreshLogs" type="checkbox" />
          Auto refresh
        </label>
      </div>

      <div v-if="logLoading" class="paper-ops__loading">Loading logs...</div>
      <div v-else-if="logEntries.length === 0" class="paper-ops__empty paper-ops__empty--panel">
        <h2 class="tk-h3 paper-ops__empty-title">{{ logEmptyTitle }}</h2>
        <p class="paper-ops__empty-body">{{ logEmptyBody }}</p>
        <div class="paper-ops__empty-actions">
          <PaperHLBtn @click="clearLogFilters">Clear Filters</PaperHLBtn>
          <PaperHLBtn @click="loadLogs">Refresh Logs</PaperHLBtn>
          <PaperHLBtn variant="ember" @click="openRoute('/workspace/review')">Open Review</PaperHLBtn>
        </div>
      </div>
      <div v-else class="paper-ops__log-list">
        <div v-for="entry in logEntries" :key="entry.id" class="paper-ops__log-entry">
          <span class="paper-ops__log-time">{{ new Date(entry.timestamp).toLocaleString() }}</span>
          <span class="paper-ops__log-level">{{ entry.level }}</span>
          <span class="paper-ops__log-source">{{ entry.source }}</span>
          <span class="paper-ops__log-message">{{ entry.message }}</span>
          <span v-if="entry.correlationId" class="paper-ops__log-correlation">{{ entry.correlationId }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — OpsConsoleView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell.  The CLI output pane
   was a hard-coded blue-black terminal (#0b1220 / #dbe6ff); it is now a
   monospace ledger on the Paper substrate so it inverts correctly at night. */

.paper-ops {
  max-width: 980px;
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.paper-ops__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--s-6, 24px);
  align-items: flex-start;
  margin-bottom: var(--s-4, 16px);
}

.paper-ops__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  max-width: 720px;
}

.paper-ops__eyebrow { color: var(--ember, #a8421f); }
.paper-ops__title { margin: 0; font-size: var(--t-h2, 32px); }
.paper-ops__subtitle { margin: 0; color: var(--ink-2, #3a352d); }

.paper-ops__hero-actions,
.paper-ops__empty-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2, 8px);
}

.paper-ops__role-context {
  margin-bottom: var(--s-4, 16px);
  padding: var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
}

.paper-ops__role-title { font-size: var(--t-md, 13.5px); font-weight: 600; color: var(--ink-deep, #0a0908); }
.paper-ops__role-body { margin-top: 2px; font-size: var(--t-xs, 10.5px); color: var(--ink-2, #3a352d); }
.paper-ops__role-hint { margin-top: var(--s-1, 4px); font-size: var(--t-xs, 10.5px); color: var(--mute, #6c6557); }

.paper-ops__tabs {
  display: flex;
  gap: 0;
  margin-bottom: var(--s-4, 16px);
  border-bottom: 2px solid var(--line, #d8d0bf);
}

.paper-ops__tab {
  padding: var(--s-2, 8px) var(--s-4, 16px);
  border: none;
  background: transparent;
  font-family: inherit;
  font-size: var(--t-md, 13.5px);
  font-weight: 500;
  cursor: pointer;
  color: var(--mute, #6c6557);
  border-bottom: 2px solid transparent;
  margin-bottom: -2px;
  transition: color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-ops__tab:hover { color: var(--ink, #1a1814); }

.paper-ops__tab--active {
  color: var(--ember, #a8421f);
  border-bottom-color: var(--ember, #a8421f);
}

.paper-ops__panel {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-4, 16px);
}

.paper-ops__cli-toolbar {
  display: flex;
  gap: var(--s-2, 8px);
  margin-bottom: var(--s-3, 12px);
}

.paper-ops__template-meta {
  margin-bottom: var(--s-3, 12px);
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
}

.paper-ops__template-title { font-size: var(--t-md, 13.5px); color: var(--ink-deep, #0a0908); }

.paper-ops__template-details {
  margin-top: 2px;
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
}

.paper-ops__form-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
  margin-bottom: var(--s-3, 12px);
}

.paper-ops__label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #6c6557);
}

.paper-ops__input,
.paper-ops__textarea {
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-ops__textarea {
  font-family: var(--mono, ui-monospace, monospace);
  resize: vertical;
}

.paper-ops__input:focus,
.paper-ops__textarea:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-ops__cli-warning {
  margin-top: var(--s-2, 8px);
  font-size: var(--t-xs, 10.5px);
  color: var(--overdue, #8c4a26);
}

.paper-ops__cli-output {
  margin-top: var(--s-3, 12px);
  background: var(--paper-2, #ebe5d8);
  color: var(--ink, #1a1814);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  padding: var(--s-3, 12px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
  min-height: 200px;
  max-height: 360px;
  overflow-y: auto;
}

.paper-ops__cli-line { white-space: pre-wrap; line-height: 1.5; }
.paper-ops__cli-placeholder { color: var(--mute, #6c6557); }

.paper-ops__run-ref {
  margin-top: var(--s-2, 8px);
  font-family: var(--mono, ui-monospace, monospace);
  color: var(--mute, #6c6557);
  font-size: var(--t-xs, 10.5px);
}

.paper-ops__endpoint-form {
  display: flex;
  gap: var(--s-2, 8px);
  margin-bottom: var(--s-3, 12px);
}

.paper-ops__input--method { width: 110px; }
.paper-ops__input--path { flex: 1; }

.paper-ops__response-panel {
  margin-top: var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  overflow: hidden;
}

.paper-ops__response-header {
  display: flex;
  justify-content: space-between;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  background: var(--paper, #f3eee5);
  font-size: var(--t-md, 13.5px);
  font-weight: 600;
}

.paper-ops__status-code { font-family: var(--mono, ui-monospace, monospace); font-weight: 700; }
.paper-ops__status-code--ok { color: var(--applied, #4a6b3f); }
.paper-ops__status-code--err { color: var(--ember-deep, #7a2e15); }

.paper-ops__response-body {
  padding: var(--s-3, 12px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
  overflow-x: auto;
  margin: 0;
  background: var(--paper-card, #fbf7ee);
}

.paper-ops__logs-toolbar {
  display: flex;
  gap: var(--s-2, 8px);
  margin-bottom: var(--s-3, 12px);
  flex-wrap: wrap;
}

.paper-ops__autorefresh {
  display: inline-flex;
  align-items: center;
  gap: var(--s-1, 4px);
  font-size: var(--t-xs, 10.5px);
  color: var(--ink-2, #3a352d);
}

.paper-ops__loading,
.paper-ops__empty {
  text-align: center;
  padding: var(--s-6, 24px);
  color: var(--mute, #6c6557);
}

.paper-ops__empty--panel {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  align-items: center;
  justify-content: center;
  border: 1px dashed var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  background: var(--paper, #f3eee5);
}

.paper-ops__empty-title { margin: 0; font-size: var(--t-lg, 18px); }
.paper-ops__empty-body { margin: 0; max-width: 520px; line-height: 1.6; }

.paper-ops__log-list {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-ops__log-entry {
  display: grid;
  grid-template-columns: 180px 90px 130px 1fr 220px;
  gap: var(--s-2, 8px);
  align-items: center;
  padding: var(--s-2, 8px);
  border-bottom: 1px solid var(--line-soft, #e3dcc9);
  font-size: var(--t-xs, 10.5px);
}

.paper-ops__log-time { color: var(--mute, #6c6557); font-family: var(--mono, ui-monospace, monospace); }
.paper-ops__log-level { font-weight: 700; color: var(--ink-deep, #0a0908); }
.paper-ops__log-source { color: var(--ink-2, #3a352d); }
.paper-ops__log-message { color: var(--ink, #1a1814); }

.paper-ops__log-correlation {
  color: var(--mute, #6c6557);
  font-family: var(--mono, ui-monospace, monospace);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

@media (max-width: 900px) {
  .paper-ops__hero {
    flex-direction: column;
  }
}
</style>
