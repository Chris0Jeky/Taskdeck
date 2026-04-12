<script setup lang="ts">
import { ref } from 'vue'
import { exportImportApi } from '../api/exportImportApi'
import { noteImportApi } from '../api/noteImportApi'
import { getErrorDisplay } from '../composables/useErrorMapper'
import { useSessionStore } from '../store/sessionStore'
import { useToastStore } from '../store/toastStore'
import type { NoteImportResult } from '../types/note-import'

const session = useSessionStore()
const toast = useToastStore()

const activeTab = ref<'export' | 'import' | 'markdown' | 'webclip'>('export')

// --- Export state ---
const exportBoardId = ref('')
const exportResult = ref<string | null>(null)
const exporting = ref(false)

// --- Board JSON import state ---
const importJson = ref('')
const importResult = ref<{ success: boolean; message: string; summary: string | null } | null>(null)
const importing = ref(false)
const importStep = ref(1)

// --- Markdown import state ---
const mdFileName = ref('')
const mdContent = ref('')
const mdBoardId = ref('')
const mdImporting = ref(false)
const mdResult = ref<NoteImportResult | null>(null)
const mdError = ref<string | null>(null)

// --- Web clip import state ---
const clipUrl = ref('')
const clipContent = ref('')
const clipTitle = ref('')
const clipBoardId = ref('')
const clipImporting = ref(false)
const clipResult = ref<NoteImportResult | null>(null)
const clipError = ref<string | null>(null)

