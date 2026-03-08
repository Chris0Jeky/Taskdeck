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
  <div class="td-ops">
    <header class="td-ops__hero">
      <div class="td-ops__hero-copy">
        <span class="td-ops__eyebrow">Advanced</span>
        <h1 class="td-page-title">Ops Console</h1>
        <p class="td-ops__subtitle">
          Ops Console is the operator surface for direct commands, endpoint probing, and low-level logs. Most users
          should stay in Review, Inbox, and Boards unless they are diagnosing a system-level problem.
        </p>
      </div>

      <div class="td-ops__hero-actions">
        <button class="td-btn td-btn--primary td-btn--sm" @click="openRoute('/workspace/review')">Open Review</button>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/settings/preferences')">
          Open Settings
        </button>
      </div>
    </header>

    <div class="td-role-context">
      <div class="td-role-context__title">Current role: {{ currentRoleLabel }}</div>
      <div class="td-role-context__body">
        Runnable templates:
        <span v-if="runnableTemplates.length > 0">{{ runnableTemplates.map(template => template.name).join(', ') }}</span>
        <span v-else>none</span>
      </div>
      <div v-if="restrictedTemplates.length > 0" class="td-role-context__hint">
        Restricted templates require a higher role. Open <strong>Workspace &gt; Settings</strong> to confirm your role,
        then ask an owner/admin for elevated access when needed.
      </div>
    </div>

    <div class="td-tabs">
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'cli' }]" @click="activateTab('cli')">CLI Runner</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'endpoints' }]" @click="activateTab('endpoints')">Endpoint Explorer</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'logs' }]" @click="activateTab('logs')">Logs</button>
    </div>

    <div v-if="activeTab === 'cli'" class="td-ops-panel">
      <div class="td-cli-toolbar">
        <InputAssistField
          v-model="selectedTemplate"
          :options="templateOptions"
          aria-label="Command template"
          placeholder="Select template"
          no-results-text="No matching templates."
        />
        <button class="td-btn td-btn--secondary td-btn--sm" @click="loadTemplates">Reload Templates</button>
      </div>
      <div v-if="selectedTemplateMeta" class="td-template-meta">
        <div class="td-template-meta__title">{{ selectedTemplateMeta.description }}</div>
        <div class="td-template-meta__details">
          Role: {{ selectedTemplateMeta.requiredRole }} |
          Access: {{ selectedTemplateIsRunnable ? 'Runnable for your role' : 'Restricted for your role' }} |
          Timeout: {{ selectedTemplateMeta.timeoutSeconds }}s |
          Params: {{ selectedTemplateMeta.acceptedParameters.length > 0 ? selectedTemplateMeta.acceptedParameters.join(', ') : 'None' }}
        </div>
      </div>

      <div class="td-form-group">
        <label class="td-label">Parameters (JSON object)</label>
        <textarea v-model="cliParameters" class="td-textarea" rows="3" placeholder='{"query":"board"}'></textarea>
      </div>

      <button class="td-btn td-btn--primary td-btn--sm" @click="handleCliRun" :disabled="cliRunning">
        {{ cliRunning ? 'Running...' : 'Run Template' }}
      </button>
      <div v-if="selectedTemplateMeta && !selectedTemplateIsRunnable" class="td-cli-warning">
        This template is restricted for {{ currentRoleLabel }}. You can still run it to see full permission guidance.
      </div>

      <div class="td-cli-output">
        <div v-if="cliOutput.length === 0" class="td-cli-placeholder">Command output will appear here.</div>
        <div v-for="(line, i) in cliOutput" :key="i" class="td-cli-line">{{ line }}</div>
      </div>

      <div v-if="lastRunId" class="td-run-ref">Last run ID: {{ lastRunId }}</div>
    </div>

    <div v-if="activeTab === 'endpoints'" class="td-ops-panel">
      <div class="td-endpoint-form">
        <select v-model="endpointMethod" class="td-input td-input--method">
          <option v-for="m in httpMethods" :key="m" :value="m">{{ m }}</option>
        </select>
        <input v-model="endpointPath" type="text" class="td-input td-input--path" placeholder="/boards" />
        <button class="td-btn td-btn--primary td-btn--sm" @click="handleEndpointSend" :disabled="endpointSending">
          {{ endpointSending ? 'Sending...' : 'Send' }}
        </button>
      </div>
      <div v-if="endpointMethod !== 'GET'" class="td-form-group">
        <label class="td-label">Request Body (JSON)</label>
        <textarea v-model="endpointBody" class="td-textarea" rows="4" placeholder='{"name":"example"}'></textarea>
      </div>
      <div v-if="endpointResponse !== null" class="td-response-panel">
        <div class="td-response-header">
          <span>Response</span>
          <span :class="['td-status-code', endpointStatus && endpointStatus < 400 ? 'td-status-code--ok' : 'td-status-code--err']">
            {{ endpointStatus }}
          </span>
        </div>
        <pre class="td-response-body">{{ endpointResponse }}</pre>
      </div>
    </div>

    <div v-if="activeTab === 'logs'" class="td-ops-panel">
      <div class="td-logs-toolbar">
        <select v-model="logLevel" class="td-input">
          <option value="all">All levels</option>
          <option value="Info">Info</option>
          <option value="Warning">Warning</option>
          <option value="Error">Error</option>
        </select>
        <input v-model="logSource" class="td-input" placeholder="Source filter (or all)" />
        <input v-model="logCorrelationId" class="td-input" placeholder="Correlation ID (optional)" />
        <button class="td-btn td-btn--secondary td-btn--sm" @click="loadLogs" :disabled="logLoading">Refresh</button>
        <label class="td-autorefresh">
          <input v-model="autoRefreshLogs" type="checkbox" />
          Auto refresh
        </label>
      </div>

      <div v-if="logLoading" class="td-loading">Loading logs...</div>
      <div v-else-if="logEntries.length === 0" class="td-empty td-empty--panel">
        <h2 class="td-empty__title">{{ logEmptyTitle }}</h2>
        <p class="td-empty__body">{{ logEmptyBody }}</p>
        <div class="td-empty__actions">
          <button class="td-btn td-btn--secondary td-btn--sm" @click="clearLogFilters">Clear Filters</button>
          <button class="td-btn td-btn--secondary td-btn--sm" @click="loadLogs">Refresh Logs</button>
          <button class="td-btn td-btn--primary td-btn--sm" @click="openRoute('/workspace/review')">Open Review</button>
        </div>
      </div>
      <div v-else class="td-log-list">
        <div v-for="entry in logEntries" :key="entry.id" class="td-log-entry">
          <span class="td-log-time">{{ new Date(entry.timestamp).toLocaleString() }}</span>
          <span class="td-log-level">{{ entry.level }}</span>
          <span class="td-log-source">{{ entry.source }}</span>
          <span class="td-log-message">{{ entry.message }}</span>
          <span v-if="entry.correlationId" class="td-log-correlation">{{ entry.correlationId }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-ops { max-width: 980px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; color: var(--td-text-primary); }
.td-ops__hero { display: flex; justify-content: space-between; gap: var(--td-space-6); align-items: flex-start; margin-bottom: var(--td-space-4); }
.td-ops__hero-copy { display: flex; flex-direction: column; gap: var(--td-space-2); max-width: 720px; }
.td-ops__eyebrow { font-size: var(--td-font-xs); font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--td-text-tertiary); }
.td-ops__subtitle { color: var(--td-text-secondary); line-height: 1.6; }
.td-ops__hero-actions,
.td-empty__actions { display: flex; flex-wrap: wrap; gap: var(--td-space-2); }
.td-role-context { margin-bottom: var(--td-space-4); padding: var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); background: var(--td-surface-secondary); }
.td-role-context__title { font-size: var(--td-font-sm); font-weight: 600; color: var(--td-text-primary); }
.td-role-context__body { margin-top: 2px; font-size: var(--td-font-xs); color: var(--td-text-secondary); }
.td-role-context__hint { margin-top: var(--td-space-1); font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-tabs { display: flex; gap: 0; margin-bottom: var(--td-space-4); border-bottom: 2px solid var(--td-border-default); }
.td-tab { padding: var(--td-space-2) var(--td-space-4); border: none; background: transparent; font-size: var(--td-font-sm); font-weight: 500; cursor: pointer; color: var(--td-text-secondary); border-bottom: 2px solid transparent; margin-bottom: -2px; }
.td-tab--active { color: var(--td-color-primary); border-bottom-color: var(--td-color-primary); }
.td-ops-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-4); }
.td-cli-toolbar { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-3); }
.td-template-meta { margin-bottom: var(--td-space-3); padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); background: var(--td-surface-secondary); }
.td-template-meta__title { font-size: var(--td-font-sm); color: var(--td-text-primary); }
.td-template-meta__details { margin-top: 2px; font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-form-group { display: flex; flex-direction: column; gap: var(--td-space-1); margin-bottom: var(--td-space-3); }
.td-label { font-size: var(--td-font-sm); font-weight: 500; color: var(--td-text-secondary); }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-textarea { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-family: monospace; font-size: var(--td-font-sm); resize: vertical; }
.td-textarea:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn--secondary:hover:not(:disabled) { background: var(--td-surface-hover); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-cli-warning { margin-top: var(--td-space-2); font-size: var(--td-font-xs); color: var(--td-color-warning); }
.td-cli-output { margin-top: var(--td-space-3); background: #0b1220; color: #dbe6ff; border-radius: var(--td-radius-md); padding: var(--td-space-3); font-family: monospace; min-height: 200px; max-height: 360px; overflow-y: auto; }
.td-cli-line { white-space: pre-wrap; line-height: 1.5; }
.td-cli-placeholder { color: #8fa3c8; }
.td-run-ref { margin-top: var(--td-space-2); color: var(--td-text-tertiary); font-size: var(--td-font-xs); }
.td-endpoint-form { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-3); }
.td-input--method { width: 110px; }
.td-input--path { flex: 1; }
.td-response-panel { margin-top: var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); overflow: hidden; }
.td-response-header { display: flex; justify-content: space-between; padding: var(--td-space-2) var(--td-space-3); background: var(--td-surface-secondary); font-size: var(--td-font-sm); font-weight: 500; }
.td-status-code { font-family: monospace; font-weight: 700; }
.td-status-code--ok { color: var(--td-color-success); }
.td-status-code--err { color: var(--td-color-error); }
.td-response-body { padding: var(--td-space-3); font-family: monospace; font-size: var(--td-font-sm); overflow-x: auto; margin: 0; background: var(--td-surface-primary); }
.td-logs-toolbar { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-3); flex-wrap: wrap; }
.td-autorefresh { display: inline-flex; align-items: center; gap: var(--td-space-1); font-size: var(--td-font-xs); color: var(--td-text-secondary); }
.td-loading, .td-empty { text-align: center; padding: var(--td-space-6); color: var(--td-text-secondary); }
.td-empty--panel { display: flex; flex-direction: column; gap: var(--td-space-2); align-items: center; justify-content: center; border: 1px dashed var(--td-border-default); border-radius: var(--td-radius-lg); background: var(--td-surface-secondary); }
.td-empty__title { margin: 0; color: var(--td-text-primary); font-size: var(--td-font-lg); }
.td-empty__body { margin: 0; max-width: 520px; line-height: 1.6; }
.td-log-list { display: flex; flex-direction: column; gap: var(--td-space-1); }
.td-log-entry { display: grid; grid-template-columns: 180px 90px 130px 1fr 220px; gap: var(--td-space-2); align-items: center; padding: var(--td-space-2); border-bottom: 1px solid var(--td-border-default); font-size: var(--td-font-xs); }
.td-log-time { color: var(--td-text-tertiary); font-family: monospace; }
.td-log-level { font-weight: 600; }
.td-log-source { color: var(--td-text-secondary); }
.td-log-message { color: var(--td-text-primary); }
.td-log-correlation { color: var(--td-text-tertiary); font-family: monospace; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

@media (max-width: 900px) {
  .td-ops__hero {
    flex-direction: column;
  }
}
</style>
