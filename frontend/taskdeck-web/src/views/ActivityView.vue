<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
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
  <div class="paper-activity">
    <header class="paper-activity__hero">
      <div class="paper-activity__hero-copy">
        <span class="tk-eyebrow paper-activity__eyebrow">Advanced</span>
        <h1 class="tk-h1 paper-activity__title">Activity</h1>
        <p class="tk-lede paper-activity__subtitle">
          Use activity to inspect what already happened. Review is where pending proposals get decided, and Boards is
          where most day-to-day work continues.
        </p>
      </div>

      <div class="paper-activity__hero-actions">
        <PaperHLBtn variant="ember" @click="openRoute('/workspace/review')">Open Review</PaperHLBtn>
        <PaperHLBtn variant="ghost" @click="openRoute('/workspace/boards')">Open Boards</PaperHLBtn>
      </div>
    </header>

    <WorkspaceHelpCallout
      topic="activity-selectors"
      title="Why do these selectors matter?"
      description="Board history shows all activity within a board — cards, columns, labels, and the board itself. Narrow down to entity history when you know exactly which item to inspect."
    >
      <template #actions>
        <PaperHLBtn @click="openRoute('/workspace/review')">Open Review</PaperHLBtn>
        <PaperHLBtn @click="openRoute('/workspace/boards')">Open Boards</PaperHLBtn>
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
/* ── Paper & Graphite — ActivityView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   The tokens live under `.paper` / `.paper-night` (the canonical shell), so the
   var() fallbacks keep this surface legible if it is ever rendered outside the
   Paper shell (Legacy/Obsidian "off" mode). */

.paper-activity {
  max-width: 860px;
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.paper-activity__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--s-6, 24px);
  align-items: flex-start;
  margin-bottom: var(--s-4, 16px);
}

.paper-activity__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  max-width: 720px;
}

.paper-activity__eyebrow {
  color: var(--mute, #6c6557);
}

.paper-activity__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

.paper-activity__subtitle {
  margin: 0;
  color: var(--ink-2, #3a352d);
}

.paper-activity__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2, 8px);
  flex-shrink: 0;
}

@media (max-width: 900px) {
  .paper-activity__hero {
    flex-direction: column;
  }
}
</style>
