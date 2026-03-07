<script setup lang="ts">
import { computed, onActivated, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useWorkspaceStore } from '../store/workspaceStore'
import type { HomeRecommendedAction } from '../types/workspace'

const router = useRouter()
const workspace = useWorkspaceStore()

const summary = computed(() => workspace.homeSummary)
const recentBoards = computed(() => summary.value?.boards.recentBoards ?? [])
const recommendedActions = computed(() => summary.value?.recommendedActions ?? [])
const workloadCards = computed(() => {
  if (!summary.value) {
    return []
  }

  return [
    {
      id: 'triage',
      label: 'Needs triage',
      value: summary.value.workload.capturesNeedingTriage,
      helper: 'Inbox captures waiting for review prep.',
    },
    {
      id: 'in-progress',
      label: 'In progress',
      value: summary.value.workload.capturesInProgress,
      helper: 'Captures currently being triaged into a proposal-ready shape.',
    },
    {
      id: 'follow-up',
      label: 'Needs follow-up',
      value: summary.value.workload.capturesReadyForFollowUp,
      helper: 'Triaged captures that still need a linked proposal or next step.',
    },
    {
      id: 'review',
      label: 'Pending review',
      value: summary.value.workload.proposalsPendingReview,
      helper: 'Proposal changes waiting for your decision.',
    },
  ]
})

async function loadHomeSummary() {
  try {
    await workspace.fetchHomeSummary()
  } catch {
    // The store keeps the error state for the view.
  }
}

function openRoute(route: string) {
  void router.push(route)
}

function resolveActionRoute(action: HomeRecommendedAction): string {
  switch (action.targetSurface) {
    case 'review':
      return '/workspace/automations/proposals'
    case 'boards':
      return '/workspace/boards'
    case 'board':
      return action.boardId ? `/workspace/boards/${action.boardId}` : '/workspace/boards'
    case 'capture':
    default:
      return '/workspace/inbox'
  }
}

function resolveActionTone(action: HomeRecommendedAction, index: number): 'primary' | 'secondary' {
  if (action.targetSurface === 'review' || action.actionId === 'review-proposals') {
    return 'primary'
  }

  return index === 0 ? 'primary' : 'secondary'
}

function openRecommendedAction(action: HomeRecommendedAction) {
  openRoute(resolveActionRoute(action))
}

function openBoard(boardId: string) {
  void router.push(`/workspace/boards/${boardId}`)
}

function refreshHomeSummary() {
  if (workspace.homeLoading) {
    return
  }

  void loadHomeSummary()
}

onMounted(refreshHomeSummary)
onActivated(refreshHomeSummary)
</script>

<template>
  <div class="td-home">
    <header class="td-home__hero td-panel">
      <div class="td-home__hero-copy">
        <span class="td-home__eyebrow">Workspace</span>
        <h1 class="td-page-title">Home</h1>
        <p class="td-home__subtitle">
          Start from one place, move through review with intent, and keep boards as the place where work lands.
        </p>
      </div>

      <div class="td-home__hero-actions">
        <button class="td-btn td-btn--primary" @click="openRoute('/workspace/inbox')">Capture to Inbox</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/automations/proposals')">Open Review</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/boards')">Open Boards</button>
      </div>
    </header>

    <div v-if="workspace.homeLoading" class="td-panel td-home__placeholder" aria-live="polite">
      Loading your workspace summary...
    </div>

    <div v-else-if="workspace.homeError" class="td-alert td-alert--error" role="alert">
      {{ workspace.homeError }}
    </div>

    <template v-else-if="summary">
      <section v-if="summary.isFirstRun" class="td-panel td-home__start-here">
        <div>
          <h2 class="td-section-title">Start here</h2>
          <p class="td-section-desc">
            The first useful loop is simple: create a board, capture one item, then review the proposal before it touches your board.
          </p>
        </div>
        <ol class="td-home__steps">
          <li>Create your first board or starter layout.</li>
          <li>Capture one task, note, or follow-up into Inbox.</li>
          <li>Review the proposed change before you apply it.</li>
        </ol>
      </section>

      <section class="td-home__grid">
        <article class="td-panel td-home-card">
          <div class="td-home-card__header">
            <h2 class="td-section-title">Needs attention</h2>
            <span class="td-home-card__badge">{{ summary.workload.proposalsPendingReview }} awaiting review</span>
          </div>
          <div class="td-home-card__stats">
            <div v-for="card in workloadCards" :key="card.id" class="td-home-card__stat">
              <span class="td-home-card__value">{{ card.value }}</span>
              <span class="td-home-card__label">{{ card.label }}</span>
              <span class="td-home-card__helper">{{ card.helper }}</span>
            </div>
          </div>
        </article>

        <article class="td-panel td-home-card">
          <div class="td-home-card__header">
            <h2 class="td-section-title">Next step</h2>
            <span class="td-home-card__badge">Review-first</span>
          </div>
          <div class="td-home-card__actions">
            <button
              v-for="(action, index) in recommendedActions"
              :key="action.actionId"
              :class="[
                'td-home-action',
                resolveActionTone(action, index) === 'primary' ? 'td-home-action--primary' : 'td-home-action--secondary',
              ]"
              @click="openRecommendedAction(action)"
            >
              <span class="td-home-action__title">
                {{ action.title }}
                <span v-if="action.attentionCount" class="td-home-action__count">{{ action.attentionCount }}</span>
              </span>
              <span class="td-home-action__description">{{ action.description }}</span>
            </button>
          </div>
        </article>

        <article class="td-panel td-home-card">
          <div class="td-home-card__header">
            <h2 class="td-section-title">Boards</h2>
            <span class="td-home-card__badge">{{ summary.boards.recentBoardsCount }} recently active</span>
          </div>
          <dl class="td-home-card__workspace-summary">
            <div>
              <dt>Total boards</dt>
              <dd>{{ summary.boards.totalBoards }}</dd>
            </div>
            <div>
              <dt>Recent boards</dt>
              <dd>{{ summary.boards.recentBoardsCount }}</dd>
            </div>
          </dl>
          <div v-if="recentBoards.length === 0" class="td-home-card__empty">
            <template v-if="summary.boards.totalBoards === 0">
              No boards yet. Create one so captures and review can land somewhere useful.
            </template>
            <template v-else>
              No recently active boards yet. Open Boards to pick up where you left off.
            </template>
          </div>
          <div v-else class="td-home-card__board-list">
            <button
              v-for="board in recentBoards"
              :key="board.id"
              class="td-home-board"
              @click="openBoard(board.id)"
            >
              <span class="td-home-board__name">{{ board.name }}</span>
              <span class="td-home-board__description">{{ board.description || 'No description yet.' }}</span>
            </button>
          </div>
        </article>
      </section>
    </template>
  </div>
