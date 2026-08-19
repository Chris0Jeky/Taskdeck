<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useIntegrationStore } from '../store/integrationStore'
import type {
  IntegrationConnector,
  ConnectorType,
  ConnectorDirection,
  CreateIntegrationConnectorRequest,
} from '../types/integration'
import {
  ConnectorTypeValues,
  ConnectorDirectionValues,
  ConnectorTypeLabels,
  ConnectorDirectionLabels,
} from '../types/integration'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'

const store = useIntegrationStore()

const showAddForm = ref(false)
const newName = ref('')
const newType = ref<ConnectorType>('BrowserClipper')
const newDirection = ref<ConnectorDirection>('Inbound')
const newConfig = ref('')
const registering = ref(false)

const selectedId = ref<string | null>(null)
const detailLoading = ref(false)

const connectors = computed(() => store.connectors)
const loading = computed(() => store.loading)
const error = computed(() => store.error)

// Reactive detail: reads directly from store so enable/disable changes are reflected immediately
const detail = computed(() =>
  store.selectedConnector?.id === selectedId.value ? store.selectedConnector : null,
)

const connectorTypes: ConnectorType[] = [
  'BrowserClipper',
  'MarkdownImport',
  'WebClip',
  'GitHubIssueIntake',
  'WebhookInbound',
  'Custom',
]

const connectorDirections: ConnectorDirection[] = ['Inbound', 'Context', 'Outbound']

function statusBadgeClass(status: string): string {
  switch (status) {
    case 'Active':
      return 'paper-int__badge--active'
    case 'Disabled':
      return 'paper-int__badge--disabled'
    case 'Error':
      return 'paper-int__badge--error'
    default:
      return ''
  }
}

