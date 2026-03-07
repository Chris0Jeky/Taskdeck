<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import WorkspaceSetupModal from '../components/workspace/WorkspaceSetupModal.vue'
import { useWorkspaceStore } from '../store/workspaceStore'
import type {
  HomeRecommendedAction,
  TodayAgendaCard,
  WorkspaceOnboarding,
  WorkspaceOnboardingStep,
} from '../types/workspace'

const router = useRouter()
const workspace = useWorkspaceStore()
const showSetupModal = ref(false)

const summary = computed(() => workspace.todaySummary)
const onboarding = computed<WorkspaceOnboarding | null>(() => summary.value?.onboarding ?? workspace.onboarding)
const recommendedActions = computed(() => summary.value?.recommendedActions ?? [])
const stats = computed(() => {
  if (!summary.value) {
    return []
  }

  return [
    {
      id: 'review',
      label: 'Pending review',
      value: summary.value.summary.proposalsPendingReview,
      helper: 'Proposals waiting for a decision before they touch a board.',
    },
    {
      id: 'triage',
      label: 'Needs triage',
      value: summary.value.summary.capturesNeedingTriage,
      helper: 'Fresh captures that still need review prep.',
    },
    {
      id: 'overdue',
      label: 'Overdue cards',
      value: summary.value.summary.overdueCards,
      helper: 'Board work that slipped past its due date.',
    },
    {
      id: 'today',
      label: 'Due today',
      value: summary.value.summary.dueTodayCards,
      helper: 'Work that should land today.',
    },
    {
      id: 'blocked',
      label: 'Blocked cards',
      value: summary.value.summary.blockedCards,
      helper: 'Cards stuck behind dependencies or missing input.',
    },
  ]
})

const agendaSections = computed(() => {
  if (!summary.value) {
    return []
  }

  const reviewCount = summary.value.summary.proposalsPendingReview
  const captureCount = summary.value.summary.capturesNeedingTriage

  return [
    {
      id: 'review',
      title: 'Review queue',
      count: reviewCount,
      helper: 'Decide proposed changes before they hit a board.',
      route: '/workspace/review',
      items: [] as TodayAgendaCard[],
      empty: reviewCount > 0
        ? `${reviewCount} proposal${reviewCount === 1 ? '' : 's'} waiting in Review.`
        : 'Nothing is waiting in Review right now.',
    },
    {
      id: 'capture',
      title: 'Capture triage',
      count: captureCount,
      helper: 'Inbox captures that still need shaping before review.',
      route: '/workspace/inbox',
      items: [] as TodayAgendaCard[],
      empty: captureCount > 0
        ? `${captureCount} capture${captureCount === 1 ? '' : 's'} ready for Inbox triage.`
        : 'Inbox is clear enough for now.',
    },
    {
      id: 'overdue',
      title: 'Overdue cards',
      count: summary.value.summary.overdueCards,
      helper: 'Start here when board work has already slipped.',
      route: null,
      items: summary.value.overdueCards,
      empty: 'No overdue cards across your boards.',
    },
    {
      id: 'today',
      title: 'Due today',
      count: summary.value.summary.dueTodayCards,
      helper: 'Work with a due date landing today.',
      route: null,
      items: summary.value.dueTodayCards,
      empty: 'No board work is due today.',
    },
    {
      id: 'blocked',
      title: 'Blocked cards',
      count: summary.value.summary.blockedCards,
      helper: 'Cards that need a dependency cleared or a decision made.',
      route: null,
      items: summary.value.blockedCards,
      empty: 'No blocked cards right now.',
    },
  ]
})

async function loadTodaySummary() {
  try {
    await workspace.fetchTodaySummary()
  } catch {
    // The store keeps the error state for the view.
  }
}

function openRoute(route: string) {
  void router.push(route)
}

function openBoard(boardId: string) {
  void router.push(`/workspace/boards/${boardId}`)
}

function resolveActionRoute(action: HomeRecommendedAction): string {
  switch (action.targetSurface) {
    case 'review':
      return '/workspace/review'
    case 'boards':
      return '/workspace/boards'
    case 'board':
      return action.boardId ? `/workspace/boards/${action.boardId}` : '/workspace/boards'
    case 'capture':
    default:
      return '/workspace/inbox'
  }
}

function openRecommendedAction(action: HomeRecommendedAction) {
  openRoute(resolveActionRoute(action))
}

