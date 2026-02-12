<script setup lang="ts">
import { ref } from 'vue'
import { exportImportApi } from '../api/exportImportApi'
import { getErrorDisplay } from '../composables/useErrorMapper'
import { useSessionStore } from '../store/sessionStore'
import { useToastStore } from '../store/toastStore'

const session = useSessionStore()
const toast = useToastStore()

const activeTab = ref<'export' | 'import'>('export')

const exportBoardId = ref('')
const exportResult = ref<string | null>(null)
const exporting = ref(false)

const importJson = ref('')
const importResult = ref<{ success: boolean; message: string; summary: string | null } | null>(null)
const importing = ref(false)
const importStep = ref(1)

async function handleExport() {
  if (!exportBoardId.value.trim()) {
    toast.warning('Please enter a board ID.')
    return
  }

  try {
    exporting.value = true
    const userId = session.requireUserId('export/import')
    const data = await exportImportApi.exportBoardJson(exportBoardId.value.trim(), userId)
    exportResult.value = JSON.stringify(data, null, 2)
    toast.success('Board exported successfully')
  } catch (err: unknown) {
    toast.error(getErrorDisplay(err, 'Export failed. Check board ID and permissions.').message)
    exportResult.value = null
  } finally {
    exporting.value = false
  }
}

function handleCopyExport() {
  if (!exportResult.value) return
  if (!navigator.clipboard?.writeText) {
    toast.error('Clipboard API is not available in this browser context')
    return
  }

  navigator.clipboard.writeText(exportResult.value).then(
    () => toast.success('Copied to clipboard'),
    () => toast.error('Failed to copy export payload')
  )
}

