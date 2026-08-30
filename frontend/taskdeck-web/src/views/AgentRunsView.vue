<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAgentStore } from '../store/agentStore'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import { runStatusLabels, runStatusVariant } from '../types/agent'
import type { AgentRunStatus } from '../types/agent'

const route = useRoute()
const router = useRouter()
const agentStore = useAgentStore()

const agentId = computed(() => {
  const raw = route.params.agentId
  return typeof raw === 'string' ? raw : ''
})

const agentName = computed(() => {
  const match = agentStore.profiles.find((p) => p.id === agentId.value)
  return match?.name ?? 'Agent'
})

onMounted(async () => {
  try {
    // Ensure profiles are loaded so we can show the agent name
    if (agentStore.profiles.length === 0) {
      await agentStore.fetchProfiles()
    }
    if (agentId.value) {
      await agentStore.fetchRuns(agentId.value)
    }
  } catch {
    // Error is surfaced via store state and toast
  }
})

watch(agentId, async (newId) => {
  if (newId) {
    agentStore.clearRuns()
    try {
      await agentStore.fetchRuns(newId)
    } catch {
      // surfaced via store
    }
  }
})

function openRunDetail(runId: string) {
  void router.push(`/workspace/agents/${agentId.value}/runs/${runId}`)
}

function goBack() {
  void router.push('/workspace/agents')
}

