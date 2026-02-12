<script setup lang="ts">
import { ref } from 'vue'

const activeTab = ref<'cli' | 'endpoints' | 'logs'>('cli')

// CLI state
const cliCommand = ref('')
const cliOutput = ref<string[]>([])
const cliRunning = ref(false)

// Endpoint explorer state
const endpointMethod = ref('GET')
const endpointPath = ref('')
const endpointBody = ref('')
const endpointResponse = ref<string | null>(null)
const endpointStatus = ref<number | null>(null)

// Logs state
const logLevel = ref('all')
const logSource = ref('all')
const logEntries = ref<Array<{ timestamp: string; level: string; source: string; message: string }>>([])

function handleCliRun() {
  if (!cliCommand.value.trim()) return
  cliRunning.value = true
  cliOutput.value.push(`$ ${cliCommand.value}`)
  cliOutput.value.push('CLI bridge endpoints not yet implemented. Command would be sent to POST /api/ops/cli/run')
  cliOutput.value.push('')
  cliRunning.value = false
}

function handleEndpointSend() {
  endpointResponse.value = JSON.stringify({
    note: 'Endpoint explorer will execute requests via the API when fully connected.',
    method: endpointMethod.value,
    path: endpointPath.value,
  }, null, 2)
  endpointStatus.value = 200
}

const httpMethods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']

const commandTemplates = [
  { domain: 'boards', commands: ['boards list', 'boards create', 'boards update'] },
  { domain: 'cards', commands: ['cards list', 'cards add', 'cards move'] },
  { domain: 'columns', commands: ['columns list', 'columns create'] },
]
</script>