function handleDownloadExport() {
  if (!exportResult.value) return

  const blob = new Blob([exportResult.value], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `board-${exportBoardId.value}.json`
  anchor.click()
  URL.revokeObjectURL(url)
}

async function handleImport() {
  if (!importJson.value.trim()) {
    toast.warning('Please enter or paste JSON data.')
    return
  }

  try {
    importing.value = true
    const userId = session.requireUserId('export/import')
    const result = await exportImportApi.importBoardJson(importJson.value.trim(), userId)

    if (result.success) {
      importResult.value = {
        success: true,
        message: 'Board imported successfully',
        summary: `Columns: ${result.columnsImported}, Cards: ${result.cardsImported}, Labels: ${result.labelsImported}`,
      }
      toast.success('Board imported successfully')
    } else {
      importResult.value = {
        success: false,
        message: result.errorMessage ?? 'Import failed. Check your JSON data.',
        summary: null,
      }
      toast.error(importResult.value.message)
    }

    importStep.value = 3
  } catch (err: unknown) {
    const message = getErrorDisplay(err, 'Import failed. Check your JSON data.').message
    importResult.value = { success: false, message, summary: null }
    toast.error(message)
    importStep.value = 3
  } finally {
    importing.value = false
  }
}

function resetImport() {
  importJson.value = ''
  importResult.value = null
  importStep.value = 1
}
</script>

<template>
  <div class="td-export-import">
    <h1 class="td-page-title">Export / Import</h1>

    <div class="td-tabs">
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'export' }]" @click="activeTab = 'export'">Export</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'import' }]" @click="activeTab = 'import'">Import</button>
    </div>

    <div v-if="activeTab === 'export'" class="td-panel">
      <h2 class="td-section-title">Export Board</h2>
      <p class="td-section-desc">Export a board to JSON for backup or sharing.</p>

      <div class="td-export-form">
        <div class="td-form-group">
          <label for="export-board" class="td-label">Board ID</label>
          <input id="export-board" v-model="exportBoardId" type="text" class="td-input" placeholder="Enter board ID" />
        </div>
        <button class="td-btn td-btn--primary" :disabled="exporting" @click="handleExport">
          {{ exporting ? 'Exporting...' : 'Export JSON' }}
        </button>
      </div>

      <div v-if="exportResult" class="td-export-result">
        <div class="td-result-actions">
          <button class="td-btn td-btn--secondary td-btn--sm" @click="handleCopyExport">Copy</button>
          <button class="td-btn td-btn--secondary td-btn--sm" @click="handleDownloadExport">Download</button>
        </div>
        <pre class="td-json-viewer">{{ exportResult }}</pre>
      </div>
    </div>

    <div v-if="activeTab === 'import'" class="td-panel">
      <h2 class="td-section-title">Import Board</h2>

      <div v-if="importStep === 1">
        <p class="td-section-desc">Paste board JSON data to import.</p>
        <div class="td-form-group">
          <label class="td-label">Board JSON</label>
          <textarea v-model="importJson" class="td-textarea td-textarea--lg" rows="10" placeholder="Paste JSON here..."></textarea>
        </div>
        <button class="td-btn td-btn--primary" :disabled="!importJson.trim()" @click="importStep = 2">
          Validate & Preview
        </button>
      </div>

      <div v-if="importStep === 2">
        <p class="td-section-desc">Review the data before importing.</p>
        <pre class="td-json-viewer td-json-viewer--sm">{{ importJson.substring(0, 500) }}{{ importJson.length > 500 ? '...' : '' }}</pre>
        <div class="td-step-actions">
          <button class="td-btn td-btn--secondary" @click="importStep = 1">Back</button>
          <button class="td-btn td-btn--primary" :disabled="importing" @click="handleImport">
            {{ importing ? 'Importing...' : 'Import Board' }}
          </button>
        </div>
      </div>

      <div v-if="importStep === 3 && importResult">
        <div :class="['td-import-result', importResult.success ? 'td-import-result--success' : 'td-import-result--error']">
          <span>{{ importResult.success ? 'OK' : 'ERR' }}</span>
          <span>{{ importResult.message }}</span>
        </div>
        <p v-if="importResult.summary" class="td-import-summary">{{ importResult.summary }}</p>
        <button class="td-btn td-btn--secondary" @click="resetImport">Import Another</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-export-import { max-width: 800px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-tabs { display: flex; gap: 0; margin-bottom: var(--td-space-4); border-bottom: 2px solid var(--td-border-default); }
.td-tab { padding: var(--td-space-2) var(--td-space-4); border: none; background: transparent; font-size: var(--td-font-sm); font-weight: 500; cursor: pointer; color: var(--td-text-secondary); border-bottom: 2px solid transparent; margin-bottom: -2px; }
.td-tab--active { color: var(--td-color-primary); border-bottom-color: var(--td-color-primary); }
.td-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-6); }
.td-section-title { font-size: var(--td-font-lg); font-weight: 600; margin-bottom: var(--td-space-2); color: var(--td-text-primary); }
.td-section-desc { font-size: var(--td-font-sm); color: var(--td-text-secondary); margin-bottom: var(--td-space-4); }
.td-export-form { display: flex; gap: var(--td-space-3); align-items: flex-end; margin-bottom: var(--td-space-4); }
.td-form-group { display: flex; flex-direction: column; gap: var(--td-space-1); flex: 1; }
.td-label { font-size: var(--td-font-sm); font-weight: 500; color: var(--td-text-secondary); }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-textarea { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-family: monospace; font-size: var(--td-font-sm); resize: vertical; width: 100%; box-sizing: border-box; }
.td-textarea:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-textarea--lg { min-height: 200px; }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn--secondary:hover { background: var(--td-surface-hover); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-export-result { margin-top: var(--td-space-4); }
.td-result-actions { display: flex; gap: var(--td-space-2); margin-bottom: var(--td-space-2); }
.td-json-viewer { background: var(--td-text-primary); color: #e2e8f0; padding: var(--td-space-4); border-radius: var(--td-radius-md); font-family: monospace; font-size: var(--td-font-sm); overflow-x: auto; max-height: 400px; overflow-y: auto; white-space: pre-wrap; margin: 0; }
.td-json-viewer--sm { max-height: 200px; }
.td-step-actions { display: flex; gap: var(--td-space-3); margin-top: var(--td-space-4); }
.td-import-result { display: flex; align-items: center; gap: var(--td-space-3); padding: var(--td-space-4); border-radius: var(--td-radius-md); margin-bottom: var(--td-space-4); font-size: var(--td-font-sm); }
.td-import-result--success { background: var(--td-color-success-light); color: var(--td-color-success); }
.td-import-result--error { background: var(--td-color-error-light); color: var(--td-color-error); }
.td-import-summary { margin-bottom: var(--td-space-4); color: var(--td-text-secondary); font-size: var(--td-font-sm); }
</style>