</template>

<style scoped>
.td-home {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-home__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-6);
  align-items: flex-start;
}

.td-home__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-home__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

.td-home__subtitle {
  font-size: var(--td-font-base);
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-home__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  justify-content: flex-end;
}

.td-home__placeholder {
  color: var(--td-text-secondary);
}

.td-home__start-here {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-home__steps {
  margin: 0;
  padding-left: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  color: var(--td-text-secondary);
}

.td-home__grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: var(--td-space-4);
}

.td-home-card {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-home-card__header {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-2);
  align-items: flex-start;
}

.td-home-card__badge {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-secondary);
  border: 1px solid var(--td-border-default);
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
  font-weight: 600;
  padding: 0.25rem 0.625rem;
  white-space: nowrap;
}

.td-home-card__stats {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--td-space-3);
}

.td-home-card__stat {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-secondary);
  border: 1px solid var(--td-border-default);
}

.td-home-card__value {
  font-size: var(--td-font-2xl);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-home-card__label {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-home-card__helper {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
  line-height: 1.4;
}

.td-home-card__actions {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-home-action {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  width: 100%;
  text-align: left;
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
  cursor: pointer;
  transition: background var(--td-transition-fast), border-color var(--td-transition-fast);
}

.td-home-action--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
  border: 1px solid var(--td-color-primary);
}

.td-home-action--secondary {
  background: var(--td-surface-secondary);
  color: var(--td-text-primary);
  border: 1px solid var(--td-border-default);
}

.td-home-action__title {
  font-size: var(--td-font-sm);
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--td-space-2);
}

.td-home-action__description {
  font-size: var(--td-font-xs);
  line-height: 1.5;
}

.td-home-action__count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.5rem;
  padding: 0.125rem 0.375rem;
  border-radius: var(--td-radius-pill, 999px);
  background: color-mix(in srgb, currentColor 14%, transparent);
  font-size: var(--td-font-xs);
  font-weight: 700;
}

.td-home-card__workspace-summary {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--td-space-3);
  margin: 0;
}

.td-home-card__workspace-summary div {
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-secondary);
  border: 1px solid var(--td-border-default);
}

.td-home-card__workspace-summary dt {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
  margin-bottom: 0.25rem;
}

.td-home-card__workspace-summary dd {
  margin: 0;
  font-size: var(--td-font-xl);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-home-card__empty {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  line-height: 1.6;
}

.td-home-card__board-list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-home-board {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  width: 100%;
  text-align: left;
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 1px solid var(--td-border-default);
  background: var(--td-surface-secondary);
  cursor: pointer;
}

.td-home-board__name {
  font-size: var(--td-font-sm);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-home-board__description {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

@media (max-width: 768px) {
  .td-home__hero {
    flex-direction: column;
  }

  .td-home__hero-actions {
    justify-content: flex-start;
  }

  .td-home-card__stats,
  .td-home-card__workspace-summary {
    grid-template-columns: 1fr;
  }
}
</style>
