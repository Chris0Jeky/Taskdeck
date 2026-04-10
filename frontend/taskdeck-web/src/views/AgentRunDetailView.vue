<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAgentStore } from '../store/agentStore'
import { runStatusLabels, runStatusVariant, isTerminalStatus } from '../types/agent'
import type { AgentRunStatus, AgentRunEvent } from '../types/agent'

const route = useRoute()
const router = useRouter()
const agentStore = useAgentStore()

const agentId = computed(() => {
  const raw = route.params.agentId
  return typeof raw === 'string' ? raw : ''
})

const runId = computed(() => {
  const raw = route.params.runId
  return typeof raw === 'string' ? raw : ''
})

const run = computed(() => agentStore.runDetail)

const agentName = computed(() => {
  const match = agentStore.profiles.find((p) => p.id === agentId.value)
  return match?.name ?? 'Agent'
})

const sortedEvents = computed<AgentRunEvent[]>(() => {
  if (!run.value?.events) return []
  return [...run.value.events].sort((a, b) => a.sequenceNumber - b.sequenceNumber)
})

const timelineItems = computed(() =>
  sortedEvents.value.map((event) => {
    const parsedPayload = parsePayloadSafe(event.payload)

    return {
      ...event,
      eventLabel: describeEvent(event),
      sequenceLabel: `Sequence ${event.sequenceNumber + 1}`,
      payloadText: parsedPayload ? JSON.stringify(parsedPayload, null, 2) : null,
    }
  }),
)

onMounted(async () => {
  try {
    if (agentStore.profiles.length === 0) {
      await agentStore.fetchProfiles()
    }
    if (agentId.value && runId.value) {
      await agentStore.fetchRunDetail(agentId.value, runId.value)
    }
  } catch {
    // Error is surfaced via store state and toast
  }
})

watch([agentId, runId], async (newValues, oldValues) => {
  const [newAgentId, newRunId] = newValues
  const [oldAgentId, oldRunId] = oldValues ?? []

  if (!newAgentId || !newRunId || (newAgentId === oldAgentId && newRunId === oldRunId)) {
    return
  }

  agentStore.clearRunDetail()

  try {
    await agentStore.fetchRunDetail(newAgentId, newRunId)
  } catch {
    // Error is surfaced via store state and toast
  }
})

onUnmounted(() => {
  agentStore.clearRunDetail()
})

function goBack() {
  void router.push(`/workspace/agents/${agentId.value}/runs`)
}

function goToProposal() {
  if (run.value?.proposalId) {
    void router.push({
      path: '/workspace/review',
      query: { proposalId: run.value.proposalId },
    })
  }
}

function formatDate(isoDate: string): string {
  return new Date(isoDate).toLocaleString()
}

function formatTimestamp(isoDate: string): string {
  return new Date(isoDate).toLocaleTimeString()
}

function getStatusLabel(status: AgentRunStatus): string {
  return runStatusLabels[status] ?? status
}

function getStatusClass(status: AgentRunStatus): string {
  return `td-run-status--${runStatusVariant[status] ?? 'neutral'}`
}

const EVENT_TYPE_LABELS: Record<string, string> = {
  'run.started': 'Run started',
  'context.gathered': 'Context gathered from workspace',
  'plan.created': 'Plan created',
  'step.started': 'Step started',
  'step.completed': 'Step completed',
  'proposal.created': 'Proposal created for review',
  'proposal.approved': 'Proposal approved by user',
  'proposal.rejected': 'Proposal rejected by user',
  'changes.applied': 'Changes applied to board',
  'run.completed': 'Run completed successfully',
  'run.failed': 'Run failed',
  'run.cancelled': 'Run cancelled',
  'error': 'Error occurred',
}

/** Translates raw event types into human-readable product language */
function describeEvent(event: AgentRunEvent): string {
  return EVENT_TYPE_LABELS[event.eventType] ?? event.eventType
}

function parsePayloadSafe(payload: string): Record<string, unknown> | null {
  try {
    const parsed = JSON.parse(payload)
    if (typeof parsed === 'object' && parsed !== null && Object.keys(parsed).length > 0) {
      return parsed as Record<string, unknown>
    }
    return null
  } catch {
    return null
  }
}
</script>