<template>
  <div class="td-ops">
    <h1 class="td-page-title">Ops Console</h1>

    <!-- Tab Bar -->
    <div class="td-tabs">
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'cli' }]" @click="activeTab = 'cli'">CLI Runner</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'endpoints' }]" @click="activeTab = 'endpoints'">Endpoint Explorer</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'logs' }]" @click="activeTab = 'logs'">Logs</button>
    </div>

    <!-- CLI Tab -->
    <div v-if="activeTab === 'cli'" class="td-ops-panel">
      <div class="td-cli-templates">
        <h3 class="td-sub-title">Command Templates</h3>
        <div v-for="group in commandTemplates" :key="group.domain" class="td-template-group">
          <span class="td-template-domain">{{ group.domain }}</span>
          <button
            v-for="cmd in group.commands"
            :key="cmd"
            class="td-template-btn"
            @click="cliCommand = cmd"
          >{{ cmd }}</button>
        </div>
      </div>
      <div class="td-cli-input-row">
        <span class="td-cli-prompt">$</span>
        <input
          v-model="cliCommand"
          type="text"
          class="td-cli-input"
          placeholder="Enter command..."
          @keydown.enter="handleCliRun"
          :disabled="cliRunning"
        />
        <button class="td-btn td-btn--primary td-btn--sm" @click="handleCliRun" :disabled="cliRunning">
          {{ cliRunning ? 'Running...' : 'Run' }}
        </button>
      </div>
      <div class="td-cli-output">
        <div v-for="(line, i) in cliOutput" :key="i" class="td-cli-line">{{ line }}</div>
        <div v-if="cliOutput.length === 0" class="td-cli-placeholder">Output will appear here...</div>
      </div>
    </div>

    <!-- Endpoint Explorer Tab -->
    <div v-if="activeTab === 'endpoints'" class="td-ops-panel">
      <div class="td-endpoint-form">
        <select v-model="endpointMethod" class="td-input td-input--method">
          <option v-for="m in httpMethods" :key="m" :value="m">{{ m }}</option>
        </select>
        <input v-model="endpointPath" type="text" class="td-input td-input--path" placeholder="/api/boards" />
        <button class="td-btn td-btn--primary td-btn--sm" @click="handleEndpointSend">Send</button>
      </div>
      <div v-if="endpointMethod !== 'GET'" class="td-form-group">
        <label class="td-label">Request Body (JSON)</label>
        <textarea v-model="endpointBody" class="td-textarea" rows="4" placeholder='{"name": "test"}'></textarea>
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

    <!-- Logs Tab -->
    <div v-if="activeTab === 'logs'" class="td-ops-panel">
      <div class="td-logs-toolbar">
        <select v-model="logLevel" class="td-input">
          <option value="all">All Levels</option>
          <option value="info">Info</option>
          <option value="warning">Warning</option>
          <option value="error">Error</option>
        </select>
        <select v-model="logSource" class="td-input">
          <option value="all">All Sources</option>
          <option value="frontend">Frontend</option>
          <option value="api">API</option>
          <option value="queue">Queue</option>
          <option value="automation">Automation</option>
        </select>
      </div>
      <div class="td-logs-container">
        <div v-if="logEntries.length === 0" class="td-placeholder">
          <div class="td-placeholder__icon">📝</div>
          <h3>Logs</h3>
          <p>Log streaming will be available when backend log endpoints (GET /api/logs, GET /api/logs/stream) are implemented.</p>
          <p class="td-placeholder__detail">This panel will support filtering by level, source, time range, and correlation ID.</p>
        </div>
        <div v-for="(entry, i) in logEntries" :key="i" class="td-log-entry">
          <span class="td-log-time">{{ entry.timestamp }}</span>
          <span :class="['td-log-level', `td-log-level--${entry.level}`]">{{ entry.level }}</span>
          <span class="td-log-source">{{ entry.source }}</span>
          <span class="td-log-message">{{ entry.message }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-ops { max-width: 960px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-tabs { display: flex; gap: 0; margin-bottom: var(--td-space-4); border-bottom: 2px solid var(--td-border-default); }
.td-tab { padding: var(--td-space-2) var(--td-space-4); border: none; background: transparent; font-size: var(--td-font-sm); font-weight: 500; cursor: pointer; color: var(--td-text-secondary); border-bottom: 2px solid transparent; margin-bottom: -2px; }
.td-tab--active { color: var(--td-color-primary); border-bottom-color: var(--td-color-primary); }
.td-ops-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-4); }
.td-sub-title { font-size: var(--td-font-sm); font-weight: 600; margin-bottom: var(--td-space-2); color: var(--td-text-secondary); }
.td-cli-templates { margin-bottom: var(--td-space-4); }
.td-template-group { display: flex; align-items: center; gap: var(--td-space-2); margin-bottom: var(--td-space-2); }
.td-template-domain { font-size: var(--td-font-xs); font-weight: 600; color: var(--td-text-tertiary); text-transform: uppercase; min-width: 70px; }
.td-template-btn { padding: var(--td-space-1) var(--td-space-2); border: 1px solid var(--td-border-default); background: var(--td-surface-secondary); border-radius: var(--td-radius-sm); font-size: var(--td-font-xs); cursor: pointer; font-family: monospace; }
.td-template-btn:hover { background: var(--td-surface-hover); }
.td-cli-input-row { display: flex; align-items: center; gap: var(--td-space-2); margin-bottom: var(--td-space-3); }
.td-cli-prompt { font-family: monospace; font-weight: 700; color: var(--td-color-success); }
.td-cli-input { flex: 1; padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-family: monospace; }
.td-cli-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-cli-output { background: var(--td-text-primary); color: #e2e8f0; border-radius: var(--td-radius-md); padding: var(--td-space-4); font-family: monospace; font-size: var(--td-font-sm); min-height: 200px; max-height: 400px; overflow-y: auto; }
.td-cli-line { white-space: pre-wrap; line-height: 1.6; }
.td-cli-placeholder { color: var(--td-text-tertiary); }
.td-endpoint-form { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-3); }
.td-input--method { width: 100px; }
.td-input--path { flex: 1; }
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
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-response-panel { margin-top: var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); overflow: hidden; }
.td-response-header { display: flex; justify-content: space-between; padding: var(--td-space-2) var(--td-space-3); background: var(--td-surface-secondary); font-size: var(--td-font-sm); font-weight: 500; }
.td-status-code { font-family: monospace; font-weight: 700; }
.td-status-code--ok { color: var(--td-color-success); }
.td-status-code--err { color: var(--td-color-error); }
.td-response-body { padding: var(--td-space-3); font-family: monospace; font-size: var(--td-font-sm); overflow-x: auto; margin: 0; background: var(--td-surface-primary); }
.td-logs-toolbar { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-3); }
.td-logs-container { min-height: 200px; }
.td-placeholder { text-align: center; padding: var(--td-space-8); }
.td-placeholder__icon { font-size: 3rem; margin-bottom: var(--td-space-4); }
.td-placeholder h3 { font-size: var(--td-font-lg); font-weight: 600; margin-bottom: var(--td-space-2); }
.td-placeholder p { font-size: var(--td-font-sm); color: var(--td-text-secondary); margin-bottom: var(--td-space-2); }
.td-placeholder__detail { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-log-entry { display: flex; gap: var(--td-space-3); padding: var(--td-space-2); border-bottom: 1px solid var(--td-border-default); font-size: var(--td-font-sm); }
.td-log-time { color: var(--td-text-tertiary); font-family: monospace; font-size: var(--td-font-xs); min-width: 140px; }
.td-log-level { font-weight: 600; text-transform: uppercase; font-size: var(--td-font-xs); min-width: 60px; }
.td-log-level--info { color: var(--td-color-info); }
.td-log-level--warning { color: var(--td-color-warning); }
.td-log-level--error { color: var(--td-color-error); }
.td-log-source { color: var(--td-text-tertiary); font-size: var(--td-font-xs); min-width: 80px; }
.td-log-message { flex: 1; color: var(--td-text-primary); }
</style>