function formatDueDate(value: string | null): string {
  if (!value) {
    return 'No due date'
  }

  return new Date(value).toLocaleString()
}

function openSetupModal() {
  showSetupModal.value = true
}

function closeSetupModal() {
  showSetupModal.value = false
}

function handleSetupCreated() {
  void loadTodaySummary()
}

function openOnboardingStep(step: WorkspaceOnboardingStep) {
  if (step.targetSurface === 'boards') {
    openSetupModal()
    return
  }

  openRoute(step.targetSurface === 'review' ? '/workspace/review' : '/workspace/inbox')
}

async function dismissOnboarding() {
  try {
    await workspace.updateOnboarding('dismiss')
  } catch {
    // The store retains the warning state.
  }
}

async function replayOnboarding() {
  try {
    await workspace.updateOnboarding('replay')
  } catch {
    // The store retains the warning state.
  }
}

onMounted(() => {
  if (workspace.todayLoading || workspace.hasTodaySummary) {
    return
  }

  void loadTodaySummary()
})
</script>

<template>
  <div class="td-today">
    <header class="td-today__hero td-panel">
      <div class="td-today__hero-copy">
        <span class="td-today__eyebrow">Daily Agenda</span>
        <h1 class="td-page-title">Today</h1>
        <p class="td-today__subtitle">
          See what needs a decision, what needs shaping, and what board work is due before the day gets away from you.
        </p>
      </div>

      <div class="td-today__hero-actions">
        <button class="td-btn td-btn--primary" @click="openRoute('/workspace/review')">Open Review</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/inbox')">Open Inbox</button>
        <button class="td-btn td-btn--secondary" @click="openSetupModal">Start Useful Board</button>
      </div>
    </header>

    <div v-if="workspace.todayLoading" class="td-panel td-today__placeholder" aria-live="polite">
      Loading today's agenda...
    </div>

    <div v-else-if="workspace.todayError" class="td-alert td-alert--error" role="alert">
      {{ workspace.todayError }}
    </div>

    <template v-else-if="summary">
      <section v-if="onboarding" class="td-panel td-today__onboarding">
        <div class="td-today__section-head">
          <div>
            <h2 class="td-section-title">Onboarding loop</h2>
            <p class="td-section-desc">
              <template v-if="onboarding.visibility === 'dismissed'">
                Setup is dismissed. Replay it when you want the guided path back into capture, review, and boards.
              </template>
              <template v-else-if="onboarding.isComplete">
                The setup loop is complete. Reopen it anytime if you want to reset your focus around the review-first path.
              </template>
              <template v-else>
                Finish the loop once so Home, Today, Review, and boards reinforce the same operating path.
              </template>
            </p>
          </div>
          <div class="td-today__onboarding-actions">
            <button
              v-if="onboarding.visibility === 'dismissed'"
              class="td-btn td-btn--primary"
              @click="replayOnboarding"
            >
              Replay Setup
            </button>
            <template v-else>
              <button class="td-btn td-btn--primary" @click="openSetupModal">
                {{ onboarding.isComplete ? 'Reopen Setup' : 'Start Setup' }}
              </button>
              <button class="td-btn td-btn--ghost" @click="dismissOnboarding">Dismiss</button>
            </template>
          </div>
        </div>

        <div v-if="onboarding.visibility !== 'dismissed'" class="td-today__step-grid">
          <button
            v-for="step in onboarding.steps"
            :key="step.stepId"
            :class="['td-today-step', step.isComplete ? 'td-today-step--complete' : '']"
            @click="openOnboardingStep(step)"
          >
            <span class="td-today-step__state">{{ step.isComplete ? 'Done' : 'Next' }}</span>
            <span class="td-today-step__title">{{ step.title }}</span>
            <span class="td-today-step__description">{{ step.description }}</span>
          </button>
        </div>
      </section>

      <section class="td-today__stats">
        <article v-for="stat in stats" :key="stat.id" class="td-panel td-today-stat">
          <span class="td-today-stat__label">{{ stat.label }}</span>
          <span class="td-today-stat__value">{{ stat.value }}</span>
          <span class="td-today-stat__helper">{{ stat.helper }}</span>
        </article>
      </section>

      <section class="td-today__agenda-grid">
        <article
          v-for="section in agendaSections"
          :key="section.id"
          class="td-panel td-today-card"
        >
          <div class="td-today__section-head">
            <div>
              <h2 class="td-section-title">{{ section.title }}</h2>
              <p class="td-section-desc">{{ section.helper }}</p>
            </div>
            <span class="td-today-card__count">{{ section.count }}</span>
          </div>

          <div v-if="section.items.length === 0" class="td-today-card__empty">
            <p>{{ section.empty }}</p>
            <button
              v-if="section.route"
              class="td-btn td-btn--secondary td-btn--sm"
              @click="openRoute(section.route)"
            >
              Open {{ section.title }}
            </button>
          </div>

          <div v-else class="td-today-card__list">
            <button
              v-for="item in section.items"
              :key="item.cardId"
              class="td-today-item"
              @click="openBoard(item.boardId)"
            >
              <span class="td-today-item__title">{{ item.title }}</span>
              <span class="td-today-item__meta">{{ item.boardName }}</span>
              <span class="td-today-item__meta">
                {{ item.blockReason ? item.blockReason : formatDueDate(item.dueDate) }}
              </span>
            </button>
          </div>
        </article>
      </section>

      <section class="td-panel td-today__recommended">
        <div class="td-today__section-head">
          <div>
            <h2 class="td-section-title">Recommended next moves</h2>
            <p class="td-section-desc">Keep the loop moving without leaving Today to figure out where to go next.</p>
          </div>
        </div>

        <div class="td-today__recommended-actions">
          <button
            v-for="action in recommendedActions"
            :key="action.actionId"
            class="td-today-recommendation"
            @click="openRecommendedAction(action)"
          >
            <span class="td-today-recommendation__title">{{ action.title }}</span>
            <span class="td-today-recommendation__description">{{ action.description }}</span>
          </button>
        </div>
      </section>
    </template>

    <Teleport to="body">
      <WorkspaceSetupModal
        :is-open="showSetupModal"
        @close="closeSetupModal"
        @created="handleSetupCreated"
      />
    </Teleport>
  </div>