<template>
  <div class="td-run-detail">
    <header class="td-run-detail__header">
      <button
        class="td-run-detail__back"
        aria-label="Back to runs"
        @click="goBack"
      >
        &larr; {{ agentName }} runs
      </button>

      <!-- Loading state -->
      <div v-if="agentStore.runDetailLoading" class="td-run-detail__state" role="status">
        <div class="td-run-detail__spinner" aria-hidden="true" />
        <span>Loading run detail...</span>
      </div>

      <!-- Error state -->
      <div
        v-else-if="agentStore.runDetailError"
        class="td-run-detail__state td-run-detail__state--error"
        role="alert"
      >
        <p>{{ agentStore.runDetailError }}</p>
        <button
          class="td-btn td-btn--primary td-btn--sm"
          @click="agentStore.fetchRunDetail(agentId, runId)"
        >
          Retry
        </button>
      </div>

      <!-- Run header (when loaded) -->
      <template v-else-if="run">
        <span class="td-run-detail__eyebrow">Run Detail</span>
        <h1 class="td-page-title">{{ run.objective }}</h1>

        <div class="td-run-detail__summary-bar">
          <span class="td-run-status" :class="getStatusClass(run.status)">
            {{ getStatusLabel(run.status) }}
          </span>
          <span class="td-run-detail__meta">Trigger: {{ run.triggerType }}</span>
          <span class="td-run-detail__meta">Steps: {{ run.stepsExecuted }}</span>
          <span class="td-run-detail__meta">Tokens: {{ run.tokensUsed.toLocaleString() }}</span>
          <span v-if="run.approxCostUsd !== null" class="td-run-detail__meta">
            Cost: ${{ run.approxCostUsd.toFixed(4) }}
          </span>
          <span class="td-run-detail__meta">Started: {{ formatDate(run.startedAt) }}</span>
          <span v-if="run.completedAt" class="td-run-detail__meta">
            Completed: {{ formatDate(run.completedAt) }}
          </span>
        </div>

        <p v-if="run.summary" class="td-run-detail__run-summary">{{ run.summary }}</p>
        <p v-if="run.failureReason" class="td-run-detail__failure">{{ run.failureReason }}</p>

        <button
          v-if="run.proposalId"
          class="td-btn td-btn--ghost td-btn--sm td-run-detail__proposal-link"
          @click="goToProposal"
        >
          View linked proposal
        </button>
      </template>
    </header>

    <!-- Timeline -->
    <section
      v-if="run && !agentStore.runDetailLoading"
      class="td-run-detail__timeline"
      aria-label="Run event timeline"
    >
      <h2 class="td-run-detail__timeline-title">Timeline</h2>

      <div v-if="sortedEvents.length === 0" class="td-run-detail__timeline-empty">
        <p>No events recorded for this run.</p>
      </div>

      <ol v-else class="td-timeline" role="list">
        <li
          v-for="event in timelineItems"
          :key="event.id"
          class="td-timeline__item"
          role="listitem"
        >
          <div class="td-timeline__marker" aria-hidden="true" />
          <div class="td-timeline__content">
            <div class="td-timeline__header">
              <span class="td-timeline__event-type">{{ event.eventLabel }}</span>
              <time
                class="td-timeline__time"
                :datetime="event.timestamp"
                :title="formatDate(event.timestamp)"
              >
                {{ formatTimestamp(event.timestamp) }}
              </time>
            </div>
            <div class="td-timeline__seq">{{ event.sequenceLabel }}</div>
            <pre
              v-if="event.payloadText"
              class="td-timeline__payload"
            >{{ event.payloadText }}</pre>
          </div>
        </li>
      </ol>

      <div
        v-if="run && !isTerminalStatus(run.status)"
        class="td-run-detail__live-indicator"
        role="status"
        aria-live="polite"
      >
        Run is in progress. Refresh to see latest events.
      </div>
    </section>
  </div>
</template>

<style scoped>
.td-run-detail { max-width: 860px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; color: var(--td-text-primary); }
.td-run-detail__header { margin-bottom: var(--td-space-6); display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-run-detail__eyebrow { font-size: var(--td-font-xs); font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--td-text-tertiary); }

.td-run-detail__back { background: none; border: none; color: var(--td-color-ember); cursor: pointer; font-size: var(--td-font-sm); padding: 0; margin-bottom: var(--td-space-2); text-align: left; }
.td-run-detail__back:hover { text-decoration: underline; }
.td-run-detail__back:focus-visible { box-shadow: var(--td-focus-ring); outline: none; }

