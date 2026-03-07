<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { workspaceApi } from '../api/workspaceApi'

const router = useRouter()
const loading = ref(false)
const error = ref<string | null>(null)
const summary = ref<null | {
  isFirstRun: boolean
  inboxNeedsTriage: number
  proposalsPendingReview: number
  blockedCards: number
  dueToday: number
  recentBoards: Array<{ id: string; name: string; description?: string | null }>
}>(null)

const hasUrgentWork = computed(() => {
  if (!summary.value) return false
  return summary.value.inboxNeedsTriage > 0 ||
    summary.value.proposalsPendingReview > 0 ||
    summary.value.blockedCards > 0 ||
    summary.value.dueToday > 0
})

async function loadHome() {
  loading.value = true
  error.value = null
  try {
    summary.value = await workspaceApi.getHomeSummary()
  } catch (e) {
    error.value = 'Failed to load workspace summary.'
  } finally {
    loading.value = false
  }
}

function goToReview() {
  void router.push('/workspace/review')
}

function goToInbox() {
  void router.push('/workspace/inbox')
}

function goToProjects() {
  void router.push('/workspace/projects')
}

onMounted(() => {
  void loadHome()
})
</script>

<template>
  <div class="td-home">
    <header class="td-home__hero">
      <div>
        <h1 class="td-page-title">Home</h1>
        <p class="td-page-subtitle">
          Turn quick captures into reviewable project updates.
        </p>
      </div>

      <div class="td-home__hero-actions">
        <button class="td-btn td-btn--primary" @click="goToInbox">Capture something</button>
        <button class="td-btn td-btn--secondary" @click="goToProjects">Create project</button>
      </div>
    </header>

    <div v-if="loading" class="td-placeholder">Loading workspace…</div>
    <div v-else-if="error" class="td-alert td-alert--error">{{ error }}</div>

    <template v-else-if="summary">
      <section v-if="summary.isFirstRun" class="td-surface td-home__start-here">
        <h2>Start here</h2>
        <ol>
          <li>Create your first project.</li>
          <li>Capture one note or task.</li>
          <li>Review and apply the generated proposal.</li>
        </ol>
      </section>

      <section class="td-home__grid">
        <article class="td-surface td-home-card">
          <h2>Needs attention</h2>
          <ul>
            <li>Inbox needs triage: {{ summary.inboxNeedsTriage }}</li>
            <li>Pending review: {{ summary.proposalsPendingReview }}</li>
            <li>Blocked cards: {{ summary.blockedCards }}</li>
            <li>Due today: {{ summary.dueToday }}</li>
          </ul>
          <button v-if="hasUrgentWork" class="td-btn td-btn--primary td-btn--sm" @click="goToReview">
            Review now
          </button>
        </article>

        <article class="td-surface td-home-card">
          <h2>Recent projects</h2>
          <div v-if="summary.recentBoards.length === 0" class="td-placeholder">
            No projects yet. Create one to get started.
          </div>
          <ul v-else>
            <li v-for="board in summary.recentBoards" :key="board.id">
              <router-link :to="`/workspace/projects/${board.id}`">{{ board.name }}</router-link>
            </li>
          </ul>
        </article>
      </section>
    </template>
  </div>
</template>
