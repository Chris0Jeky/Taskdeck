<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAgentStore } from '../store/agentStore'

const router = useRouter()
const agentStore = useAgentStore()

onMounted(async () => {
  try {
    await agentStore.fetchProfiles()
  } catch {
    // Error is surfaced via store state and toast
  }
})

function openRuns(agentId: string) {
  void router.push(`/workspace/agents/${agentId}/runs`)
}
</script>

<template>
  <div class="td-agents">
    <header class="td-agents__header">
      <div class="td-agents__header-copy">
        <span class="td-agents__eyebrow">Agent Mode</span>
        <h1 class="td-page-title">Agents</h1>
        <p class="td-agents__subtitle">
          Agent profiles define what an agent can do, its scope, and its policy constraints.
          Select an agent to view its run history.
        </p>
      </div>
    </header>

    <!-- Loading state -->
    <div v-if="agentStore.profilesLoading" class="td-agents__state" role="status">
      <div class="td-agents__spinner" aria-hidden="true" />
      <span>Loading agents...</span>
    </div>

    <!-- Error state -->
    <div
      v-else-if="agentStore.profilesError"
      class="td-agents__state td-agents__state--error"
      role="alert"
    >
      <p>{{ agentStore.profilesError }}</p>
      <button class="td-btn td-btn--primary td-btn--sm" @click="agentStore.fetchProfiles()">
        Retry
      </button>
    </div>

    <!-- Empty state -->
    <div
      v-else-if="agentStore.profiles.length === 0"
      class="td-agents__state td-agents__state--empty"
    >
      <p class="td-agents__empty-title">No agents configured</p>
      <p class="td-agents__empty-body">
        Agent profiles are created via the API. Once configured, they will appear here
        with their status and run history.
      </p>
    </div>

    <!-- Profile list -->
    <ul v-else class="td-agents__list" role="list" aria-label="Agent profiles">
      <li
        v-for="profile in agentStore.profiles"
        :key="profile.id"
        class="td-agents__card"
        role="listitem"
      >
        <button
          class="td-agents__card-btn"
          :aria-label="`View runs for ${profile.name}`"
          @click="openRuns(profile.id)"
        >
          <div class="td-agents__card-header">
            <span class="td-agents__card-name">{{ profile.name }}</span>
            <span
              class="td-agents__status-badge"
              :class="profile.isEnabled ? 'td-agents__status-badge--active' : 'td-agents__status-badge--disabled'"
            >
              {{ profile.isEnabled ? 'Active' : 'Disabled' }}
            </span>
          </div>
          <p v-if="profile.description" class="td-agents__card-desc">
            {{ profile.description }}
          </p>
          <div class="td-agents__card-meta">
            <span class="td-agents__meta-item">
              Scope: {{ profile.scopeType === 'Board' ? 'Board' : 'Workspace' }}
            </span>
            <span class="td-agents__meta-item">
              Template: {{ profile.templateKey }}
            </span>
          </div>
        </button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.td-agents { max-width: 860px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; color: var(--td-text-primary); }
.td-agents__header { margin-bottom: var(--td-space-6); }
.td-agents__header-copy { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-agents__eyebrow { font-size: var(--td-font-xs); font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--td-text-tertiary); }
.td-agents__subtitle { color: var(--td-text-secondary); line-height: 1.6; }

.td-agents__state { padding: var(--td-space-8); text-align: center; color: var(--td-text-secondary); display: flex; flex-direction: column; align-items: center; gap: var(--td-space-4); }
.td-agents__state--error { color: var(--td-color-error); }
.td-agents__spinner { width: 24px; height: 24px; border: 3px solid var(--td-border-ghost); border-top-color: var(--td-color-ember); border-radius: 50%; animation: td-spin 0.8s linear infinite; }
@keyframes td-spin { to { transform: rotate(360deg); } }

.td-agents__empty-title { font-size: var(--td-font-lg); font-weight: 600; color: var(--td-text-primary); }
.td-agents__empty-body { color: var(--td-text-secondary); max-width: 440px; }

.td-agents__list { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: var(--td-space-3); }

.td-agents__card { background: var(--td-surface-container); border: 1px solid var(--td-border-ghost); border-radius: var(--td-radius-lg); transition: border-color var(--td-transition-fast); }
.td-agents__card:hover { border-color: var(--td-color-ember); }

.td-agents__card-btn { display: flex; flex-direction: column; gap: var(--td-space-2); width: 100%; padding: var(--td-space-5); background: transparent; border: none; cursor: pointer; text-align: left; color: inherit; font-family: inherit; }
.td-agents__card-btn:focus-visible { box-shadow: var(--td-focus-ring); outline: none; border-radius: var(--td-radius-lg); }

.td-agents__card-header { display: flex; align-items: center; gap: var(--td-space-3); }
.td-agents__card-name { font-size: var(--td-font-lg); font-weight: 600; color: var(--td-text-primary); }
.td-agents__card-desc { color: var(--td-text-secondary); font-size: var(--td-font-sm); line-height: 1.5; }
.td-agents__card-meta { display: flex; gap: var(--td-space-4); font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-agents__meta-item { white-space: nowrap; }

.td-agents__status-badge { font-size: var(--td-font-xs); font-weight: 700; padding: var(--td-space-1) var(--td-space-3); border-radius: 9999px; text-transform: uppercase; letter-spacing: 0.05em; }
.td-agents__status-badge--active { background: var(--td-color-success-dim, rgba(34, 197, 94, 0.15)); color: var(--td-color-success, #22c55e); }
.td-agents__status-badge--disabled { background: var(--td-surface-container-high); color: var(--td-text-tertiary); }
</style>