.td-run-detail__state { padding: var(--td-space-8); text-align: center; color: var(--td-text-secondary); display: flex; flex-direction: column; align-items: center; gap: var(--td-space-4); }
.td-run-detail__state--error { color: var(--td-color-error); }
.td-run-detail__spinner { width: 24px; height: 24px; border: 3px solid var(--td-border-ghost); border-top-color: var(--td-color-ember); border-radius: 50%; animation: td-spin 0.8s linear infinite; }
@keyframes td-spin { to { transform: rotate(360deg); } }

.td-run-detail__summary-bar { display: flex; flex-wrap: wrap; align-items: center; gap: var(--td-space-4); margin-top: var(--td-space-2); }
.td-run-detail__meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); white-space: nowrap; }
.td-run-detail__run-summary { color: var(--td-text-secondary); font-size: var(--td-font-sm); line-height: 1.5; margin-top: var(--td-space-2); }
.td-run-detail__failure { color: var(--td-color-error); font-size: var(--td-font-sm); line-height: 1.5; margin-top: var(--td-space-2); }
.td-run-detail__proposal-link { margin-top: var(--td-space-2); }

/* Run status badge - shared with AgentRunsView */
.td-run-status { font-size: var(--td-font-xs); font-weight: 700; padding: var(--td-space-1) var(--td-space-3); border-radius: 9999px; text-transform: uppercase; letter-spacing: 0.05em; white-space: nowrap; flex-shrink: 0; }
.td-run-status--success { background: var(--td-color-success-dim, rgba(34, 197, 94, 0.15)); color: var(--td-color-success, #22c55e); }
.td-run-status--error { background: var(--td-color-error-dim, rgba(239, 68, 68, 0.15)); color: var(--td-color-error, #ef4444); }
.td-run-status--warning { background: var(--td-color-warning-dim, rgba(234, 179, 8, 0.15)); color: var(--td-color-warning, #eab308); }
.td-run-status--info { background: var(--td-color-info-dim, rgba(59, 130, 246, 0.15)); color: var(--td-color-info, #3b82f6); }
.td-run-status--neutral { background: var(--td-surface-container-high); color: var(--td-text-tertiary); }

/* Timeline */
.td-run-detail__timeline { margin-top: var(--td-space-6); }
.td-run-detail__timeline-title { font-size: var(--td-font-lg); font-weight: 700; color: var(--td-text-primary); margin-bottom: var(--td-space-4); }
.td-run-detail__timeline-empty { color: var(--td-text-secondary); padding: var(--td-space-6); text-align: center; }

.td-timeline { list-style: none; padding: 0; margin: 0; position: relative; }
.td-timeline::before { content: ''; position: absolute; left: 7px; top: 0; bottom: 0; width: 2px; background: var(--td-border-ghost); }

.td-timeline__item { position: relative; padding-left: var(--td-space-8); padding-bottom: var(--td-space-5); }
.td-timeline__item:last-child { padding-bottom: 0; }

.td-timeline__marker { position: absolute; left: 2px; top: 4px; width: 12px; height: 12px; border-radius: 50%; background: var(--td-color-ember); border: 2px solid var(--td-surface-container); z-index: 1; }

.td-timeline__content { background: var(--td-surface-container); border: 1px solid var(--td-border-ghost); border-radius: var(--td-radius-md); padding: var(--td-space-4); }

.td-timeline__header { display: flex; align-items: center; justify-content: space-between; gap: var(--td-space-3); }
.td-timeline__event-type { font-weight: 600; color: var(--td-text-primary); font-size: var(--td-font-sm); }
.td-timeline__time { font-size: var(--td-font-xs); color: var(--td-text-tertiary); white-space: nowrap; }
.td-timeline__seq { font-size: var(--td-font-xs); color: var(--td-text-tertiary); margin-top: var(--td-space-1); }

.td-timeline__payload { margin-top: var(--td-space-3); padding: var(--td-space-3); background: var(--td-surface-container-high); border-radius: var(--td-radius-sm); font-size: var(--td-font-xs); color: var(--td-text-secondary); overflow-x: auto; white-space: pre-wrap; word-break: break-word; max-height: 200px; overflow-y: auto; }

.td-run-detail__live-indicator { margin-top: var(--td-space-4); padding: var(--td-space-3) var(--td-space-4); background: var(--td-color-info-dim, rgba(59, 130, 246, 0.1)); border-radius: var(--td-radius-md); color: var(--td-color-info, #3b82f6); font-size: var(--td-font-sm); text-align: center; }
</style>
