<script setup lang="ts">
import { ref } from 'vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
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
  <div class="paper-portability">
    <header class="paper-portability__hero">
      <span class="tk-eyebrow paper-portability__eyebrow">Settings</span>
      <h1 class="tk-h1 paper-portability__title">Export / Import</h1>
    </header>

    <div class="paper-portability__tabs">
      <button :class="['paper-portability__tab', { 'paper-portability__tab--active': activeTab === 'export' }]" @click="activeTab = 'export'">Export</button>
      <button :class="['paper-portability__tab', { 'paper-portability__tab--active': activeTab === 'import' }]" @click="activeTab = 'import'">Import</button>
      <button :class="['paper-portability__tab', { 'paper-portability__tab--active': activeTab === 'markdown' }]" @click="activeTab = 'markdown'">Markdown</button>
      <button :class="['paper-portability__tab', { 'paper-portability__tab--active': activeTab === 'webclip' }]" @click="activeTab = 'webclip'">Web Clip</button>
    </div>

    <!-- Export tab -->
    <div v-if="activeTab === 'export'" class="paper-portability__panel">
      <h2 class="tk-h3 paper-portability__panel-title">Export Board</h2>
      <p class="paper-portability__panel-desc">Export a board to JSON for backup or sharing.</p>

      <div class="paper-portability__export-form">
        <div class="paper-portability__form-group">
          <label for="export-board" class="paper-portability__label">Board ID</label>
          <input id="export-board" v-model="exportBoardId" type="text" class="paper-portability__input" placeholder="Enter board ID" />
        </div>
        <PaperHLBtn variant="ember" :disabled="exporting" @click="handleExport">
          {{ exporting ? 'Exporting...' : 'Export JSON' }}
        </PaperHLBtn>
      </div>

      <div v-if="exportResult" class="paper-portability__export-result">
        <div class="paper-portability__result-actions">
          <PaperHLBtn @click="handleCopyExport">Copy</PaperHLBtn>
          <PaperHLBtn @click="handleDownloadExport">Download</PaperHLBtn>
        </div>
        <pre class="paper-portability__json">{{ exportResult }}</pre>
      </div>
    </div>

    <!-- Board JSON import tab -->
    <div v-if="activeTab === 'import'" class="paper-portability__panel">
      <h2 class="tk-h3 paper-portability__panel-title">Import Board</h2>

      <div v-if="importStep === 1">
        <p class="paper-portability__panel-desc">Paste board JSON data to import.</p>
        <div class="paper-portability__form-group">
          <label for="import-json" class="paper-portability__label">Board JSON</label>
          <textarea id="import-json" v-model="importJson" class="paper-portability__textarea paper-portability__textarea--lg" rows="10" placeholder="Paste JSON here..."></textarea>
        </div>
        <PaperHLBtn variant="ember" :disabled="!importJson.trim()" @click="importStep = 2">
          Validate & Preview
        </PaperHLBtn>
      </div>

      <div v-if="importStep === 2">
        <p class="paper-portability__panel-desc">Review the data before importing.</p>
        <pre class="paper-portability__json paper-portability__json--sm">{{ importJson.substring(0, 500) }}{{ importJson.length > 500 ? '...' : '' }}</pre>
        <div class="paper-portability__step-actions">
          <PaperHLBtn @click="importStep = 1">Back</PaperHLBtn>
          <PaperHLBtn variant="ember" :disabled="importing" @click="handleImport">
            {{ importing ? 'Importing...' : 'Import Board' }}
          </PaperHLBtn>
        </div>
      </div>

      <div v-if="importStep === 3 && importResult">
        <div :class="['paper-portability__result', importResult.success ? 'paper-portability__result--success' : 'paper-portability__result--error']">
          <span>{{ importResult.success ? 'OK' : 'ERR' }}</span>
          <span>{{ importResult.message }}</span>
        </div>
        <p v-if="importResult.summary" class="paper-portability__result-summary">{{ importResult.summary }}</p>
        <PaperHLBtn @click="resetImport">Import Another</PaperHLBtn>
      </div>
    </div>

    <!-- Markdown import tab -->
    <div v-if="activeTab === 'markdown'" class="paper-portability__panel">
      <h2 class="tk-h3 paper-portability__panel-title">Import Markdown</h2>
      <p class="paper-portability__panel-desc">
        Import a markdown file or paste markdown content. Sections are split at
        headings and created as capture items in the inbox for review.
      </p>

      <template v-if="!mdResult && !mdError">
        <div class="paper-portability__form-group">
          <label for="md-file" class="paper-portability__label">Markdown File</label>
          <input
            id="md-file"
            type="file"
            accept=".md,.markdown,.txt"
            class="paper-portability__input"
            @change="handleMarkdownFileSelect"
          />
        </div>

        <div class="paper-portability__form-group">
          <label for="md-content" class="paper-portability__label">Or paste markdown content</label>
          <textarea
            id="md-content"
            v-model="mdContent"
            class="paper-portability__textarea paper-portability__textarea--lg"
            rows="10"
            placeholder="# My Notes&#10;&#10;Content here..."
          ></textarea>
        </div>

        <div class="paper-portability__form-group">
          <label for="md-board" class="paper-portability__label">Target Board ID (optional)</label>
          <input id="md-board" v-model="mdBoardId" type="text" class="paper-portability__input" placeholder="Leave empty for inbox" />
        </div>

        <PaperHLBtn
          variant="ember"
          :disabled="mdImporting || !mdContent.trim()"
          @click="handleMarkdownImport"
        >
          {{ mdImporting ? 'Importing...' : 'Import to Capture Inbox' }}
        </PaperHLBtn>
      </template>

      <template v-if="mdResult">
        <div class="paper-portability__result paper-portability__result--success">
          <span>OK</span>
          <span>{{ mdResult.itemsCreated }} capture item(s) created</span>
        </div>
        <div v-if="mdResult.items.length > 0" class="paper-portability__note-items">
          <div v-for="item in mdResult.items" :key="item.captureItemId" class="paper-portability__note-item">
            <span class="paper-portability__note-badge">{{ item.sourceType }}</span>
            <span class="paper-portability__note-excerpt">{{ item.textExcerpt }}</span>
            <span v-if="item.sourceRef" class="paper-portability__note-ref">{{ item.sourceRef }}</span>
          </div>
        </div>
        <PaperHLBtn @click="resetMarkdownImport">Import Another</PaperHLBtn>
      </template>

      <template v-if="mdError">
        <div class="paper-portability__result paper-portability__result--error">
          <span>ERR</span>
          <span>{{ mdError }}</span>
        </div>
        <PaperHLBtn @click="resetMarkdownImport">Try Again</PaperHLBtn>
      </template>
    </div>

    <!-- Web clip import tab -->
    <div v-if="activeTab === 'webclip'" class="paper-portability__panel">
      <h2 class="tk-h3 paper-portability__panel-title">Import Web Clip</h2>
      <p class="paper-portability__panel-desc">
        Capture a web page snippet with its URL. The clip is created as a capture
        item in the inbox for review, with the source URL preserved as provenance.
      </p>

      <template v-if="!clipResult && !clipError">
        <div class="paper-portability__form-group">
          <label for="clip-url" class="paper-portability__label">Source URL</label>
          <input
            id="clip-url"
            v-model="clipUrl"
            type="url"
            class="paper-portability__input"
            placeholder="https://example.com/article"
          />
        </div>

        <div class="paper-portability__form-group">
          <label for="clip-title" class="paper-portability__label">Title (optional)</label>
          <input
            id="clip-title"
            v-model="clipTitle"
            type="text"
            class="paper-portability__input"
            placeholder="Article title or description"
          />
        </div>

        <div class="paper-portability__form-group">
          <label for="clip-content" class="paper-portability__label">Content Snippet</label>
          <textarea
            id="clip-content"
            v-model="clipContent"
            class="paper-portability__textarea paper-portability__textarea--lg"
            rows="8"
            placeholder="Paste the relevant content from the web page..."
          ></textarea>
        </div>

        <div class="paper-portability__form-group">
          <label for="clip-board" class="paper-portability__label">Target Board ID (optional)</label>
          <input id="clip-board" v-model="clipBoardId" type="text" class="paper-portability__input" placeholder="Leave empty for inbox" />
        </div>

        <PaperHLBtn
          variant="ember"
          :disabled="clipImporting || !clipUrl.trim() || !clipContent.trim()"
          @click="handleWebClipImport"
        >
          {{ clipImporting ? 'Importing...' : 'Clip to Capture Inbox' }}
        </PaperHLBtn>
      </template>

      <template v-if="clipResult">
        <div class="paper-portability__result paper-portability__result--success">
          <span>OK</span>
          <span>Web clip captured successfully</span>
        </div>
        <div v-if="clipResult.items.length > 0" class="paper-portability__note-items">
          <div v-for="item in clipResult.items" :key="item.captureItemId" class="paper-portability__note-item">
            <span class="paper-portability__note-badge">{{ item.sourceType }}</span>
            <span class="paper-portability__note-excerpt">{{ item.textExcerpt }}</span>
            <span v-if="item.sourceRef" class="paper-portability__note-ref">{{ item.sourceRef }}</span>
          </div>
        </div>
        <PaperHLBtn @click="resetWebClipImport">Clip Another</PaperHLBtn>
      </template>

      <template v-if="clipError">
        <div class="paper-portability__result paper-portability__result--error">
          <span>ERR</span>
          <span>{{ clipError }}</span>
        </div>
        <PaperHLBtn @click="resetWebClipImport">Try Again</PaperHLBtn>
      </template>
    </div>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — ExportImportView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens are defined under `.paper` / `.paper-night` (the canonical shell), so
   var() fallbacks keep the surface legible if the view is ever rendered outside
   the Paper shell (Legacy/Obsidian "off" mode). */

