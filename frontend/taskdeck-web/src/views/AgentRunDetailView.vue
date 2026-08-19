<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAgentStore } from '../store/agentStore'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
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
  const proposalId = run.value?.proposalId
  if (!proposalId) return

  void router.push({
    name: 'workspace-review',
    query: run.value.boardId ? { boardId: run.value.boardId } : undefined,
    hash: `#proposal-${encodeURIComponent(proposalId)}`,
  })
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
  return `paper-run-status--${runStatusVariant[status] ?? 'neutral'}`
}

function isQueuedStatus(status: AgentRunStatus): boolean {
  return status === 'Queued'
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
  <div class="paper-run-detail">
    <header class="paper-run-detail__header">
      <button
        class="paper-run-detail__back"
        aria-label="Back to runs"
        @click="goBack"
      >
        &larr; {{ agentName }} runs
      </button>

      <!-- Loading state -->
      <div v-if="agentStore.runDetailLoading" class="paper-run-detail__state" role="status">
        <div class="paper-run-detail__spinner" aria-hidden="true" />
        <span>Loading run detail...</span>
      </div>

      <!-- Error state -->
      <div
        v-else-if="agentStore.runDetailError"
        class="paper-run-detail__state paper-run-detail__state--error"
        role="alert"
      >
        <p>{{ agentStore.runDetailError }}</p>
        <PaperHLBtn variant="ember" @click="agentStore.fetchRunDetail(agentId, runId)">
          Retry
        </PaperHLBtn>
      </div>

      <!-- Run header (when loaded) -->
      <template v-else-if="run">
        <span class="tk-eyebrow paper-run-detail__eyebrow">Run Detail</span>
        <h1 class="tk-h2 paper-run-detail__title">{{ run.objective }}</h1>

        <div class="paper-run-detail__summary-bar">
          <span class="paper-run-status" :class="getStatusClass(run.status)">
            {{ getStatusLabel(run.status) }}
          </span>
          <span class="paper-run-detail__meta">Trigger: {{ run.triggerType }}</span>
          <span class="paper-run-detail__meta">Steps: {{ run.stepsExecuted }}</span>
          <span class="paper-run-detail__meta">Tokens: {{ run.tokensUsed.toLocaleString() }}</span>
          <span v-if="run.approxCostUsd !== null" class="paper-run-detail__meta">
            Cost: ${{ run.approxCostUsd.toFixed(4) }}
          </span>
          <span v-if="isQueuedStatus(run.status)" class="paper-run-detail__meta">
            Requested: {{ formatDate(run.startedAt) }}
          </span>
          <span v-else class="paper-run-detail__meta">Started: {{ formatDate(run.startedAt) }}</span>
          <span v-if="run.completedAt" class="paper-run-detail__meta">
            Completed: {{ formatDate(run.completedAt) }}
          </span>
        </div>

        <p v-if="run.summary" class="paper-run-detail__run-summary">{{ run.summary }}</p>
        <p v-if="run.failureReason" class="paper-run-detail__failure">{{ run.failureReason }}</p>
        <p
          v-if="isQueuedStatus(run.status)"
          class="paper-run-detail__queued-note"
          role="status"
        >
          Queued by the API. Execution has not started.
        </p>

        <PaperHLBtn
          v-if="run.proposalId"
          variant="ghost"
          class="paper-run-detail__proposal-link"
          @click="goToProposal"
        >
          View linked proposal
        </PaperHLBtn>
      </template>
    </header>

    <!-- Timeline -->
    <section
      v-if="run && !agentStore.runDetailLoading"
      class="paper-run-detail__timeline"
      aria-label="Run event timeline"
    >
      <h2 class="tk-h3 paper-run-detail__timeline-title">Timeline</h2>

      <div v-if="sortedEvents.length === 0" class="paper-run-detail__timeline-empty">
        <p>No events recorded for this run.</p>
      </div>

      <ol v-else class="paper-timeline" role="list">
        <li
          v-for="event in timelineItems"
          :key="event.id"
          class="paper-timeline__item"
          role="listitem"
        >
          <div class="paper-timeline__marker" aria-hidden="true" />
          <div class="paper-timeline__content">
            <div class="paper-timeline__header">
              <span class="paper-timeline__event-type">{{ event.eventLabel }}</span>
              <time
                class="paper-timeline__time"
                :datetime="event.timestamp"
                :title="formatDate(event.timestamp)"
              >
                {{ formatTimestamp(event.timestamp) }}
              </time>
            </div>
            <div class="paper-timeline__seq">{{ event.sequenceLabel }}</div>
            <pre
              v-if="event.payloadText"
              class="paper-timeline__payload"
            >{{ event.payloadText }}</pre>
          </div>
        </li>
      </ol>

      <div
        v-if="run && !isQueuedStatus(run.status) && !isTerminalStatus(run.status)"
        class="paper-run-detail__live-indicator"
        role="status"
        aria-live="polite"
      >
        Run is in progress. Refresh to see latest events.
      </div>
    </section>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — AgentRunDetailView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell. */

.paper-run-detail {
  max-width: 860px;
  font-family: var(--sans, system-ui, sans-serif);
  /* Legacy ("off") mode: Paper vars are scoped to .paper/.paper-night, so a root
     that sets --ink must paint --paper alongside it or the near-black fallback
     lands on AppShell's Obsidian surface. No-op inside the Paper shell. */
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

.paper-run-detail__header {
  margin-bottom: var(--s-6, 24px);
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  align-items: flex-start;
}

.paper-run-detail__eyebrow { color: var(--ember, #a8421f); }
.paper-run-detail__title { margin: 0; font-size: var(--t-h2, 32px); }

.paper-run-detail__back {
  background: none;
  border: none;
  color: var(--ember, #a8421f);
  cursor: pointer;
  font-family: inherit;
  font-size: var(--t-sm, 12px);
  padding: 0;
  margin-bottom: var(--s-2, 8px);
  text-align: left;
}

.paper-run-detail__back:hover { text-decoration: underline; }
.paper-run-detail__back:focus-visible {
  outline: 2px solid var(--ember, #a8421f);
  outline-offset: 2px;
  border-radius: var(--r-1, 2px);
}

.paper-run-detail__state {
  padding: var(--s-8, 32px);
  text-align: center;
  color: var(--mute, #635c4e);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--s-4, 16px);
  width: 100%;
}

.paper-run-detail__state--error { color: var(--overdue, #8c4a26); }

.paper-run-detail__spinner {
  width: 24px;
  height: 24px;
  border: 3px solid var(--line, #d8d0bf);
  border-top-color: var(--ember, #a8421f);
  border-radius: 50%;
  animation: paper-run-detail-spin 0.8s linear infinite;
}

@keyframes paper-run-detail-spin { to { transform: rotate(360deg); } }

.paper-run-detail__summary-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--s-4, 16px);
  margin-top: var(--s-2, 8px);
}

.paper-run-detail__meta {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #635c4e);
  white-space: nowrap;
}

.paper-run-detail__run-summary { margin: var(--s-2, 8px) 0 0; color: var(--ink-2, #3a352d); font-size: var(--t-md, 13.5px); line-height: 1.5; }
.paper-run-detail__failure { margin: var(--s-2, 8px) 0 0; color: var(--overdue, #8c4a26); font-size: var(--t-md, 13.5px); line-height: 1.5; }
.paper-run-detail__queued-note { margin: var(--s-2, 8px) 0 0; color: var(--mute, #635c4e); font-size: var(--t-md, 13.5px); line-height: 1.5; }
.paper-run-detail__proposal-link { margin-top: var(--s-2, 8px); }

/* Run status badge — mirrors AgentRunsView */
.paper-run-status {
  font-size: var(--t-xs, 10.5px);
  font-weight: 700;
  padding: var(--s-1, 4px) var(--s-3, 12px);
  border-radius: var(--r-1, 2px);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  white-space: nowrap;
  flex-shrink: 0;
}

.paper-run-status--success { background: var(--applied-tint, #d8e0ce); color: var(--applied, #4a6b3f); }
.paper-run-status--error { background: var(--ember-bloom, #a8421f1a); color: var(--ember-deep, #7a2e15); }
.paper-run-status--warning { background: var(--overdue-tint, #ecd9c4); color: var(--overdue, #8c4a26); }
.paper-run-status--info { background: var(--ember-tint, #f0d9c8); color: var(--ember-ink, #6e2810); }
.paper-run-status--neutral { background: var(--paper-2, #ebe5d8); color: var(--mute, #635c4e); }

/* Timeline */
.paper-run-detail__timeline { margin-top: var(--s-6, 24px); }
.paper-run-detail__timeline-title { margin: 0 0 var(--s-4, 16px); font-size: var(--t-lg, 18px); }
.paper-run-detail__timeline-empty { color: var(--mute, #635c4e); padding: var(--s-6, 24px); text-align: center; }

.paper-timeline { list-style: none; padding: 0; margin: 0; position: relative; }
.paper-timeline::before {
  content: '';
  position: absolute;
  left: 7px;
  top: 0;
  bottom: 0;
  width: 2px;
  background: var(--line, #d8d0bf);
}

.paper-timeline__item { position: relative; padding-left: var(--s-8, 32px); padding-bottom: var(--s-5, 20px); }
.paper-timeline__item:last-child { padding-bottom: 0; }

.paper-timeline__marker {
  position: absolute;
  left: 2px;
  top: 4px;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: var(--ember, #a8421f);
  border: 2px solid var(--paper-card, #fbf7ee);
  z-index: 1;
}

.paper-timeline__content {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-4, 16px);
}

.paper-timeline__header { display: flex; align-items: center; justify-content: space-between; gap: var(--s-3, 12px); }
.paper-timeline__event-type { font-weight: 600; color: var(--ink-deep, #0a0908); font-size: var(--t-md, 13.5px); }
.paper-timeline__time { font-family: var(--mono, ui-monospace, monospace); font-size: var(--t-xs, 10.5px); color: var(--mute, #635c4e); white-space: nowrap; }
.paper-timeline__seq { font-family: var(--mono, ui-monospace, monospace); font-size: var(--t-xs, 10.5px); color: var(--mute, #635c4e); margin-top: var(--s-1, 4px); }

.paper-timeline__payload {
  margin-top: var(--s-3, 12px);
  padding: var(--s-3, 12px);
  background: var(--paper-2, #ebe5d8);
  border-radius: var(--r-2, 4px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--ink-2, #3a352d);
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 200px;
  overflow-y: auto;
}

.paper-run-detail__live-indicator {
  margin-top: var(--s-4, 16px);
  padding: var(--s-3, 12px) var(--s-4, 16px);
  background: var(--ember-bloom, #a8421f1a);
  border-radius: var(--r-3, 6px);
  color: var(--ember-deep, #7a2e15);
  font-size: var(--t-md, 13.5px);
  text-align: center;
}
</style>
