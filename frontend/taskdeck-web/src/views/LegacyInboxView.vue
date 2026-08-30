<script setup lang="ts">
import { ref } from 'vue'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import CaptureModal from '../components/common/CaptureModal.vue'
import InboxListPanel from '../components/inbox/InboxListPanel.vue'
import InboxDetailPanel from '../components/inbox/InboxDetailPanel.vue'
import { useInboxOrchestrator } from '../composables/useInboxOrchestrator'

const listPanelRef = ref<InstanceType<typeof InboxListPanel> | null>(null)

const {
  captureStore,
  items,
  selectedItemId,
  hashLoadFailedItemId,
  activeItemIndex,
  activeDescendantId,
  selectedItem,
  activeBoardId,
  isArchivedHistory,
  showCaptureModal,
  selectedIds,
  isEditingSuggestion,
  editedText,
  editedTitleHint,
  loadInbox,
  openItemFromList,
  setActiveIndex,
  handleKeydown,
  toggleItemSelection,
  toggleSelectAll,
  clearSelection,
  batchAction,
  openCaptureModal,
  closeCaptureModal,
  handleCaptureCreated,
  openRoute,
  openReview,
  closeDetail,
  refreshSelectedDetail,
  triageSelected,
  ignoreSelected,
  cancelSelected,
  startEditSuggestion,
  cancelEditSuggestion,
  saveEditedSuggestion,
  openProposal,
} = useInboxOrchestrator({
  scrollToIndex: () => listPanelRef.value?.scrollToIndex,
})
</script>

<template>
  <div class="td-inbox" role="region" aria-label="Capture inbox">
    <header class="td-inbox__header">
      <div>
        <h1 class="td-page-title">{{ isArchivedHistory ? 'Archived capture history' : 'Inbox' }}</h1>
        <p class="td-inbox__subtitle">
          {{ isArchivedHistory
            ? 'Read-only retained captures. Restore the board before creating, editing, or triaging work.'
            : 'Capture rough notes and turn them into reviewable proposed work.' }}
        </p>
        <p v-if="activeBoardId" class="td-inbox__board-context">
          Showing capture items linked to board {{ activeBoardId }}.
        </p>
      </div>
      <div class="td-inbox__header-actions">
        <button
          v-if="!isArchivedHistory"
          class="td-btn td-btn--primary"
          aria-label="Open capture modal to add a new inbox item"
          @click="openCaptureModal"
        >
          + New Capture
        </button>
        <button class="td-btn td-btn--secondary" @click="loadInbox" :disabled="captureStore.loadingList">
          {{ captureStore.loadingList ? 'Refreshing...' : 'Refresh' }}
        </button>
      </div>
    </header>

    <WorkspaceHelpCallout
      v-if="!isArchivedHistory"
      topic="inbox"
      title="What is Inbox for?"
      description="Inbox is where Taskdeck prepares a proposed change from your note, then sends it to Review before anything reaches a board."
    >
      <template #actions>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/home')">Open Home</button>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openReview">Open Review</button>
      </template>
    </WorkspaceHelpCallout>

    <div class="td-inbox__layout">
      <InboxListPanel
        ref="listPanelRef"
        :items="items"
        :loading-list="captureStore.loadingList"
        :list-error="captureStore.listError"
        :has-items="captureStore.hasItems"
        :batch-busy="captureStore.batchBusy"
        :active-item-index="activeItemIndex"
        :selected-item-id="selectedItemId"
        :selected-ids="selectedIds"
        :active-descendant-id="activeDescendantId"
        :read-only="isArchivedHistory"
        @open-item="openItemFromList"
        @set-active-index="setActiveIndex"
        @keydown="handleKeydown"
        @toggle-item-selection="toggleItemSelection"
        @toggle-select-all="toggleSelectAll"
        @clear-selection="clearSelection"
        @batch-action="batchAction"
        @open-capture-modal="openCaptureModal"
        @open-route="openRoute"
        @open-review="openReview"
        @load-inbox="loadInbox"
      />

      <InboxDetailPanel
        :selected-item-id="selectedItemId"
        :selected-item="selectedItem"
        :hash-load-failed-item-id="hashLoadFailedItemId"
        :loading-detail="captureStore.loadingDetail"
        :action-busy-item-id="captureStore.actionBusyItemId"
        :triage-polling-item-id="captureStore.triagePollingItemId"
        :is-editing-suggestion="isEditingSuggestion"
        :edited-text="editedText"
        :edited-title-hint="editedTitleHint"
        :read-only="isArchivedHistory"
        @close-detail="closeDetail"
        @refresh-detail="refreshSelectedDetail"
        @triage-selected="triageSelected"
        @ignore-selected="ignoreSelected"
        @cancel-selected="cancelSelected"
        @start-edit-suggestion="startEditSuggestion"
        @cancel-edit-suggestion="cancelEditSuggestion"
        @save-edited-suggestion="saveEditedSuggestion"
        @open-proposal="openProposal"
        @update:edited-text="editedText = $event"
        @update:edited-title-hint="editedTitleHint = $event"
      />
    </div>
  </div>

  <Teleport to="body">
    <CaptureModal
      v-if="showCaptureModal && !isArchivedHistory"
      @close="closeCaptureModal"
      @created="handleCaptureCreated"
    />
  </Teleport>
</template>

<style scoped>
/* ---- Obsidian & Ember -- InboxView ---- */

.td-inbox {
  max-width: 1200px;
}

/* ---- Page header ---- */

.td-inbox__header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-4);
  margin-bottom: var(--td-space-4);
}

.td-inbox__header-actions {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  flex-shrink: 0;
}

.td-inbox__subtitle {
  margin-top: var(--td-space-1);
  color: var(--td-text-secondary);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.2em;
}

.td-inbox__board-context {
  margin-top: var(--td-space-2);
  color: var(--td-color-ember);
  font-size: var(--td-font-sm);
  font-weight: 600;
}

/* ---- Two-column layout ---- */

.td-inbox__layout {
  display: grid;
  grid-template-columns: minmax(320px, 1fr) minmax(420px, 1.4fr);
  gap: var(--td-space-4);
}

/* ---- Responsive ---- */

@media (max-width: 1024px) {
  .td-inbox__layout {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .td-inbox {
    max-width: 100%;
  }

  .td-inbox__header {
    flex-direction: column;
    gap: var(--td-space-3);
  }

  .td-inbox__header .td-btn {
    width: 100%;
    min-height: 44px;
    justify-content: center;
  }

  .td-inbox__layout {
    gap: var(--td-space-3);
  }
}
</style>