function formatDate(isoDate: string): string {
  return new Date(isoDate).toLocaleString()
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
</script>

<template>
  <div class="paper-agent-runs">
    <header class="paper-agent-runs__header">
      <div class="paper-agent-runs__header-copy">
        <button
          class="paper-agent-runs__back"
          aria-label="Back to agents"
          @click="goBack"
        >
          &larr; Agents
        </button>
        <span class="tk-eyebrow paper-agent-runs__eyebrow">Agent Runs</span>
        <h1 class="tk-h2 paper-agent-runs__title">{{ agentName }}</h1>
        <p class="tk-lede paper-agent-runs__subtitle">
          Each record shows an agent run request and, once execution begins, its status and outcome.
        </p>
      </div>
    </header>

    <!-- Loading state -->
    <div v-if="agentStore.runsLoading" class="paper-agent-runs__state" role="status">
      <div class="paper-agent-runs__spinner" aria-hidden="true" />
      <span>Loading runs...</span>
    </div>

    <!-- Error state -->
    <div
      v-else-if="agentStore.runsError"
      class="paper-agent-runs__state paper-agent-runs__state--error"
      role="alert"
    >
      <p>{{ agentStore.runsError }}</p>
      <PaperHLBtn variant="ember" @click="agentStore.fetchRuns(agentId)">Retry</PaperHLBtn>
    </div>

    <!-- Empty state -->
    <div
      v-else-if="agentStore.runs.length === 0"
      class="paper-agent-runs__state paper-agent-runs__state--empty"
    >
      <p class="paper-agent-runs__empty-title">No runs yet</p>
      <p class="paper-agent-runs__empty-body">
        This agent has not been invoked. Runs are currently created through the API.
        Automation-trigger execution is planned for a future release.
      </p>
    </div>

    <!-- Run list -->
    <ul v-else class="paper-agent-runs__list" role="list" aria-label="Agent runs">
      <li
        v-for="run in agentStore.runs"
        :key="run.id"
        class="paper-agent-runs__card"
        role="listitem"
      >
        <button
          class="paper-agent-runs__card-btn"
          :aria-label="`View run detail: ${run.objective}`"
          @click="openRunDetail(run.id)"
        >
          <div class="paper-agent-runs__card-top">
            <span class="paper-agent-runs__objective">{{ run.objective }}</span>
            <span class="paper-run-status" :class="getStatusClass(run.status)">
              {{ getStatusLabel(run.status) }}
            </span>
          </div>

          <p v-if="run.summary" class="paper-agent-runs__summary">
            {{ run.summary }}
          </p>
          <p v-if="run.failureReason" class="paper-agent-runs__failure">
            {{ run.failureReason }}
          </p>
          <p v-if="isQueuedStatus(run.status)" class="paper-agent-runs__queued-note">
            Queued by the API. Execution has not started.
          </p>

          <div class="paper-agent-runs__meta">
            <span>Trigger: {{ run.triggerType }}</span>
            <span>Steps: {{ run.stepsExecuted }}</span>
            <span v-if="run.proposalId">Proposal linked</span>
            <span v-if="isQueuedStatus(run.status)">Requested: {{ formatDate(run.startedAt) }}</span>
            <span v-else>Started: {{ formatDate(run.startedAt) }}</span>
            <span v-if="run.completedAt">Completed: {{ formatDate(run.completedAt) }}</span>
          </div>
        </button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — AgentRunsView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell. */

.paper-agent-runs {
  max-width: 860px;
  font-family: var(--sans, system-ui, sans-serif);
  /* Legacy ("off") mode: Paper vars are scoped to .paper/.paper-night, so a root
     that sets --ink must paint --paper alongside it or the near-black fallback
     lands on AppShell's Obsidian surface. No-op inside the Paper shell. */
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

.paper-agent-runs__header { margin-bottom: var(--s-6, 24px); }
.paper-agent-runs__header-copy { display: flex; flex-direction: column; gap: var(--s-2, 8px); }
.paper-agent-runs__eyebrow { color: var(--mute, #635c4e); }
.paper-agent-runs__title { margin: 0; font-size: var(--t-h2, 32px); }
.paper-agent-runs__subtitle { margin: 0; color: var(--ink-2, #3a352d); }

.paper-agent-runs__back {
  background: none;
  border: none;
  color: var(--ember, #a8421f);
  cursor: pointer;
  font-family: inherit;
  font-size: var(--t-sm, 12px);
  padding: 0;
  margin-bottom: var(--s-2, 8px);
  text-align: left;
  align-self: flex-start;
}

.paper-agent-runs__back:hover { text-decoration: underline; }
.paper-agent-runs__back:focus-visible {
  outline: 2px solid var(--ember, #a8421f);
  outline-offset: 2px;
  border-radius: var(--r-1, 2px);
}

.paper-agent-runs__state {
  padding: var(--s-8, 32px);
  text-align: center;
  color: var(--mute, #635c4e);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--s-4, 16px);
}

.paper-agent-runs__state--error { color: var(--overdue, #8c4a26); }

.paper-agent-runs__spinner {
  width: 24px;
  height: 24px;
  border: 3px solid var(--line, #d8d0bf);
  border-top-color: var(--ember, #a8421f);
  border-radius: 50%;
  animation: paper-agent-runs-spin 0.8s linear infinite;
}

@keyframes paper-agent-runs-spin { to { transform: rotate(360deg); } }

.paper-agent-runs__empty-title {
  margin: 0;
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-lg, 18px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-agent-runs__empty-body { margin: 0; color: var(--ink-2, #3a352d); max-width: 440px; }

.paper-agent-runs__list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-agent-runs__card {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-agent-runs__card:hover { border-color: var(--ember, #a8421f); }

.paper-agent-runs__card-btn {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  width: 100%;
  padding: var(--s-5, 20px);
  background: transparent;
  border: none;
  cursor: pointer;
  text-align: left;
  color: inherit;
  font-family: inherit;
}

.paper-agent-runs__card-btn:focus-visible {
  outline: 2px solid var(--ember, #a8421f);
  outline-offset: -2px;
  border-radius: var(--r-3, 6px);
}

.paper-agent-runs__card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--s-3, 12px);
}

.paper-agent-runs__objective {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
  flex: 1;
}

.paper-agent-runs__summary { margin: 0; font-size: var(--t-md, 13.5px); color: var(--ink-2, #3a352d); line-height: 1.5; }
.paper-agent-runs__failure { margin: 0; font-size: var(--t-md, 13.5px); color: var(--overdue, #8c4a26); line-height: 1.5; }
.paper-agent-runs__queued-note { margin: 0; font-size: var(--t-md, 13.5px); color: var(--mute, #635c4e); line-height: 1.5; }

.paper-agent-runs__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-4, 16px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #635c4e);
}

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
</style>
