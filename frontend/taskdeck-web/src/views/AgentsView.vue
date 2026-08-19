<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAgentStore } from '../store/agentStore'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'

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
  <div class="paper-agents">
    <header class="paper-agents__header">
      <div class="paper-agents__header-copy">
        <span class="tk-eyebrow paper-agents__eyebrow">Agent Mode</span>
        <h1 class="tk-h2 paper-agents__title">Agents</h1>
        <p class="tk-lede paper-agents__subtitle">
          Agent profiles define what an agent can do, its scope, and its policy constraints.
          Select an agent to view its run history.
        </p>
      </div>
    </header>

    <!-- Loading state -->
    <div v-if="agentStore.profilesLoading" class="paper-agents__state" role="status">
      <div class="paper-agents__spinner" aria-hidden="true" />
      <span>Loading agents...</span>
    </div>

    <!-- Error state -->
    <div
      v-else-if="agentStore.profilesError"
      class="paper-agents__state paper-agents__state--error"
      role="alert"
    >
      <p>{{ agentStore.profilesError }}</p>
      <PaperHLBtn variant="ember" @click="agentStore.fetchProfiles()">Retry</PaperHLBtn>
    </div>

    <!-- Empty state -->
    <div
      v-else-if="agentStore.profiles.length === 0"
      class="paper-agents__state paper-agents__state--empty"
    >
      <p class="paper-agents__empty-title">No agents configured</p>
      <p class="paper-agents__empty-body">
        Agent profiles are created via the API. Once configured, they will appear here
        with their status and run history.
      </p>
    </div>

    <!-- Profile list -->
    <ul v-else class="paper-agents__list" role="list" aria-label="Agent profiles">
      <li
        v-for="profile in agentStore.profiles"
        :key="profile.id"
        class="paper-agents__card"
        role="listitem"
      >
        <button
          class="paper-agents__card-btn"
          :aria-label="`View runs for ${profile.name}`"
          @click="openRuns(profile.id)"
        >
          <div class="paper-agents__card-header">
            <span class="paper-agents__card-name">{{ profile.name }}</span>
            <span
              class="paper-agents__status-badge"
              :class="profile.isEnabled ? 'paper-agents__status-badge--active' : 'paper-agents__status-badge--disabled'"
            >
              {{ profile.isEnabled ? 'Active' : 'Disabled' }}
            </span>
          </div>
          <p v-if="profile.description" class="paper-agents__card-desc">
            {{ profile.description }}
          </p>
          <div class="paper-agents__card-meta">
            <span class="paper-agents__meta-item">
              Scope: {{ profile.scopeType === 'Board' ? 'Board' : 'Workspace' }}
            </span>
            <span class="paper-agents__meta-item">
              Template: {{ profile.templateKey }}
            </span>
          </div>
        </button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — AgentsView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell. */

.paper-agents {
  max-width: 860px;
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.paper-agents__header { margin-bottom: var(--s-6, 24px); }
.paper-agents__header-copy { display: flex; flex-direction: column; gap: var(--s-2, 8px); }
.paper-agents__eyebrow { color: var(--ember, #a8421f); }
.paper-agents__title { margin: 0; font-size: var(--t-h2, 32px); }
.paper-agents__subtitle { margin: 0; color: var(--ink-2, #3a352d); }

.paper-agents__state {
  padding: var(--s-8, 32px);
  text-align: center;
  color: var(--mute, #6c6557);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--s-4, 16px);
}

.paper-agents__state--error { color: var(--overdue, #8c4a26); }

.paper-agents__spinner {
  width: 24px;
  height: 24px;
  border: 3px solid var(--line, #d8d0bf);
  border-top-color: var(--ember, #a8421f);
  border-radius: 50%;
  animation: paper-agents-spin 0.8s linear infinite;
}

@keyframes paper-agents-spin { to { transform: rotate(360deg); } }

.paper-agents__empty-title {
  margin: 0;
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-lg, 18px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-agents__empty-body { margin: 0; color: var(--ink-2, #3a352d); max-width: 440px; }

.paper-agents__list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-agents__card {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-agents__card:hover { border-color: var(--ember, #a8421f); }

.paper-agents__card-btn {
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

.paper-agents__card-btn:focus-visible {
  outline: 2px solid var(--ember, #a8421f);
  outline-offset: -2px;
  border-radius: var(--r-3, 6px);
}

.paper-agents__card-header { display: flex; align-items: center; gap: var(--s-3, 12px); }

.paper-agents__card-name {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-lg, 18px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-agents__card-desc {
  margin: 0;
  color: var(--ink-2, #3a352d);
  font-size: var(--t-md, 13.5px);
  line-height: 1.5;
}

.paper-agents__card-meta {
  display: flex;
  gap: var(--s-4, 16px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
}

.paper-agents__meta-item { white-space: nowrap; }

.paper-agents__status-badge {
  font-size: var(--t-xs, 10.5px);
  font-weight: 700;
  padding: var(--s-1, 4px) var(--s-3, 12px);
  border-radius: var(--r-1, 2px);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.paper-agents__status-badge--active {
  background: var(--applied-tint, #d8e0ce);
  color: var(--applied, #4a6b3f);
}

.paper-agents__status-badge--disabled {
  background: var(--paper-2, #ebe5d8);
  color: var(--mute, #6c6557);
}
</style>
