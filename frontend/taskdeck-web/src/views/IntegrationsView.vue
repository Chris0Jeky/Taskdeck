<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useIntegrationStore } from '../store/integrationStore'
import type {
  IntegrationConnector,
  IntegrationConnectorDetail,
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

const store = useIntegrationStore()

const showAddForm = ref(false)
const newName = ref('')
const newType = ref<ConnectorType>('BrowserClipper')
const newDirection = ref<ConnectorDirection>('Inbound')
const newConfig = ref('')
const registering = ref(false)

const selectedId = ref<string | null>(null)
const detail = ref<IntegrationConnectorDetail | null>(null)
const detailLoading = ref(false)

const connectors = computed(() => store.connectors)
const loading = computed(() => store.loading)
const error = computed(() => store.error)

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
      return 'td-int__badge--active'
    case 'Disabled':
      return 'td-int__badge--disabled'
    case 'Error':
      return 'td-int__badge--error'
    default:
      return ''
  }
}

function directionBadgeClass(direction: string): string {
  switch (direction) {
    case 'Inbound':
      return 'td-int__dir--inbound'
    case 'Context':
      return 'td-int__dir--context'
    case 'Outbound':
      return 'td-int__dir--outbound'
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
    detail.value = null
    return
  }
  selectedId.value = connector.id
  detailLoading.value = true
  try {
    await store.fetchConnectorDetail(connector.id)
    detail.value = store.selectedConnector
  } finally {
    detailLoading.value = false
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
      detail.value = null
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
  <div class="td-int">
    <header class="td-int__hero">
      <div class="td-int__hero-copy">
        <span class="td-int__eyebrow">Platform</span>
        <h1 class="td-page-title">Integrations</h1>
        <p class="td-int__subtitle">
          Manage connector instances that feed data into your workspace.
          All inbound connectors route through the capture pipeline for review-first safety.
        </p>
      </div>
      <button
        class="td-int__add-btn"
        :disabled="showAddForm"
        @click="showAddForm = true"
      >
        + Add Connector
      </button>
    </header>

    <!-- Add connector form -->
    <section
      v-if="showAddForm"
      class="td-int__form"
      aria-label="Register a new connector"
    >
      <h2 class="td-int__form-title">Register Connector</h2>
      <form @submit.prevent="handleRegister">
        <div class="td-int__form-row">
          <label for="connector-name" class="td-int__label">Name</label>
          <input
            id="connector-name"
            v-model="newName"
            type="text"
            class="td-int__input"
            placeholder="My GitHub Connector"
            required
            maxlength="100"
          />
        </div>
        <div class="td-int__form-row">
          <label for="connector-type" class="td-int__label">Type</label>
          <select id="connector-type" v-model="newType" class="td-int__select">
            <option v-for="ct in connectorTypes" :key="ct" :value="ct">
              {{ ConnectorTypeLabels[ct] }}
            </option>
          </select>
        </div>
        <div class="td-int__form-row">
          <label for="connector-direction" class="td-int__label">Direction</label>
          <select id="connector-direction" v-model="newDirection" class="td-int__select">
            <option v-for="cd in connectorDirections" :key="cd" :value="cd">
              {{ ConnectorDirectionLabels[cd] }}
            </option>
          </select>
        </div>
        <div class="td-int__form-row">
          <label for="connector-config" class="td-int__label">Configuration (JSON, optional)</label>
          <textarea
            id="connector-config"
            v-model="newConfig"
            class="td-int__textarea"
            placeholder='{"url": "https://...", "token": "..."}'
            rows="3"
          />
        </div>
        <div class="td-int__form-actions">
          <button
            type="submit"
            class="td-int__btn td-int__btn--primary"
            :disabled="registering || !newName.trim()"
          >
            {{ registering ? 'Registering...' : 'Register' }}
          </button>
          <button
            type="button"
            class="td-int__btn td-int__btn--secondary"
            @click="showAddForm = false"
          >
            Cancel
          </button>
        </div>
      </form>
    </section>

    <!-- Loading state -->
    <div v-if="loading && !connectors.length" class="td-int__loading" role="status">
      Loading integrations...
    </div>

    <!-- Error state -->
    <div v-else-if="error && !connectors.length" class="td-int__error" role="alert">
      {{ error }}
    </div>

    <!-- Empty state -->
    <div v-else-if="!loading && !connectors.length" class="td-int__empty">
      <p class="td-int__empty-title">No connectors configured</p>
      <p class="td-int__empty-desc">
        Add a connector to start ingesting data from external sources into your workspace.
        Inbound connectors route through the capture pipeline so nothing changes without your review.
      </p>
      <button
        v-if="!showAddForm"
        class="td-int__btn td-int__btn--primary"
        @click="showAddForm = true"
      >
        + Add Your First Connector
      </button>
    </div>

    <!-- Connector list -->
    <section
      v-else
      class="td-int__list"
      aria-label="Configured connectors"
    >
      <div
        v-for="connector in connectors"
        :key="connector.id"
        class="td-int__card"
        :class="{ 'td-int__card--selected': selectedId === connector.id }"
      >
        <div
          class="td-int__card-header"
          role="button"
          tabindex="0"
          :aria-expanded="selectedId === connector.id"
          @click="handleSelectConnector(connector)"
          @keydown.enter="handleSelectConnector(connector)"
          @keydown.space.prevent="handleSelectConnector(connector)"
        >
          <div class="td-int__card-info">
            <h3 class="td-int__card-name">{{ connector.name }}</h3>
            <div class="td-int__card-meta">
              <span
                class="td-int__badge"
                :class="statusBadgeClass(connector.status)"
              >
                {{ connector.status }}
              </span>
              <span
                class="td-int__dir"
                :class="directionBadgeClass(connector.direction)"
              >
                {{ ConnectorDirectionLabels[connector.direction] || connector.direction }}
              </span>
              <span class="td-int__type">
                {{ ConnectorTypeLabels[connector.connectorType] || connector.connectorType }}
              </span>
            </div>
          </div>
          <div class="td-int__card-actions">
            <button
              class="td-int__toggle-btn"
              :title="connector.status === 'Active' ? 'Disable' : 'Enable'"
              :aria-label="connector.status === 'Active' ? 'Disable connector' : 'Enable connector'"
              @click.stop="handleToggle(connector)"
            >
              {{ connector.status === 'Active' ? 'Disable' : 'Enable' }}
            </button>
            <button
              class="td-int__delete-btn"
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
          class="td-int__detail"
        >
          <div v-if="detailLoading" class="td-int__detail-loading" role="status">
            Loading details...
          </div>
          <template v-else-if="detail">
            <div class="td-int__detail-section">
              <h4 class="td-int__detail-heading">Configuration</h4>
              <pre v-if="detail.configuration" class="td-int__config-pre">{{ detail.configuration }}</pre>
              <p v-else class="td-int__config-empty">No configuration set.</p>
            </div>
            <div class="td-int__detail-section">
              <h4 class="td-int__detail-heading">Recent Events</h4>
              <div v-if="detail.recentEvents.length" class="td-int__events">
                <div
                  v-for="event in detail.recentEvents"
                  :key="event.id"
                  class="td-int__event"
                >
                  <span class="td-int__event-type">{{ event.eventType }}</span>
                  <span class="td-int__event-date">{{ formatDate(event.createdAt) }}</span>
                  <span v-if="event.payload" class="td-int__event-payload">{{ event.payload }}</span>
                </div>
              </div>
              <p v-else class="td-int__events-empty">No events recorded yet.</p>
            </div>
            <div class="td-int__detail-section">
              <h4 class="td-int__detail-heading">Timestamps</h4>
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
.td-int {
  max-width: 52rem;
  margin: 0 auto;
  padding: 1.5rem;
}

.td-int__hero {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.td-int__eyebrow {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--td-text-tertiary, #9ca3af);
}

.td-page-title {
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0.25rem 0 0.5rem;
  color: var(--td-text-primary, #111827);
}

.td-int__subtitle {
  font-size: 0.875rem;
  color: var(--td-text-secondary, #6b7280);
  line-height: 1.5;
  max-width: 32rem;
}

.td-int__add-btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 0.375rem;
  background: var(--td-primary, #3b82f6);
  color: white;
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
  flex-shrink: 0;
}

.td-int__add-btn:hover:not(:disabled) {
  background: var(--td-primary-hover, #2563eb);
}

.td-int__add-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Form */
.td-int__form {
  background: var(--td-surface, #f9fafb);
  border: 1px solid var(--td-border, #e5e7eb);
  border-radius: 0.5rem;
  padding: 1.25rem;
  margin-bottom: 1.5rem;
}

.td-int__form-title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 1rem;
}

.td-int__form-row {
  margin-bottom: 0.75rem;
}

.td-int__label {
  display: block;
  font-size: 0.8125rem;
  font-weight: 500;
  margin-bottom: 0.25rem;
  color: var(--td-text-secondary, #6b7280);
}

.td-int__input,
.td-int__select,
.td-int__textarea {
  width: 100%;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--td-border, #d1d5db);
  border-radius: 0.375rem;
  font-size: 0.875rem;
  background: var(--td-bg, white);
  color: var(--td-text-primary, #111827);
  box-sizing: border-box;
}

.td-int__textarea {
  resize: vertical;
  font-family: monospace;
}

.td-int__form-actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 1rem;
}

.td-int__btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 0.375rem;
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
}

.td-int__btn--primary {
  background: var(--td-primary, #3b82f6);
  color: white;
}

.td-int__btn--primary:hover:not(:disabled) {
  background: var(--td-primary-hover, #2563eb);
}

.td-int__btn--primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.td-int__btn--secondary {
  background: var(--td-surface, #f3f4f6);
  color: var(--td-text-primary, #374151);
  border: 1px solid var(--td-border, #d1d5db);
}

.td-int__btn--secondary:hover {
  background: var(--td-surface-hover, #e5e7eb);
}

/* States */
.td-int__loading,
.td-int__error,
.td-int__empty {
  text-align: center;
  padding: 3rem 1rem;
  color: var(--td-text-secondary, #6b7280);
}

.td-int__error {
  color: var(--td-error, #ef4444);
}

.td-int__empty-title {
  font-size: 1rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
  color: var(--td-text-primary, #374151);
}

.td-int__empty-desc {
  font-size: 0.875rem;
  max-width: 28rem;
  margin: 0 auto 1rem;
  line-height: 1.5;
}

/* Connector list */
.td-int__list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.td-int__card {
  border: 1px solid var(--td-border, #e5e7eb);
  border-radius: 0.5rem;
  background: var(--td-bg, white);
  overflow: hidden;
}

.td-int__card--selected {
  border-color: var(--td-primary, #3b82f6);
}

.td-int__card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1rem;
  cursor: pointer;
  gap: 0.75rem;
}

.td-int__card-header:hover {
  background: var(--td-surface-hover, #f9fafb);
}

.td-int__card-name {
  font-size: 0.9375rem;
  font-weight: 600;
  margin: 0 0 0.25rem;
}

.td-int__card-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
  font-size: 0.75rem;
}

.td-int__badge {
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-weight: 500;
  font-size: 0.6875rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.td-int__badge--active {
  background: #d1fae5;
  color: #065f46;
}

.td-int__badge--disabled {
  background: #f3f4f6;
  color: #6b7280;
}

.td-int__badge--error {
  background: #fee2e2;
  color: #991b1b;
}

.td-int__dir {
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-weight: 500;
  font-size: 0.6875rem;
}

.td-int__dir--inbound {
  background: #dbeafe;
  color: #1e40af;
}

.td-int__dir--context {
  background: #fef3c7;
  color: #92400e;
}

.td-int__dir--outbound {
  background: #ede9fe;
  color: #5b21b6;
}

.td-int__type {
  color: var(--td-text-tertiary, #9ca3af);
}

.td-int__card-actions {
  display: flex;
  gap: 0.375rem;
  flex-shrink: 0;
}

.td-int__toggle-btn,
.td-int__delete-btn {
  padding: 0.25rem 0.625rem;
  border: 1px solid var(--td-border, #d1d5db);
  border-radius: 0.25rem;
  background: var(--td-bg, white);
  font-size: 0.75rem;
  cursor: pointer;
  color: var(--td-text-secondary, #374151);
}

.td-int__toggle-btn:hover {
  background: var(--td-surface, #f3f4f6);
}

.td-int__delete-btn:hover {
  background: #fee2e2;
  color: #991b1b;
  border-color: #fca5a5;
}

/* Detail panel */
.td-int__detail {
  border-top: 1px solid var(--td-border, #e5e7eb);
  padding: 1rem;
  background: var(--td-surface, #f9fafb);
}

.td-int__detail-loading {
  text-align: center;
  color: var(--td-text-secondary, #6b7280);
  font-size: 0.875rem;
  padding: 1rem 0;
}

.td-int__detail-section {
  margin-bottom: 1rem;
}

.td-int__detail-section:last-child {
  margin-bottom: 0;
}

.td-int__detail-heading {
  font-size: 0.8125rem;
  font-weight: 600;
  margin: 0 0 0.375rem;
  color: var(--td-text-secondary, #6b7280);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.td-int__config-pre {
  background: var(--td-bg, white);
  border: 1px solid var(--td-border, #e5e7eb);
  border-radius: 0.25rem;
  padding: 0.5rem 0.75rem;
  font-size: 0.8125rem;
  white-space: pre-wrap;
  word-break: break-all;
  overflow: auto;
  max-height: 10rem;
}

.td-int__config-empty {
  font-size: 0.8125rem;
  color: var(--td-text-tertiary, #9ca3af);
  font-style: italic;
}

.td-int__events {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.td-int__event {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: baseline;
  font-size: 0.8125rem;
  padding: 0.375rem 0;
  border-bottom: 1px solid var(--td-border-light, #f3f4f6);
}

.td-int__event:last-child {
  border-bottom: none;
}

.td-int__event-type {
  font-weight: 500;
  color: var(--td-text-primary, #111827);
}

.td-int__event-date {
  color: var(--td-text-tertiary, #9ca3af);
  font-size: 0.75rem;
}

.td-int__event-payload {
  color: var(--td-text-secondary, #6b7280);
  font-size: 0.75rem;
  word-break: break-all;
}

.td-int__events-empty {
  font-size: 0.8125rem;
  color: var(--td-text-tertiary, #9ca3af);
  font-style: italic;
}

.td-int__detail-section p {
  font-size: 0.8125rem;
  color: var(--td-text-secondary, #6b7280);
  margin: 0.125rem 0;
}
</style>
