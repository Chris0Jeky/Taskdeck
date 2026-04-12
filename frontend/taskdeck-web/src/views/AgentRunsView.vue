<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAgentStore } from '../store/agentStore'
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
  return `td-run-status--${runStatusVariant[status] ?? 'neutral'}`
}
</script>

<template>
  <div class="td-agent-runs">
    <header class="td-agent-runs__header">
      <div class="td-agent-runs__header-copy">
        <button
          class="td-agent-runs__back"
          aria-label="Back to agents"
          @click="goBack"
        >
          &larr; Agents
        </button>
        <span class="td-agent-runs__eyebrow">Agent Runs</span>
        <h1 class="td-page-title">{{ agentName }}</h1>
        <p class="td-agent-runs__subtitle">
          Each run represents a single agent execution: its objective, status, and outcome.
        </p>
      </div>
    </header>

    <!-- Loading state -->
    <div v-if="agentStore.runsLoading" class="td-agent-runs__state" role="status">
      <div class="td-agent-runs__spinner" aria-hidden="true" />
      <span>Loading runs...</span>
    </div>

    <!-- Error state -->
    <div
      v-else-if="agentStore.runsError"
      class="td-agent-runs__state td-agent-runs__state--error"
      role="alert"
    >
      <p>{{ agentStore.runsError }}</p>
      <button
        class="td-btn td-btn--primary td-btn--sm"
        @click="agentStore.fetchRuns(agentId)"
      >
        Retry
      </button>
    </div>

    <!-- Empty state -->
    <div
      v-else-if="agentStore.runs.length === 0"
      class="td-agent-runs__state td-agent-runs__state--empty"
    >
      <p class="td-agent-runs__empty-title">No runs yet</p>
      <p class="td-agent-runs__empty-body">
        This agent has not been triggered. Runs are created when the agent is invoked
        via the API or an automation trigger.
      </p>
    </div>

    <!-- Run list -->
    <ul v-else class="td-agent-runs__list" role="list" aria-label="Agent runs">
      <li
        v-for="run in agentStore.runs"
        :key="run.id"
        class="td-agent-runs__card"
        role="listitem"
      >
        <button
          class="td-agent-runs__card-btn"
          :aria-label="`View run detail: ${run.objective}`"
          @click="openRunDetail(run.id)"
        >
          <div class="td-agent-runs__card-top">
            <span class="td-agent-runs__objective">{{ run.objective }}</span>
            <span class="td-run-status" :class="getStatusClass(run.status)">
              {{ getStatusLabel(run.status) }}
            </span>
          </div>

          <p v-if="run.summary" class="td-agent-runs__summary">
            {{ run.summary }}
          </p>
          <p v-if="run.failureReason" class="td-agent-runs__failure">
            {{ run.failureReason }}
          </p>

          <div class="td-agent-runs__meta">
            <span>Trigger: {{ run.triggerType }}</span>
            <span>Steps: {{ run.stepsExecuted }}</span>
            <span v-if="run.proposalId">Proposal linked</span>
            <span>Started: {{ formatDate(run.startedAt) }}</span>
            <span v-if="run.completedAt">Completed: {{ formatDate(run.completedAt) }}</span>
          </div>
        </button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.td-agent-runs { max-width: 860px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; color: var(--td-text-primary); }
.td-agent-runs__header { margin-bottom: var(--td-space-6); }
.td-agent-runs__header-copy { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-agent-runs__eyebrow { font-size: var(--td-font-xs); font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--td-text-tertiary); }
.td-agent-runs__subtitle { color: var(--td-text-secondary); line-height: 1.6; }

.td-agent-runs__back { background: none; border: none; color: var(--td-color-ember); cursor: pointer; font-size: var(--td-font-sm); padding: 0; margin-bottom: var(--td-space-2); text-align: left; }
.td-agent-runs__back:hover { text-decoration: underline; }
.td-agent-runs__back:focus-visible { box-shadow: var(--td-focus-ring); outline: none; }

.td-agent-runs__state { padding: var(--td-space-8); text-align: center; color: var(--td-text-secondary); display: flex; flex-direction: column; align-items: center; gap: var(--td-space-4); }
.td-agent-runs__state--error { color: var(--td-color-error); }
.td-agent-runs__spinner { width: 24px; height: 24px; border: 3px solid var(--td-border-ghost); border-top-color: var(--td-color-ember); border-radius: 50%; animation: td-spin 0.8s linear infinite; }
@keyframes td-spin { to { transform: rotate(360deg); } }

.td-agent-runs__empty-title { font-size: var(--td-font-lg); font-weight: 600; color: var(--td-text-primary); }
.td-agent-runs__empty-body { color: var(--td-text-secondary); max-width: 440px; }

.td-agent-runs__list { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: var(--td-space-3); }

.td-agent-runs__card { background: var(--td-surface-container); border: 1px solid var(--td-border-ghost); border-radius: var(--td-radius-lg); transition: border-color var(--td-transition-fast); }
.td-agent-runs__card:hover { border-color: var(--td-color-ember); }

.td-agent-runs__card-btn { display: flex; flex-direction: column; gap: var(--td-space-2); width: 100%; padding: var(--td-space-5); background: transparent; border: none; cursor: pointer; text-align: left; color: inherit; font-family: inherit; }
.td-agent-runs__card-btn:focus-visible { box-shadow: var(--td-focus-ring); outline: none; border-radius: var(--td-radius-lg); }

.td-agent-runs__card-top { display: flex; align-items: center; justify-content: space-between; gap: var(--td-space-3); }
.td-agent-runs__objective { font-size: var(--td-font-base); font-weight: 600; color: var(--td-text-primary); flex: 1; }
.td-agent-runs__summary { font-size: var(--td-font-sm); color: var(--td-text-secondary); line-height: 1.5; }
.td-agent-runs__failure { font-size: var(--td-font-sm); color: var(--td-color-error); line-height: 1.5; }
.td-agent-runs__meta { display: flex; flex-wrap: wrap; gap: var(--td-space-4); font-size: var(--td-font-xs); color: var(--td-text-tertiary); }

.td-run-status { font-size: var(--td-font-xs); font-weight: 700; padding: var(--td-space-1) var(--td-space-3); border-radius: 9999px; text-transform: uppercase; letter-spacing: 0.05em; white-space: nowrap; flex-shrink: 0; }
.td-run-status--success { background: var(--td-color-success-dim, rgba(34, 197, 94, 0.15)); color: var(--td-color-success, #22c55e); }
.td-run-status--error { background: var(--td-color-error-dim, rgba(239, 68, 68, 0.15)); color: var(--td-color-error, #ef4444); }
.td-run-status--warning { background: var(--td-color-warning-dim, rgba(234, 179, 8, 0.15)); color: var(--td-color-warning, #eab308); }
.td-run-status--info { background: var(--td-color-info-dim, rgba(59, 130, 246, 0.15)); color: var(--td-color-info, #3b82f6); }
.td-run-status--neutral { background: var(--td-surface-container-high); color: var(--td-text-tertiary); }
</style>