.paper-portability {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
  max-width: 800px;
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-portability__hero {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-portability__eyebrow {
  color: var(--mute, #6c6557);
}

.paper-portability__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

/* ── Tabs ── */

.paper-portability__tabs {
  display: flex;
  gap: 0;
  border-bottom: 1px solid var(--line, #d8d0bf);
}

.paper-portability__tab {
  padding: var(--s-2, 8px) var(--s-4, 16px);
  border: none;
  background: transparent;
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-sm, 12px);
  font-weight: 500;
  cursor: pointer;
  color: var(--mute, #6c6557);
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  transition: color var(--d-quick, 140ms) var(--ease-paper, ease),
    border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-portability__tab:hover {
  color: var(--ink, #1a1814);
}

.paper-portability__tab--active {
  color: var(--ember, #a8421f);
  border-bottom-color: var(--ember, #a8421f);
  font-weight: 600;
}

/* ── Panels ── */

.paper-portability__panel {
  padding: var(--s-5, 20px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-portability__panel-title {
  margin: 0 0 var(--s-2, 8px);
  font-size: var(--t-lg, 18px);
  color: var(--ink-deep, #0a0908);
}

.paper-portability__panel-desc {
  margin: 0 0 var(--s-4, 16px);
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
  line-height: 1.55;
}

/* ── Forms ── */

.paper-portability__export-form {
  display: flex;
  gap: var(--s-3, 12px);
  align-items: flex-end;
  margin-bottom: var(--s-4, 16px);
}

.paper-portability__form-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
  flex: 1;
  margin-bottom: var(--s-3, 12px);
}

.paper-portability__label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #6c6557);
}

.paper-portability__input,
.paper-portability__textarea {
  width: 100%;
  box-sizing: border-box;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-size: var(--t-md, 13.5px);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-portability__input {
  font-family: var(--sans, system-ui, sans-serif);
}

.paper-portability__textarea {
  font-family: var(--mono, ui-monospace, monospace);
  resize: vertical;
}

.paper-portability__textarea--lg {
  min-height: 200px;
}

.paper-portability__input:focus,
.paper-portability__textarea:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

/* ── Export payload viewer ── */

.paper-portability__export-result {
  margin-top: var(--s-4, 16px);
}

.paper-portability__result-actions {
  display: flex;
  gap: var(--s-2, 8px);
  margin-bottom: var(--s-2, 8px);
}

.paper-portability__json {
  margin: 0;
  padding: var(--s-4, 16px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
  overflow-x: auto;
  overflow-y: auto;
  max-height: 400px;
  white-space: pre-wrap;
}

.paper-portability__json--sm {
  max-height: 200px;
}

.paper-portability__step-actions {
  display: flex;
  gap: var(--s-3, 12px);
  margin-top: var(--s-4, 16px);
}

/* ── Import outcomes ── */

.paper-portability__result {
  display: flex;
  align-items: center;
  gap: var(--s-3, 12px);
  padding: var(--s-4, 16px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  margin-bottom: var(--s-4, 16px);
  font-size: var(--t-md, 13.5px);
}

.paper-portability__result--success {
  border-color: var(--applied, #4a6b3f);
  background: var(--applied-tint, #d8e0ce);
  color: var(--applied, #4a6b3f);
}

.paper-portability__result--error {
  border-color: var(--overdue, #8c4a26);
  background: var(--overdue-tint, #ecd9c4);
  color: var(--overdue, #8c4a26);
}

.paper-portability__result-summary {
  margin: 0 0 var(--s-4, 16px);
  color: var(--ink-2, #3a352d);
  font-size: var(--t-sm, 12px);
}

/* ── Note-import receipts ── */

.paper-portability__note-items {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  margin-bottom: var(--s-4, 16px);
}

.paper-portability__note-item {
  display: flex;
  align-items: center;
  gap: var(--s-2, 8px);
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border: 1px solid var(--line-soft, #e3dcc9);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
  font-size: var(--t-sm, 12px);
}

.paper-portability__note-badge {
  display: inline-block;
  padding: 1px var(--s-2, 8px);
  border-radius: var(--r-1, 2px);
  background: var(--ember, #a8421f);
  color: var(--td-on-ember, #fefaf6);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  text-transform: uppercase;
  flex-shrink: 0;
}

.paper-portability__note-excerpt {
  color: var(--ink, #1a1814);
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.paper-portability__note-ref {
  color: var(--mute, #6c6557);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  flex-shrink: 0;
  max-width: 200px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
