<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { apiKeysApi } from '../api/apiKeysApi'
import type { ApiKeyListItem, CreateApiKeyResponse } from '../api/apiKeysApi'
import TdButton from '../components/ui/TdButton.vue'
import TdDialog from '../components/ui/TdDialog.vue'
import TdInput from '../components/ui/TdInput.vue'
import TdEmptyState from '../components/ui/TdEmptyState.vue'
import TdBadge from '../components/ui/TdBadge.vue'
import TdInlineAlert from '../components/ui/TdInlineAlert.vue'
import TdSkeleton from '../components/ui/TdSkeleton.vue'
import { getErrorDisplay } from '../composables/useErrorMapper'

// ── List state ──
const keys = ref<ApiKeyListItem[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

// ── Create dialog state ──
const showCreateDialog = ref(false)
const newKeyName = ref('')
const creating = ref(false)
const createError = ref<string | null>(null)
const createdKey = ref<CreateApiKeyResponse | null>(null)
const keyCopied = ref(false)
const createdKeyValueRef = ref<HTMLElement | null>(null)

// ── Revoke dialog state ──
const showRevokeDialog = ref(false)
const revoking = ref(false)
const revokeError = ref<string | null>(null)
const keyToRevoke = ref<ApiKeyListItem | null>(null)

const activeKeys = computed(() => keys.value.filter(k => k.isActive))
const revokedKeys = computed(() => keys.value.filter(k => !k.isActive && k.revokedAt !== null))
const expiredKeys = computed(() => keys.value.filter(k => !k.isActive && k.revokedAt === null))

async function loadKeys() {
  loading.value = true
  loadError.value = null
  try {
    keys.value = await apiKeysApi.listKeys()
  } catch (e: unknown) {
    loadError.value = getErrorDisplay(e, 'Failed to load API keys.').message
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  newKeyName.value = ''
  createError.value = null
  createdKey.value = null
  keyCopied.value = false
  showCreateDialog.value = true
}

function closeCreateDialog() {
  if (creating.value) return
  showCreateDialog.value = false
  if (createdKey.value) {
    createdKey.value = null
    loadKeys()
  }
}

async function handleCreateKey() {
  if (!newKeyName.value.trim()) {
    createError.value = 'Key name is required.'
    return
  }
  creating.value = true
  createError.value = null
  try {
    createdKey.value = await apiKeysApi.createKey(newKeyName.value.trim())
  } catch (e: unknown) {
    createError.value = getErrorDisplay(e, 'Failed to create API key.').message
  } finally {
    creating.value = false
  }
}

async function copyKeyToClipboard() {
  if (!createdKey.value) return
  try {
    await navigator.clipboard.writeText(createdKey.value.key)
    keyCopied.value = true
  } catch {
    // Fallback: select the text in the code element for manual copy
    const codeEl = createdKeyValueRef.value
    if (codeEl) {
      const range = document.createRange()
      range.selectNodeContents(codeEl)
      const selection = window.getSelection()
      selection?.removeAllRanges()
      selection?.addRange(range)
    }
  }
}

function openRevokeDialog(key: ApiKeyListItem) {
  keyToRevoke.value = key
  revokeError.value = null
  showRevokeDialog.value = true
}

function closeRevokeDialog() {
  if (revoking.value) return
  showRevokeDialog.value = false
  keyToRevoke.value = null
}

async function handleRevokeKey() {
  if (!keyToRevoke.value) return
  revoking.value = true
  revokeError.value = null
  try {
    await apiKeysApi.revokeKey(keyToRevoke.value.id)
    revoking.value = false
    closeRevokeDialog()
    await loadKeys()
  } catch (e: unknown) {
    revokeError.value = getErrorDisplay(e, 'Failed to revoke API key.').message
  } finally {
    revoking.value = false
  }
}

function formatDate(dateString: string | null): string {
  if (!dateString) return 'Never'
  const date = new Date(dateString)
  if (isNaN(date.getTime())) return '—'
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

onMounted(loadKeys)
</script>

<template>
  <div class="paper-api-keys">
    <header class="paper-api-keys__hero">
      <span class="tk-eyebrow paper-api-keys__eyebrow">Settings</span>
      <h1 class="tk-h1 paper-api-keys__title">API Keys</h1>
      <p class="tk-lede paper-api-keys__subtitle">
        Manage API keys for MCP server HTTP transport authentication.
        Keys use the <code class="paper-api-keys__code">tdsk_</code> prefix and are rate-limited.
      </p>
    </header>

    <!-- Loading state -->
    <section v-if="loading" class="paper-api-keys__panel" aria-label="Loading API keys">
      <TdSkeleton height="1.5rem" width="40%" />
      <TdSkeleton height="3rem" />
      <TdSkeleton height="3rem" />
    </section>

    <!-- Error state -->
    <TdInlineAlert v-else-if="loadError" variant="error">
      {{ loadError }}
      <TdButton variant="ghost" size="sm" aria-label="Retry loading API keys" @click="loadKeys">
        Retry
      </TdButton>
    </TdInlineAlert>

    <!-- Empty state -->
    <section v-else-if="keys.length === 0" class="paper-api-keys__panel">
      <TdEmptyState
        title="No API keys yet"
        description="Create an API key to authenticate MCP server requests over HTTP transport."
      >
        <template #action>
          <TdButton variant="primary" aria-label="Create your first API key" @click="openCreateDialog">
            Create API Key
          </TdButton>
        </template>
      </TdEmptyState>
    </section>

    <!-- Keys list -->
    <template v-else>
      <section class="paper-api-keys__panel">
        <div class="paper-api-keys__panel-header">
          <h2 class="tk-h3 paper-api-keys__panel-title">Active Keys</h2>
          <TdButton variant="primary" size="sm" aria-label="Create a new API key" @click="openCreateDialog">
            Create Key
          </TdButton>
        </div>

        <div v-if="activeKeys.length === 0" class="paper-api-keys__empty-hint">
          No active keys. Create one to get started.
        </div>

        <div v-else class="paper-api-keys__list" role="list" aria-label="Active API keys">
          <div
            v-for="key in activeKeys"
            :key="key.id"
            class="paper-api-keys__card"
            role="listitem"
          >
            <div class="paper-api-keys__card-header">
              <span class="paper-api-keys__card-name">{{ key.name }}</span>
              <TdBadge variant="success" size="sm">Active</TdBadge>
            </div>
            <div class="paper-api-keys__card-meta">
              <span class="paper-api-keys__meta">
                <span class="paper-api-keys__meta-label">Prefix:</span>
                <code class="paper-api-keys__code">{{ key.keyPrefix }}...</code>
              </span>
              <span class="paper-api-keys__meta">
                <span class="paper-api-keys__meta-label">Created:</span>
                {{ formatDate(key.createdAt) }}
              </span>
              <span class="paper-api-keys__meta">
                <span class="paper-api-keys__meta-label">Last used:</span>
                {{ formatDate(key.lastUsedAt) }}
              </span>
              <span v-if="key.expiresAt" class="paper-api-keys__meta">
                <span class="paper-api-keys__meta-label">Expires:</span>
                {{ formatDate(key.expiresAt) }}
              </span>
            </div>
            <div class="paper-api-keys__card-actions">
              <TdButton
                variant="danger"
                size="sm"
                :aria-label="`Revoke API key ${key.name}`"
                @click="openRevokeDialog(key)"
              >
                Revoke
              </TdButton>
            </div>
          </div>
        </div>
      </section>

      <section v-if="expiredKeys.length > 0" class="paper-api-keys__panel paper-api-keys__panel--muted">
        <h2 class="tk-h3 paper-api-keys__panel-title">Expired Keys</h2>
        <div class="paper-api-keys__list" role="list" aria-label="Expired API keys">
          <div
            v-for="key in expiredKeys"
            :key="key.id"
            class="paper-api-keys__card paper-api-keys__card--inactive"
            role="listitem"
          >
            <div class="paper-api-keys__card-header">
              <span class="paper-api-keys__card-name">{{ key.name }}</span>
              <TdBadge variant="warning" size="sm">Expired</TdBadge>
            </div>
            <div class="paper-api-keys__card-meta">
              <span class="paper-api-keys__meta">
                <span class="paper-api-keys__meta-label">Prefix:</span>
                <code class="paper-api-keys__code">{{ key.keyPrefix }}...</code>
              </span>
              <span class="paper-api-keys__meta">
                <span class="paper-api-keys__meta-label">Expired:</span>
                {{ formatDate(key.expiresAt) }}
              </span>
            </div>
          </div>
        </div>
      </section>

      <section v-if="revokedKeys.length > 0" class="paper-api-keys__panel paper-api-keys__panel--muted">
        <h2 class="tk-h3 paper-api-keys__panel-title">Revoked Keys</h2>
        <div class="paper-api-keys__list" role="list" aria-label="Revoked API keys">
          <div
            v-for="key in revokedKeys"
            :key="key.id"
            class="paper-api-keys__card paper-api-keys__card--revoked"
            role="listitem"
          >
            <div class="paper-api-keys__card-header">
              <span class="paper-api-keys__card-name">{{ key.name }}</span>
              <TdBadge variant="error" size="sm">Revoked</TdBadge>
            </div>
            <div class="paper-api-keys__card-meta">
              <span class="paper-api-keys__meta">
                <span class="paper-api-keys__meta-label">Prefix:</span>
                <code class="paper-api-keys__code">{{ key.keyPrefix }}...</code>
              </span>
              <span class="paper-api-keys__meta">
                <span class="paper-api-keys__meta-label">Revoked:</span>
                {{ formatDate(key.revokedAt) }}
              </span>
            </div>
          </div>
        </div>
      </section>
    </template>

    <!-- Create Key Dialog -->
    <TdDialog
      :open="showCreateDialog"
      :title="createdKey ? 'API Key Created' : 'Create API Key'"
      @close="closeCreateDialog"
    >
      <!-- After creation: show the key -->
      <template v-if="createdKey">
        <TdInlineAlert variant="warning">
          Copy this key now. It will not be shown again.
        </TdInlineAlert>

        <div class="paper-api-keys__created">
          <p id="created-key-description" class="paper-api-keys__created-label">Your new API key</p>
          <div class="paper-api-keys__created-display" aria-describedby="created-key-description">
            <code
              ref="createdKeyValueRef"
              class="paper-api-keys__created-value"
              data-testid="created-key-value"
            >{{ createdKey.key }}</code>
            <TdButton
              variant="secondary"
              size="sm"
              :aria-label="keyCopied ? 'Key copied to clipboard' : 'Copy API key to clipboard'"
              @click="copyKeyToClipboard"
            >
              {{ keyCopied ? 'Copied' : 'Copy' }}
            </TdButton>
          </div>
        </div>
      </template>

      <!-- Before creation: show the form -->
      <template v-else>
        <TdInlineAlert v-if="createError" variant="error">
          {{ createError }}
        </TdInlineAlert>

        <div class="paper-api-keys__form-group">
          <label for="api-key-name" class="paper-api-keys__label">Key Name</label>
          <TdInput
            id="api-key-name"
            v-model="newKeyName"
            placeholder="e.g. CI pipeline, local dev"
            :disabled="creating"
          />
          <span class="paper-api-keys__hint">A descriptive name to identify this key later.</span>
        </div>
      </template>

      <template #footer>
        <TdButton v-if="createdKey" variant="primary" @click="closeCreateDialog">
          Done
        </TdButton>
        <template v-else>
          <TdButton variant="ghost" :disabled="creating" @click="closeCreateDialog">
            Cancel
          </TdButton>
          <TdButton
            variant="primary"
            :loading="creating"
            :disabled="!newKeyName.trim()"
            @click="handleCreateKey"
          >
            Create Key
          </TdButton>
        </template>
      </template>
    </TdDialog>

    <!-- Revoke Confirmation Dialog -->
    <TdDialog
      :open="showRevokeDialog"
      title="Revoke API Key"
      @close="closeRevokeDialog"
    >
      <TdInlineAlert v-if="revokeError" variant="error">
        {{ revokeError }}
      </TdInlineAlert>

      <p class="paper-api-keys__revoke-warning">
        Are you sure you want to revoke the key
        <strong>{{ keyToRevoke?.name }}</strong>?
        Any integrations using this key will immediately stop working.
        This action cannot be undone.
      </p>

      <template #footer>
        <TdButton variant="ghost" :disabled="revoking" @click="closeRevokeDialog">
          Cancel
        </TdButton>
        <TdButton
          variant="danger"
          :loading="revoking"
          @click="handleRevokeKey"
        >
          Revoke Key
        </TdButton>
      </template>
    </TdDialog>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — ApiKeySettingsView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night` in paper-tokens.css and are NOT
   defined at :root, so outside the Paper shell (Legacy/Obsidian "off" mode)
   every var() resolves to its literal fallback. The substrate line on the root —
   `background: var(--paper, #f3eee5)` painted alongside `color: var(--ink,
   #1a1814)` — is what keeps the text legible in Legacy: without it the near-black
   ink lands on AppShell's Obsidian `--td-surface-base` (#131313) at ~1.05:1. It
   is a no-op under `.paper` / `.paper-night`, where `.td-shell--paper
   .td-content` already paints `var(--paper)`.
   Paper typography (the `tk-*` classes) is scoped as `.paper .tk-*` /
   `.paper-night .tk-*` and intentionally does NOT render in Legacy mode — only
   legibility is preserved there, not the Paper type ladder.

   Scope note: this page composes shared `components/ui/Td*` primitives
   (TdButton, TdBadge, TdDialog, TdInlineAlert, TdEmptyState, TdSkeleton, TdInput)
   which have no Paper variant and are owned by the shared-component layer, not
   by this view. Only the page's own chrome is restyled here; making the Td*
   primitives Paper-aware is a separate, shared-surface change. */

.paper-api-keys {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
  max-width: 640px;
  font-family: var(--sans, system-ui, sans-serif);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-api-keys__hero {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-api-keys__eyebrow {
  color: var(--mute, #6c6557);
}

.paper-api-keys__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

.paper-api-keys__subtitle {
  margin: 0;
  color: var(--ink-2, #3a352d);
}

/* ── Panels ── */

.paper-api-keys__panel {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  padding: var(--s-5, 20px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-api-keys__panel--muted {
  opacity: 0.7;
}

.paper-api-keys__panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
}

.paper-api-keys__panel-title {
  margin: 0;
  font-size: var(--t-lg, 18px);
  color: var(--ink-deep, #0a0908);
}

/* ── Key cards ── */

.paper-api-keys__list {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-api-keys__empty-hint {
  font-size: var(--t-sm, 12px);
  color: var(--mute, #6c6557);
  text-align: center;
  padding: var(--s-4, 16px);
}

.paper-api-keys__card {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  padding: var(--s-4, 16px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
}

.paper-api-keys__card--revoked,
.paper-api-keys__card--inactive {
  opacity: 0.6;
}

.paper-api-keys__card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
}

.paper-api-keys__card-name {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-api-keys__card-meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-4, 16px);
}

.paper-api-keys__meta {
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
}

.paper-api-keys__meta-label {
  color: var(--mute, #6c6557);
  margin-right: var(--s-1, 4px);
}

.paper-api-keys__card-actions {
  display: flex;
  justify-content: flex-end;
  padding-top: var(--s-2, 8px);
  border-top: 1px solid var(--line-soft, #e3dcc9);
}

.paper-api-keys__code {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
  background: var(--paper-2, #ebe5d8);
  color: var(--ink, #1a1814);
  padding: 1px var(--s-1, 4px);
  border-radius: var(--r-1, 2px);
}

/* ── Created-key display (inside the shared TdDialog) ── */

.paper-api-keys__created {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-api-keys__created-label {
  margin: 0;
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #6c6557);
}

.paper-api-keys__created-display {
  display: flex;
  align-items: center;
  gap: var(--s-2, 8px);
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper-2, #ebe5d8);
}

.paper-api-keys__created-value {
  flex: 1;
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
  word-break: break-all;
  color: var(--ink, #1a1814);
  user-select: all;
}

/* ── Form helpers ── */

.paper-api-keys__form-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-api-keys__label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #6c6557);
}

.paper-api-keys__hint {
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
}

/* ── Revoke warning ── */

.paper-api-keys__revoke-warning {
  margin: 0;
  font-size: var(--t-md, 13.5px);
  color: var(--ink-2, #3a352d);
  line-height: 1.6;
}
</style>
