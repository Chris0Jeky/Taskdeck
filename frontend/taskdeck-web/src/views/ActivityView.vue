<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import ActivitySelector from '../components/activity/ActivitySelector.vue'
import ActivityResults from '../components/activity/ActivityResults.vue'
import { useActivityQuery } from '../composables/useActivityQuery'

const router = useRouter()

const {
  viewMode,
  selectedBoardId,
  selectedEntityType,
  selectedEntityBoardId,
  selectedEntityId,
  limit,
  loadingEntitySource,
  boardOptions,
  requiresEntityBoardContext,
  entityOptions,
  canFetch,
  selectedIdForCopy,
  selectedIdLabel,
  emptyStateTitle,
  emptyStateBody,
  handleFetchClick,
  copySelectedId,
  initialize,
} = useActivityQuery()

function openRoute(path: string) {
  void router.push(path)
}

onMounted(async () => {
  await initialize()
})
</script>

<template>
  <div class="td-activity">
    <header class="td-activity__hero">
      <div class="td-activity__hero-copy">
        <span class="td-activity__eyebrow">Advanced</span>
        <h1 class="td-page-title">Activity</h1>
        <p class="td-activity__subtitle">
          Use activity to inspect what already happened. Review is where pending proposals get decided, and Boards is
          where most day-to-day work continues.
        </p>
      </div>

      <div class="td-activity__hero-actions">
        <button class="td-btn td-btn--primary td-btn--sm" @click="openRoute('/workspace/review')">Open Review</button>
        <button class="td-btn td-btn--ghost td-btn--sm" @click="openRoute('/workspace/boards')">Open Boards</button>
      </div>
    </header>

    <WorkspaceHelpCallout
      topic="activity-selectors"
      title="Why do these selectors matter?"
      description="Board history shows all activity within a board — cards, columns, labels, and the board itself. Narrow down to entity history when you know exactly which item to inspect."
    >
      <template #actions>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/review')">Open Review</button>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/boards')">Open Boards</button>
      </template>
    </WorkspaceHelpCallout>

    <ActivitySelector
      :view-mode="viewMode"
      :selected-board-id="selectedBoardId"
      :selected-entity-type="selectedEntityType"
      :selected-entity-board-id="selectedEntityBoardId"
      :selected-entity-id="selectedEntityId"
      :limit="limit"
      :loading-entity-source="loadingEntitySource"
      :board-options="boardOptions"
      :requires-entity-board-context="requiresEntityBoardContext"
      :entity-options="entityOptions"
      :can-fetch="canFetch"
      :selected-id-for-copy="selectedIdForCopy"
      :selected-id-label="selectedIdLabel"
      @update:view-mode="viewMode = $event"
      @update:selected-board-id="selectedBoardId = $event"
      @update:selected-entity-type="selectedEntityType = $event"
      @update:selected-entity-board-id="selectedEntityBoardId = $event"
      @update:selected-entity-id="selectedEntityId = $event"
      @update:limit="limit = $event"
      @fetch="handleFetchClick"
      @copy-id="copySelectedId"
    />

    <ActivityResults
      :empty-state-title="emptyStateTitle"
      :empty-state-body="emptyStateBody"
      @navigate="openRoute"
    />
  </div>
</template>

<style scoped>
.td-activity { max-width: 860px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; color: var(--td-text-primary); }
.td-activity__hero { display: flex; justify-content: space-between; gap: var(--td-space-6); align-items: flex-start; margin-bottom: var(--td-space-4); }
.td-activity__hero-copy { display: flex; flex-direction: column; gap: var(--td-space-2); max-width: 720px; }
.td-activity__eyebrow { font-size: var(--td-font-xs); font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--td-text-tertiary); }
.td-activity__subtitle { color: var(--td-text-secondary); line-height: 1.6; }
.td-activity__hero-actions,
.td-empty__actions { display: flex; flex-wrap: wrap; gap: var(--td-space-2); }

@media (max-width: 900px) {
  .td-activity__hero {
    flex-direction: column;
  }
}
</style>