</template>

<style scoped>
.td-today {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-today__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-6);
  align-items: flex-start;
}

.td-today__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-today__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

.td-today__subtitle {
  font-size: var(--td-font-base);
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-today__hero-actions,
.td-today__onboarding-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

.td-today__placeholder {
  color: var(--td-text-secondary);
}

.td-today__onboarding,
.td-today__recommended {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-today__section-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-3);
}

.td-today__step-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--td-space-3);
}

.td-today-step {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  text-align: left;
  padding: var(--td-space-3);
  border-radius: var(--td-radius-lg);
  border: 1px solid var(--td-border-default);
  background: var(--td-surface-secondary);
  cursor: pointer;
}

.td-today-step--complete {
  border-color: color-mix(in srgb, var(--td-color-success) 45%, var(--td-border-default));
  background: color-mix(in srgb, var(--td-color-success) 8%, var(--td-surface-primary));
}

.td-today-step__state {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-text-tertiary);
}

.td-today-step__title {
  font-size: var(--td-font-base);
  font-weight: 700;
}

.td-today-step__description {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-today__stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: var(--td-space-3);
}

.td-today-stat {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-today-stat__label {
  font-size: var(--td-font-sm);
  font-weight: 700;
  color: var(--td-text-secondary);
}

.td-today-stat__value {
  font-size: var(--td-font-3xl);
  font-weight: 700;
}

.td-today-stat__helper {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-today__agenda-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: var(--td-space-4);
}

.td-today-card {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-today-card__count {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-secondary);
  border: 1px solid var(--td-border-default);
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
  font-weight: 700;
  padding: 0.25rem 0.625rem;
}

.td-today-card__empty {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-today-card__empty p {
  margin: 0;
}

.td-today-card__list,
.td-today__recommended-actions {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-today-item,
.td-today-recommendation {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  text-align: left;
  width: 100%;
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 1px solid var(--td-border-default);
  background: var(--td-surface-secondary);
  cursor: pointer;
}

.td-today-item__title,
.td-today-recommendation__title {
  font-size: var(--td-font-sm);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-today-item__meta,
.td-today-recommendation__description {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-btn--sm {
  padding: var(--td-space-1) var(--td-space-3);
  font-size: var(--td-font-xs);
}

.td-alert {
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
}

.td-alert--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

@media (max-width: 768px) {
  .td-today__hero,
  .td-today__section-head {
    flex-direction: column;
  }
}
</style>