function directionBadgeClass(direction: string): string {
  switch (direction) {
    case 'Inbound':
      return 'paper-int__dir--inbound'
    case 'Context':
      return 'paper-int__dir--context'
    case 'Outbound':
      return 'paper-int__dir--outbound'
    default:
      return ''
  }
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

async function handleRegister() {
  if (!newName.value.trim()) return
  registering.value = true
  try {
    const request: CreateIntegrationConnectorRequest = {
      name: newName.value.trim(),
      connectorType: ConnectorTypeValues[newType.value],
      direction: ConnectorDirectionValues[newDirection.value],
      configuration: newConfig.value.trim() || null,
    }
    await store.registerConnector(request)
    newName.value = ''
    newConfig.value = ''
    showAddForm.value = false
  } catch {
    // Error handled by store
  } finally {
    registering.value = false
  }
}

async function handleSelectConnector(connector: IntegrationConnector) {
  if (selectedId.value === connector.id) {
    selectedId.value = null
    return
  }
  const targetId = connector.id
  selectedId.value = targetId
  detailLoading.value = true
  try {
    await store.fetchConnectorDetail(targetId)
  } finally {
    // Only clear loading if this request is still the active one;
    // a newer request for a different connector owns the loading state.
    if (selectedId.value === targetId) {
      detailLoading.value = false
    }
  }
}

async function handleToggle(connector: IntegrationConnector) {
  try {
    if (connector.status === 'Active') {
      await store.disableConnector(connector.id)
    } else {
      await store.enableConnector(connector.id)
    }
  } catch {
    // Error handled by store
  }
}

async function handleDelete(connector: IntegrationConnector) {
  try {
    await store.deleteConnector(connector.id)
    if (selectedId.value === connector.id) {
      selectedId.value = null
    }
  } catch {
    // Error handled by store
  }
}

onMounted(() => {
  void store.fetchConnectors()
})
</script>

<template>
  <div class="paper-int">
    <header class="paper-int__hero">
      <div class="paper-int__hero-copy">
        <span class="tk-eyebrow paper-int__eyebrow">Platform</span>
        <h1 class="tk-h2 paper-int__title">Integrations</h1>
        <p class="tk-lede paper-int__subtitle">
          Register and manage connector definitions for future integrations.
          Registration, enablement, and configuration do not yet ingest external content.
        </p>
      </div>
      <PaperHLBtn
        variant="ember"
        class="paper-int__add-btn"
        :disabled="showAddForm"
        @click="showAddForm = true"
      >
        + Add Connector
      </PaperHLBtn>
    </header>

    <section class="paper-int__capture-callout" aria-label="Standalone content capture">
      <div>
        <h2 class="paper-int__capture-title">Capture content without a connector</h2>
        <p class="paper-int__capture-desc">
          Use Markdown import or web clip capture in Settings → Export &amp; Import.
          Connector registry entries manage metadata and lifecycle only; they do not ingest content.
        </p>
      </div>
      <router-link
        class="paper-int__action paper-int__action--ember paper-int__capture-link"
        :to="{ name: 'workspace-settings-export-import' }"
      >
        Open Markdown import and web clip capture
      </router-link>
    </section>

    <!-- Add connector form -->
    <section
      v-if="showAddForm"
      class="paper-int__form"
      aria-label="Register a new connector"
    >
      <h2 class="tk-h3 paper-int__form-title">Register Connector</h2>
      <form @submit.prevent="handleRegister">
        <div class="paper-int__form-row">
          <label for="connector-name" class="paper-int__label">Name</label>
          <input
            id="connector-name"
            v-model="newName"
            type="text"
            class="paper-int__input"
            placeholder="My GitHub Connector"
            required
            maxlength="100"
          />
        </div>
        <div class="paper-int__form-row">
          <label for="connector-type" class="paper-int__label">Type</label>
          <select id="connector-type" v-model="newType" class="paper-int__select">
            <option v-for="ct in connectorTypes" :key="ct" :value="ct">
              {{ ConnectorTypeLabels[ct] }}
            </option>
          </select>
        </div>
        <div class="paper-int__form-row">
          <label for="connector-direction" class="paper-int__label">Direction</label>
          <select id="connector-direction" v-model="newDirection" class="paper-int__select">
            <option v-for="cd in connectorDirections" :key="cd" :value="cd">
              {{ ConnectorDirectionLabels[cd] }}
            </option>
          </select>
        </div>
        <div class="paper-int__form-row">
          <label for="connector-config" class="paper-int__label">Configuration (JSON, optional)</label>
          <textarea
            id="connector-config"
            v-model="newConfig"
            class="paper-int__textarea"
            placeholder='{"url": "https://...", "token": "..."}'
            rows="3"
          />
        </div>
        <div class="paper-int__form-actions">
          <PaperHLBtn
            type="submit"
            variant="ember"
            class="paper-int__register"
            :disabled="registering || !newName.trim()"
          >
            {{ registering ? 'Registering...' : 'Register' }}
          </PaperHLBtn>
          <PaperHLBtn
            type="button"
            variant="ghost"
            class="paper-int__cancel"
            @click="showAddForm = false"
          >
            Cancel
          </PaperHLBtn>
        </div>
      </form>
    </section>

    <!-- Loading state -->
    <div v-if="loading && !connectors.length" class="paper-int__loading" role="status">
      Loading integrations...
    </div>

    <!-- Error state -->
    <div v-else-if="error && !connectors.length" class="paper-int__error" role="alert">
      {{ error }}
    </div>

    <!-- Empty state -->
    <div v-else-if="!loading && !connectors.length" class="paper-int__empty">
      <p class="paper-int__empty-title">No connectors configured</p>
      <p class="paper-int__empty-desc">
        Register a connector definition to manage its type, direction, and configuration.
        Connector runtime ingestion is not available yet; use the note import or web clip capture routes for content today.
      </p>
      <PaperHLBtn
        v-if="!showAddForm"
        variant="ember"
        class="paper-int__add-first"
        @click="showAddForm = true"
      >
        + Add Your First Connector
      </PaperHLBtn>
    </div>

    <!-- Connector list -->
    <section
      v-else
      class="paper-int__list"
      aria-label="Configured connectors"
    >
      <div
        v-for="connector in connectors"
        :key="connector.id"
        class="paper-int__card"
        :class="{ 'paper-int__card--selected': selectedId === connector.id }"
      >
        <div
          class="paper-int__card-header"
          role="button"
          tabindex="0"
          :aria-expanded="selectedId === connector.id"
          @click="handleSelectConnector(connector)"
          @keydown.enter.self="handleSelectConnector(connector)"
          @keydown.space.self.prevent="handleSelectConnector(connector)"
        >
          <div class="paper-int__card-info">
            <h3 class="paper-int__card-name">{{ connector.name }}</h3>
            <div class="paper-int__card-meta">
              <span
                class="paper-int__badge"
                :class="statusBadgeClass(connector.status)"
              >
                {{ connector.status }}
              </span>
              <span
                class="paper-int__dir"
                :class="directionBadgeClass(connector.direction)"
              >
                {{ ConnectorDirectionLabels[connector.direction] || connector.direction }}
              </span>
              <span class="paper-int__type">
                {{ ConnectorTypeLabels[connector.connectorType] || connector.connectorType }}
              </span>
            </div>
          </div>
          <div class="paper-int__card-actions">
            <button
              class="paper-int__toggle-btn"
              :title="connector.status === 'Active' ? 'Disable' : 'Enable'"
              :aria-label="connector.status === 'Active' ? 'Disable connector' : 'Enable connector'"
              @click.stop="handleToggle(connector)"
            >
              {{ connector.status === 'Active' ? 'Disable' : 'Enable' }}
            </button>
            <button
              class="paper-int__delete-btn"
              title="Remove connector"
              aria-label="Remove connector"
              @click.stop="handleDelete(connector)"
            >
              Remove
            </button>
          </div>
        </div>

        <!-- Detail panel -->
        <div
          v-if="selectedId === connector.id"
          class="paper-int__detail"
        >
          <div v-if="detailLoading" class="paper-int__detail-loading" role="status">
            Loading details...
          </div>
          <template v-else-if="detail">
            <div class="paper-int__detail-section">
              <h4 class="paper-int__detail-heading">Configuration</h4>
              <pre v-if="detail.configuration" class="paper-int__config-pre">{{ detail.configuration }}</pre>
              <p v-else class="paper-int__config-empty">No configuration set.</p>
            </div>
            <div class="paper-int__detail-section">
              <h4 class="paper-int__detail-heading">Recent Events</h4>
              <div v-if="detail.recentEvents.length" class="paper-int__events">
                <div
                  v-for="event in detail.recentEvents"
                  :key="event.id"
                  class="paper-int__event"
                >
                  <span class="paper-int__event-type">{{ event.eventType }}</span>
                  <span class="paper-int__event-date">{{ formatDate(event.createdAt) }}</span>
                  <span v-if="event.payload" class="paper-int__event-payload">{{ event.payload }}</span>
                </div>
              </div>
              <p v-else class="paper-int__events-empty">No events recorded yet.</p>
            </div>
            <div class="paper-int__detail-section">
              <h4 class="paper-int__detail-heading">Timestamps</h4>
              <p>Created: {{ formatDate(detail.createdAt) }}</p>
              <p>Updated: {{ formatDate(detail.updatedAt) }}</p>
            </div>
          </template>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — IntegrationsView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell.  The status and
   direction badges previously used raw Tailwind-palette hexes; they now read
   from the earth-tone semantic tokens instead. */

.paper-int {
  max-width: 52rem;
  margin: 0 auto;
  padding: var(--s-6, 24px);
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.paper-int__hero {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--s-4, 16px);
  margin-bottom: var(--s-6, 24px);
}

.paper-int__hero-copy { display: flex; flex-direction: column; gap: var(--s-2, 8px); }
.paper-int__eyebrow { color: var(--ember, #a8421f); }
.paper-int__title { margin: 0; font-size: var(--t-h2, 32px); }
.paper-int__subtitle { margin: 0; color: var(--ink-2, #3a352d); max-width: 32rem; }

.paper-int__add-btn { flex-shrink: 0; white-space: nowrap; }

/* Link styled as a Paper action (router-link cannot be a PaperHLBtn button). */
.paper-int__action {
  display: inline-flex;
  align-items: center;
  padding: var(--s-2, 8px) var(--s-4, 16px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-size: var(--t-md, 13.5px);
  font-weight: 600;
  text-decoration: none;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-int__action--ember {
  border-color: var(--ember, #a8421f);
  background: var(--ember, #a8421f);
  color: var(--td-on-ember, #fefaf6);
}

.paper-int__action--ember:hover { filter: brightness(1.1); }

.paper-int__capture-callout {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--s-4, 16px);
  padding: var(--s-4, 16px);
  margin-bottom: var(--s-6, 24px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-int__capture-title {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 600;
  margin: 0 0 var(--s-1, 4px);
  color: var(--ink-deep, #0a0908);
}

.paper-int__capture-desc {
  font-size: var(--t-md, 13.5px);
  line-height: 1.5;
  margin: 0;
  color: var(--ink-2, #3a352d);
}

.paper-int__capture-link { flex-shrink: 0; }

@media (max-width: 640px) {
  .paper-int__capture-callout {
    flex-direction: column;
    align-items: stretch;
  }

  .paper-int__capture-link {
    width: 100%;
    box-sizing: border-box;
    justify-content: center;
    text-align: center;
  }
}

/* Form */
.paper-int__form {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-5, 20px);
  margin-bottom: var(--s-6, 24px);
}

.paper-int__form-title { margin: 0 0 var(--s-4, 16px); font-size: var(--t-lg, 18px); }

.paper-int__form-row { margin-bottom: var(--s-3, 12px); }

.paper-int__label {
  display: block;
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  margin-bottom: var(--s-1, 4px);
  color: var(--mute, #6c6557);
}

.paper-int__input,
.paper-int__select,
.paper-int__textarea {
  width: 100%;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  box-sizing: border-box;
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-int__input:focus,
.paper-int__select:focus,
.paper-int__textarea:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-int__textarea {
  resize: vertical;
  font-family: var(--mono, ui-monospace, monospace);
}

.paper-int__form-actions {
  display: flex;
  gap: var(--s-2, 8px);
  margin-top: var(--s-4, 16px);
}

/* States */
.paper-int__loading,
.paper-int__error,
.paper-int__empty {
  text-align: center;
  padding: var(--s-12, 56px) var(--s-4, 16px);
  color: var(--mute, #6c6557);
}

.paper-int__error { color: var(--overdue, #8c4a26); }

.paper-int__empty-title {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-lg, 18px);
  font-weight: 600;
  margin: 0 0 var(--s-2, 8px);
  color: var(--ink-deep, #0a0908);
}

.paper-int__empty-desc {
  font-size: var(--t-md, 13.5px);
  max-width: 28rem;
  margin: 0 auto var(--s-4, 16px);
  line-height: 1.5;
}

/* Connector list */
.paper-int__list {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-int__card {
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  overflow: hidden;
}

.paper-int__card--selected { border-color: var(--ember, #a8421f); }

.paper-int__card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--s-3, 12px) var(--s-4, 16px);
  cursor: pointer;
  gap: var(--s-3, 12px);
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-int__card-header:hover { background: var(--paper-2, #ebe5d8); }

.paper-int__card-name {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 600;
  margin: 0 0 var(--s-1, 4px);
  color: var(--ink-deep, #0a0908);
}

.paper-int__card-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--s-2, 8px);
  font-size: var(--t-xs, 10.5px);
}

.paper-int__badge {
  padding: 1px var(--s-2, 8px);
  border-radius: var(--r-1, 2px);
  font-weight: 600;
  font-size: var(--t-xs, 10.5px);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.paper-int__badge--active { background: var(--applied-tint, #d8e0ce); color: var(--applied, #4a6b3f); }
.paper-int__badge--disabled { background: var(--paper-2, #ebe5d8); color: var(--mute, #6c6557); }
.paper-int__badge--error { background: var(--ember-bloom, #a8421f1a); color: var(--ember-deep, #7a2e15); }

.paper-int__dir {
  padding: 1px var(--s-2, 8px);
  border-radius: var(--r-1, 2px);
  font-weight: 600;
  font-size: var(--t-xs, 10.5px);
}

.paper-int__dir--inbound { background: var(--ember-tint, #f0d9c8); color: var(--ember-ink, #6e2810); }
.paper-int__dir--context { background: var(--overdue-tint, #ecd9c4); color: var(--overdue, #8c4a26); }
.paper-int__dir--outbound { background: var(--paper-edge, #e3dac8); color: var(--ink-2, #3a352d); }

.paper-int__type {
  font-family: var(--mono, ui-monospace, monospace);
  color: var(--mute, #6c6557);
}

.paper-int__card-actions {
  display: flex;
  gap: var(--s-1, 4px);
  flex-shrink: 0;
}

.paper-int__toggle-btn,
.paper-int__delete-btn {
  padding: var(--s-1, 4px) var(--s-2, 8px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
  font-family: inherit;
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  cursor: pointer;
  color: var(--ink-2, #3a352d);
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease),
    border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-int__toggle-btn:hover { background: var(--paper-2, #ebe5d8); }

.paper-int__delete-btn:hover {
  background: var(--ember-bloom, #a8421f1a);
  color: var(--ember-deep, #7a2e15);
  border-color: var(--ember, #a8421f);
}

/* Detail panel */
.paper-int__detail {
  border-top: 1px solid var(--line, #d8d0bf);
  padding: var(--s-4, 16px);
  background: var(--paper, #f3eee5);
}

.paper-int__detail-loading {
  text-align: center;
  color: var(--mute, #6c6557);
  font-size: var(--t-md, 13.5px);
  padding: var(--s-4, 16px) 0;
}

.paper-int__detail-section { margin-bottom: var(--s-4, 16px); }
.paper-int__detail-section:last-child { margin-bottom: 0; }

.paper-int__detail-heading {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  margin: 0 0 var(--s-1, 4px);
  color: var(--mute, #6c6557);
  text-transform: uppercase;
  letter-spacing: 0.1em;
}

.paper-int__config-pre {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  padding: var(--s-2, 8px) var(--s-3, 12px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
  white-space: pre-wrap;
  word-break: break-all;
  overflow: auto;
  max-height: 10rem;
}

.paper-int__config-empty {
  font-size: var(--t-md, 13.5px);
  color: var(--mute, #6c6557);
  font-style: italic;
}

.paper-int__events {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-int__event {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2, 8px);
  align-items: baseline;
  font-size: var(--t-md, 13.5px);
  padding: var(--s-1, 4px) 0;
  border-bottom: 1px solid var(--line-soft, #e3dcc9);
}

.paper-int__event:last-child { border-bottom: none; }

.paper-int__event-type { font-weight: 600; color: var(--ink-deep, #0a0908); }

.paper-int__event-date {
  font-family: var(--mono, ui-monospace, monospace);
  color: var(--mute, #6c6557);
  font-size: var(--t-xs, 10.5px);
}

.paper-int__event-payload {
  color: var(--ink-2, #3a352d);
  font-size: var(--t-xs, 10.5px);
  word-break: break-all;
}

.paper-int__events-empty {
  font-size: var(--t-md, 13.5px);
  color: var(--mute, #6c6557);
  font-style: italic;
}

.paper-int__detail-section p {
  font-size: var(--t-md, 13.5px);
  color: var(--ink-2, #3a352d);
  margin: var(--s-1, 4px) 0;
}
</style>