// --- Export handlers ---
async function handleExport() {
  if (!exportBoardId.value.trim()) {
    toast.warning('Please enter a board ID.')
    return
  }

  try {
    exporting.value = true
    session.requireUserId('export/import')
    const data = await exportImportApi.exportBoardJson(exportBoardId.value.trim())
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

// --- Board JSON import handlers ---
async function handleImport() {
  if (!importJson.value.trim()) {
    toast.warning('Please enter or paste JSON data.')
    return
  }

  try {
    importing.value = true
    session.requireUserId('export/import')
    const result = await exportImportApi.importBoardJson(importJson.value.trim())

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

// --- Markdown import handlers ---
function handleMarkdownFileSelect(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return

  if (file.size > 102_400) {
    toast.error('File too large. Maximum size is 100 KB.')
    target.value = ''
    return
  }

  mdFileName.value = file.name
  const reader = new FileReader()
  reader.onload = () => {
    mdContent.value = reader.result as string
  }
  reader.onerror = () => {
    toast.error('Failed to read file')
  }
  reader.readAsText(file)
}

async function handleMarkdownImport() {
  if (!mdContent.value.trim()) {
    toast.warning('Please select a markdown file or paste content.')
    return
  }

  try {
    mdImporting.value = true
    mdError.value = null
    session.requireUserId('markdown import')

    const result = await noteImportApi.importMarkdown({
      fileName: mdFileName.value || 'paste.md',
      content: mdContent.value,
      boardId: mdBoardId.value.trim() || null,
    })

    mdResult.value = result
    toast.success(`Imported ${result.itemsCreated} capture item(s) from markdown`)
  } catch (err: unknown) {
    const message = getErrorDisplay(err, 'Markdown import failed.').message
    mdError.value = message
    toast.error(message)
  } finally {
    mdImporting.value = false
  }
}

function resetMarkdownImport() {
  mdFileName.value = ''
  mdContent.value = ''
  mdBoardId.value = ''
  mdResult.value = null
  mdError.value = null
}

// --- Web clip import handlers ---
async function handleWebClipImport() {
  if (!clipUrl.value.trim()) {
    toast.warning('Please enter a URL.')
    return
  }
  if (!clipContent.value.trim()) {
    toast.warning('Please enter the clip content.')
    return
  }

  try {
    clipImporting.value = true
    clipError.value = null
    session.requireUserId('web clip import')

    const result = await noteImportApi.importWebClip({
      url: clipUrl.value.trim(),
      content: clipContent.value,
      title: clipTitle.value.trim() || null,
      boardId: clipBoardId.value.trim() || null,
    })

    clipResult.value = result
    toast.success('Web clip imported as capture item')
  } catch (err: unknown) {
    const message = getErrorDisplay(err, 'Web clip import failed.').message
    clipError.value = message
    toast.error(message)
  } finally {
    clipImporting.value = false
  }
}

function resetWebClipImport() {
  clipUrl.value = ''
  clipContent.value = ''
  clipTitle.value = ''
  clipBoardId.value = ''
  clipResult.value = null
  clipError.value = null
}
</script>

<template>
  <div class="td-export-import">
    <h1 class="td-page-title">Export / Import</h1>

    <div class="td-tabs">
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'export' }]" @click="activeTab = 'export'">Export</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'import' }]" @click="activeTab = 'import'">Import</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'markdown' }]" @click="activeTab = 'markdown'">Markdown</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'webclip' }]" @click="activeTab = 'webclip'">Web Clip</button>
    </div>

    <!-- Export tab -->
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

    <!-- Board JSON import tab -->
    <div v-if="activeTab === 'import'" class="td-panel">
      <h2 class="td-section-title">Import Board</h2>

      <div v-if="importStep === 1">
        <p class="td-section-desc">Paste board JSON data to import.</p>
        <div class="td-form-group">
          <label for="import-json" class="td-label">Board JSON</label>
          <textarea id="import-json" v-model="importJson" class="td-textarea td-textarea--lg" rows="10" placeholder="Paste JSON here..."></textarea>
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

    <!-- Markdown import tab -->
    <div v-if="activeTab === 'markdown'" class="td-panel">
      <h2 class="td-section-title">Import Markdown</h2>
      <p class="td-section-desc">
        Import a markdown file or paste markdown content. Sections are split at
        headings and created as capture items in the inbox for review.
      </p>

      <template v-if="!mdResult && !mdError">
        <div class="td-form-group">
          <label for="md-file" class="td-label">Markdown File</label>
          <input
            id="md-file"
            type="file"
            accept=".md,.markdown,.txt"
            class="td-input"
            @change="handleMarkdownFileSelect"
          />
        </div>

        <div class="td-form-group">
          <label for="md-content" class="td-label">Or paste markdown content</label>
          <textarea
            id="md-content"
            v-model="mdContent"
            class="td-textarea td-textarea--lg"
            rows="10"
            placeholder="# My Notes&#10;&#10;Content here..."
          ></textarea>
        </div>

        <div class="td-form-group">
          <label for="md-board" class="td-label">Target Board ID (optional)</label>
          <input id="md-board" v-model="mdBoardId" type="text" class="td-input" placeholder="Leave empty for inbox" />
        </div>

        <button
          class="td-btn td-btn--primary"
          :disabled="mdImporting || !mdContent.trim()"
          @click="handleMarkdownImport"
        >
          {{ mdImporting ? 'Importing...' : 'Import to Capture Inbox' }}
        </button>
      </template>

      <template v-if="mdResult">
        <div class="td-import-result td-import-result--success">
          <span>OK</span>
          <span>{{ mdResult.itemsCreated }} capture item(s) created</span>
        </div>
        <div v-if="mdResult.items.length > 0" class="td-note-import-items">
          <div v-for="item in mdResult.items" :key="item.captureItemId" class="td-note-import-item">
            <span class="td-note-import-badge">{{ item.sourceType }}</span>
            <span class="td-note-import-excerpt">{{ item.textExcerpt }}</span>
            <span v-if="item.sourceRef" class="td-note-import-ref">{{ item.sourceRef }}</span>
          </div>
        </div>
        <button class="td-btn td-btn--secondary" @click="resetMarkdownImport">Import Another</button>
      </template>

      <template v-if="mdError">
        <div class="td-import-result td-import-result--error">
          <span>ERR</span>
          <span>{{ mdError }}</span>
        </div>
        <button class="td-btn td-btn--secondary" @click="resetMarkdownImport">Try Again</button>
      </template>
    </div>

    <!-- Web clip import tab -->
    <div v-if="activeTab === 'webclip'" class="td-panel">
      <h2 class="td-section-title">Import Web Clip</h2>
      <p class="td-section-desc">
        Capture a web page snippet with its URL. The clip is created as a capture
        item in the inbox for review, with the source URL preserved as provenance.
      </p>

      <template v-if="!clipResult && !clipError">
        <div class="td-form-group">
          <label for="clip-url" class="td-label">Source URL</label>
          <input
            id="clip-url"
            v-model="clipUrl"
            type="url"
            class="td-input"
            placeholder="https://example.com/article"
          />
        </div>

        <div class="td-form-group">
          <label for="clip-title" class="td-label">Title (optional)</label>
          <input
            id="clip-title"
            v-model="clipTitle"
            type="text"
            class="td-input"
            placeholder="Article title or description"
          />
        </div>

        <div class="td-form-group">
          <label for="clip-content" class="td-label">Content Snippet</label>
          <textarea
            id="clip-content"
            v-model="clipContent"
            class="td-textarea td-textarea--lg"
            rows="8"
            placeholder="Paste the relevant content from the web page..."
          ></textarea>
        </div>

        <div class="td-form-group">
          <label for="clip-board" class="td-label">Target Board ID (optional)</label>
          <input id="clip-board" v-model="clipBoardId" type="text" class="td-input" placeholder="Leave empty for inbox" />
        </div>

        <button
          class="td-btn td-btn--primary"
          :disabled="clipImporting || !clipUrl.trim() || !clipContent.trim()"
          @click="handleWebClipImport"
        >
          {{ clipImporting ? 'Importing...' : 'Clip to Capture Inbox' }}
        </button>
      </template>

      <template v-if="clipResult">
        <div class="td-import-result td-import-result--success">
          <span>OK</span>
          <span>Web clip captured successfully</span>
        </div>
        <div v-if="clipResult.items.length > 0" class="td-note-import-items">
          <div v-for="item in clipResult.items" :key="item.captureItemId" class="td-note-import-item">
            <span class="td-note-import-badge">{{ item.sourceType }}</span>
            <span class="td-note-import-excerpt">{{ item.textExcerpt }}</span>
            <span v-if="item.sourceRef" class="td-note-import-ref">{{ item.sourceRef }}</span>
          </div>
        </div>
        <button class="td-btn td-btn--secondary" @click="resetWebClipImport">Clip Another</button>
      </template>

      <template v-if="clipError">
        <div class="td-import-result td-import-result--error">
          <span>ERR</span>
          <span>{{ clipError }}</span>
        </div>
        <button class="td-btn td-btn--secondary" @click="resetWebClipImport">Try Again</button>
      </template>
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
.td-form-group { display: flex; flex-direction: column; gap: var(--td-space-1); flex: 1; margin-bottom: var(--td-space-3); }
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
.td-json-viewer { background: var(--td-surface-container-lowest); color: var(--td-text-primary); padding: var(--td-space-4); border-radius: var(--td-radius-md); font-family: monospace; font-size: var(--td-font-sm); overflow-x: auto; max-height: 400px; overflow-y: auto; white-space: pre-wrap; margin: 0; }
.td-json-viewer--sm { max-height: 200px; }
.td-step-actions { display: flex; gap: var(--td-space-3); margin-top: var(--td-space-4); }
.td-import-result { display: flex; align-items: center; gap: var(--td-space-3); padding: var(--td-space-4); border-radius: var(--td-radius-md); margin-bottom: var(--td-space-4); font-size: var(--td-font-sm); }
.td-import-result--success { background: var(--td-color-success-light); color: var(--td-color-success); }
.td-import-result--error { background: var(--td-color-error-light); color: var(--td-color-error); }
.td-import-summary { margin-bottom: var(--td-space-4); color: var(--td-text-secondary); font-size: var(--td-font-sm); }
.td-note-import-items { display: flex; flex-direction: column; gap: var(--td-space-2); margin-bottom: var(--td-space-4); }
.td-note-import-item { display: flex; align-items: center; gap: var(--td-space-2); padding: var(--td-space-2) var(--td-space-3); background: var(--td-surface-container-lowest); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-note-import-badge { display: inline-block; padding: var(--td-space-0) var(--td-space-2); background: var(--td-color-primary); color: var(--td-text-inverse); border-radius: var(--td-radius-sm); font-size: var(--td-font-xs); font-weight: 600; text-transform: uppercase; flex-shrink: 0; }
.td-note-import-excerpt { color: var(--td-text-primary); flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.td-note-import-ref { color: var(--td-text-tertiary); font-size: var(--td-font-xs); flex-shrink: 0; max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
