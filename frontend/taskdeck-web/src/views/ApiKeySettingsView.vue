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

// ── Revoke dialog state ──
const showRevokeDialog = ref(false)
const revoking = ref(false)
const revokeError = ref<string | null>(null)
const keyToRevoke = ref<ApiKeyListItem | null>(null)

const activeKeys = computed(() => keys.value.filter(k => k.isActive))
const revokedKeys = computed(() => keys.value.filter(k => !k.isActive))

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
  showCreateDialog.value = false
  // If a key was created, refresh the list
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
    const codeEl = document.querySelector('[data-testid="created-key-value"]')
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
  showRevokeDialog.value = false
  keyToRevoke.value = null
}

async function handleRevokeKey() {
  if (!keyToRevoke.value) return
  revoking.value = true
  revokeError.value = null
  try {
    await apiKeysApi.revokeKey(keyToRevoke.value.id)
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
  return date.toLocaleDateString(undefined, {
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
  <div class="td-settings">
    <h1 class="td-page-title">API Keys</h1>
    <p class="td-page-desc">
      Manage API keys for MCP server HTTP transport authentication.
      Keys use the <code class="td-code-inline">tdsk_</code> prefix and are rate-limited to 60 requests per minute.
    </p>

    <!-- Loading state -->
    <section v-if="loading" class="td-settings__section" aria-label="Loading API keys">
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
    <section v-else-if="keys.length === 0" class="td-settings__section">
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
      <section class="td-settings__section">
        <div class="td-section-header">
          <h2 class="td-section-title">Active Keys</h2>
          <TdButton variant="primary" size="sm" aria-label="Create a new API key" @click="openCreateDialog">
            Create Key
          </TdButton>
        </div>

        <div v-if="activeKeys.length === 0" class="td-keys-empty-hint">
          No active keys. Create one to get started.
        </div>

        <div v-else class="td-keys-list" role="list" aria-label="Active API keys">
          <div
            v-for="key in activeKeys"
            :key="key.id"
            class="td-key-card"
            role="listitem"
          >
            <div class="td-key-card__header">
              <span class="td-key-card__name">{{ key.name }}</span>
              <TdBadge variant="success" size="sm">Active</TdBadge>
            </div>
            <div class="td-key-card__meta">
              <span class="td-key-meta">
                <span class="td-key-meta__label">Prefix:</span>
                <code class="td-code-inline">{{ key.keyPrefix }}...</code>
              </span>
              <span class="td-key-meta">
                <span class="td-key-meta__label">Created:</span>
                {{ formatDate(key.createdAt) }}
              </span>
              <span class="td-key-meta">
                <span class="td-key-meta__label">Last used:</span>
                {{ formatDate(key.lastUsedAt) }}
              </span>
              <span v-if="key.expiresAt" class="td-key-meta">
                <span class="td-key-meta__label">Expires:</span>
                {{ formatDate(key.expiresAt) }}
              </span>
            </div>
            <div class="td-key-card__actions">
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

      <section v-if="revokedKeys.length > 0" class="td-settings__section td-settings__section--muted">
        <h2 class="td-section-title">Revoked Keys</h2>
        <div class="td-keys-list" role="list" aria-label="Revoked API keys">
          <div
            v-for="key in revokedKeys"
            :key="key.id"
            class="td-key-card td-key-card--revoked"
            role="listitem"
          >
            <div class="td-key-card__header">
              <span class="td-key-card__name">{{ key.name }}</span>
              <TdBadge variant="error" size="sm">Revoked</TdBadge>
            </div>
            <div class="td-key-card__meta">
              <span class="td-key-meta">
                <span class="td-key-meta__label">Prefix:</span>
                <code class="td-code-inline">{{ key.keyPrefix }}...</code>
              </span>
              <span class="td-key-meta">
                <span class="td-key-meta__label">Revoked:</span>
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

        <div class="td-created-key">
          <label class="td-created-key__label" for="created-key-display">Your new API key</label>
          <div class="td-created-key__display">
            <code
              id="created-key-display"
              class="td-created-key__value"
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

        <div class="td-form-group">
          <label for="api-key-name" class="td-label">Key Name</label>
          <TdInput
            id="api-key-name"
            v-model="newKeyName"
            placeholder="e.g. CI pipeline, local dev"
            :disabled="creating"
          />
          <span class="td-form-hint">A descriptive name to identify this key later.</span>
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

      <p class="td-revoke-warning">
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
.td-settings { max-width: 640px; }

.td-page-title {
  font-size: var(--td-font-2xl);
  font-weight: 700;
  margin-bottom: var(--td-space-2);
  color: var(--td-text-primary);
}

.td-page-desc {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  margin-bottom: var(--td-space-6);
  line-height: 1.5;
}

.td-settings__section {
  background: var(--td-surface-primary);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-6);
  margin-bottom: var(--td-space-4);
  border: 1px solid var(--td-border-default);
}

.td-settings__section--muted {
  opacity: 0.7;
}

.td-section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: var(--td-space-4);
}

.td-section-title {
  font-size: var(--td-font-lg);
  font-weight: 600;
  color: var(--td-text-primary);
  margin: 0;
}

.td-keys-list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-keys-empty-hint {
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
  text-align: center;
  padding: var(--td-space-4);
}

.td-key-card {
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-4);
  background: var(--td-surface-container);
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-key-card--revoked {
  opacity: 0.6;
}

.td-key-card__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.td-key-card__name {
  font-size: var(--td-font-base);
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-key-card__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-4);
}

.td-key-meta {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-key-meta__label {
  color: var(--td-text-tertiary);
  margin-right: var(--td-space-1);
}

.td-key-card__actions {
  display: flex;
  justify-content: flex-end;
  padding-top: var(--td-space-2);
  border-top: 1px solid var(--td-border-ghost);
}

.td-code-inline {
  font-family: monospace;
  font-size: var(--td-font-sm);
  background: var(--td-surface-container-high);
  padding: 1px var(--td-space-1);
  border-radius: var(--td-radius-sm);
}

/* ── Create key display ── */
.td-created-key {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-created-key__label {
  font-size: var(--td-font-sm);
  font-weight: 500;
  color: var(--td-text-secondary);
}

.td-created-key__display {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  background: var(--td-surface-container-high);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-2) var(--td-space-3);
}

.td-created-key__value {
  flex: 1;
  font-family: monospace;
  font-size: var(--td-font-sm);
  word-break: break-all;
  color: var(--td-text-primary);
  user-select: all;
}

/* ── Form helpers ── */
.td-form-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-label {
  font-size: var(--td-font-sm);
  font-weight: 500;
  color: var(--td-text-secondary);
}

.td-form-hint {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
}

/* ── Revoke warning ── */
.td-revoke-warning {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  line-height: 1.6;
  margin: 0;
}
</style>
