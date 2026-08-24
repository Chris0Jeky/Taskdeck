<script setup lang="ts">
import { TdBadge, TdEmptyState, TdInlineAlert, TdSkeleton, TdSpinner } from '../ui'
import { statusLabel, statusBadgeVariant, sourceLabel, canMutateSelection, triageButtonLabel } from './inboxUtils'
import type { CaptureItem } from '../../types/capture'

const props = withDefaults(defineProps<{
  selectedItemId: string | null
  selectedItem: CaptureItem | null
  hashLoadFailedItemId: string | null
  loadingDetail: boolean
  actionBusyItemId: string | null
  triagePollingItemId: string | null
  isEditingSuggestion: boolean
  editedText: string
  editedTitleHint: string
  readOnly?: boolean
}>(), { readOnly: false })

const emit = defineEmits<{
  (e: 'close-detail'): void
  (e: 'refresh-detail'): void
  (e: 'triage-selected'): void
  (e: 'ignore-selected'): void
  (e: 'cancel-selected'): void
  (e: 'start-edit-suggestion'): void
  (e: 'cancel-edit-suggestion'): void
  (e: 'save-edited-suggestion'): void
  (e: 'open-proposal', proposalId: string): void
  (e: 'update:editedText', value: string): void
  (e: 'update:editedTitleHint', value: string): void
}>()

const canTriageSelection = canMutateSelection
</script>

<template>
  <section class="td-inbox__detail-panel" aria-label="Capture item detail" aria-live="polite">
    <div
      v-if="hashLoadFailedItemId && !selectedItemId"
      class="td-inbox__detail-feedback"
      data-testid="inbox-detail-error"
    >
      <TdInlineAlert variant="error">
        Unable to load capture detail.
      </TdInlineAlert>
    </div>
    <div v-else-if="!selectedItemId" class="td-inbox__detail-feedback" data-testid="inbox-detail-placeholder">
      <TdEmptyState
        title="No item selected"
        :description="props.readOnly
          ? 'Select an item to inspect the retained capture. Archived capture history is read-only.'
          : 'Select an item to inspect the captured text and decide whether to triage, ignore, or cancel it.'"
      >
        <template #icon>
          <svg width="36" height="36" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <rect x="3" y="5" width="18" height="14" rx="2" stroke="currentColor" stroke-width="1.5"/>
            <path d="M8 10h8M8 14h5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
          </svg>
        </template>
      </TdEmptyState>
    </div>
    <div
      v-else-if="loadingDetail && !selectedItem"
      class="td-inbox__detail-feedback"
      data-testid="inbox-detail-loading"
      role="status"
    >
      <span class="sr-only">Loading capture detail...</span>
      <div class="td-inbox__detail-skeleton">
        <TdSkeleton width="40%" height="20px" />
        <TdSkeleton width="60%" height="14px" />
        <TdSkeleton width="100%" height="200px" />
        <div class="td-inbox__detail-skeleton-actions">
          <TdSkeleton width="100px" height="32px" />
          <TdSkeleton width="80px" height="32px" />
        </div>
      </div>
    </div>
    <div v-else-if="!selectedItem" class="td-inbox__detail-feedback" data-testid="inbox-detail-missing">
      <TdInlineAlert variant="error">
        Unable to load capture detail.
      </TdInlineAlert>
    </div>

    <article v-else class="td-inbox-detail">
      <header class="td-inbox-detail__header">
        <div>
          <h2>Capture Detail</h2>
          <div class="td-inbox-detail__meta">
            <TdBadge :variant="statusBadgeVariant(selectedItem.status)" size="sm">{{ statusLabel(selectedItem.status) }}</TdBadge>
            <TdBadge variant="default" size="sm">{{ sourceLabel(selectedItem.source) }}</TdBadge>
            <span class="td-inbox-detail__timestamp">{{ new Date(selectedItem.createdAt).toLocaleString() }}</span>
          </div>
        </div>
        <button class="td-btn td-btn--ghost" @click="emit('close-detail')">Close (Esc)</button>
      </header>

      <div class="td-inbox-detail__content">
        <div v-if="loadingDetail" class="td-inbox-detail__spinner">
          <TdSpinner label="Refreshing detail..." />
        </div>
        <template v-else-if="isEditingSuggestion && selectedItem.canEditSuggestion === true && !props.readOnly">
          <label for="inbox-edit-text" class="td-inbox-detail__edit-label">Capture Text</label>
          <textarea
            id="inbox-edit-text"
            :value="editedText"
            class="td-inbox-detail__edit-textarea"
            data-testid="suggestion-edit-textarea"
            rows="12"
            placeholder="Edit the capture text before triage..."
            @input="emit('update:editedText', ($event.target as HTMLTextAreaElement).value)"
          />
          <label for="inbox-edit-title" class="td-inbox-detail__edit-label">Title Hint (optional)</label>
          <input
            id="inbox-edit-title"
            :value="editedTitleHint"
            class="td-inbox-detail__edit-input"
            data-testid="suggestion-edit-title"
            type="text"
            placeholder="Short title for the resulting card..."
            @input="emit('update:editedTitleHint', ($event.target as HTMLInputElement).value)"
          />
          <div class="td-inbox-detail__edit-actions">
            <button
              class="td-btn td-btn--primary td-btn--sm"
              data-testid="suggestion-save-btn"
              :disabled="!editedText.trim() || actionBusyItemId === selectedItem.id"
              @click="emit('save-edited-suggestion')"
            >
              {{ actionBusyItemId === selectedItem.id ? 'Saving...' : 'Save' }}
            </button>
            <button
              class="td-btn td-btn--ghost td-btn--sm"
              data-testid="suggestion-cancel-btn"
              @click="emit('cancel-edit-suggestion')"
            >
              Cancel
            </button>
          </div>
        </template>
        <template v-else>
          <pre class="td-inbox-detail__text">{{ selectedItem.rawText }}</pre>
          <button
            v-if="selectedItem.canEditSuggestion === true && !props.readOnly"
            class="td-btn td-btn--secondary td-btn--sm td-inbox-detail__edit-btn"
            data-testid="suggestion-edit-btn"
            @click="emit('start-edit-suggestion')"
          >
            Edit Text
          </button>
        </template>
      </div>

      <TdInlineAlert
        v-if="selectedItem.status === 6 || selectedItem.status === 'Failed'"
        variant="error"
        data-testid="capture-error-banner"
      >
        <p class="td-inbox-detail__error-title">Triage failed</p>
        <p v-if="selectedItem.errorMessage" class="td-inbox-detail__error-msg">{{ selectedItem.errorMessage }}</p>
        <p v-if="props.readOnly" class="td-inbox-detail__error-hint">
          This archived capture is retained for inspection. Restore the board before changing its capture workflow.
        </p>
        <p v-else-if="selectedItem.canEditSuggestion === true" class="td-inbox-detail__error-hint">
          You can edit the text and retry, or ignore this capture if it is no longer needed.
        </p>
        <p v-else class="td-inbox-detail__error-hint">
          Editing is unavailable for this capture. Retry triage or ignore this capture if it is no longer needed.
        </p>
      </TdInlineAlert>

      <div v-if="selectedItem.provenance?.proposalId" class="td-inbox-detail__proposal-link" data-testid="inbox-proposal-link">
        <TdInlineAlert variant="success">
          <div class="td-inbox-detail__proposal-link-content">
            <span>{{ props.readOnly ? 'Open the related retained decision record.' : 'A proposed board update is ready for approval.' }}</span>
            <button
              class="td-btn td-btn--primary td-btn--sm"
              @click="emit('open-proposal', selectedItem.provenance!.proposalId!)"
            >
              Open in Review
            </button>
          </div>
        </TdInlineAlert>
      </div>

      <footer class="td-inbox-detail__actions">
        <button
          class="td-btn td-btn--secondary"
          @click="emit('refresh-detail')"
          :disabled="loadingDetail"
        >
          {{ loadingDetail ? 'Refreshing...' : 'Refresh Detail' }}
        </button>
        <button
          v-if="!props.readOnly"
          class="td-btn td-btn--primary"
          @click="emit('triage-selected')"
          :disabled="actionBusyItemId === selectedItem.id || !canTriageSelection(selectedItem.status)"
        >
          {{ actionBusyItemId === selectedItem.id ? 'Working...' : triageButtonLabel(selectedItem.status, triagePollingItemId, selectedItemId) }}
        </button>
        <button
          v-if="!props.readOnly"
          class="td-btn td-btn--danger"
          @click="emit('ignore-selected')"
          :disabled="actionBusyItemId === selectedItem.id || !canMutateSelection(selectedItem.status)"
        >
          {{ actionBusyItemId === selectedItem.id ? 'Working...' : 'Ignore' }}
        </button>
        <button
          v-if="!props.readOnly"
          class="td-btn td-btn--secondary"
          @click="emit('cancel-selected')"
          :disabled="actionBusyItemId === selectedItem.id || !canMutateSelection(selectedItem.status)"
        >
          {{ actionBusyItemId === selectedItem.id ? 'Working...' : 'Cancel' }}
        </button>
      </footer>
    </article>
  </section>
</template>

<style scoped>
/* ---- Detail panel ---- */

.td-inbox__detail-panel {
  padding: var(--td-space-4);
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-lg);
  min-height: 580px;
  background: var(--td-surface-low, #1c1b1b);
}

.td-inbox-detail {
  display: flex;
  flex-direction: column;
  height: 100%;
  gap: var(--td-space-4);
}

/* Glass header effect */
.td-inbox-detail__header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-3);
  background: var(--td-glass-bg, rgba(32, 31, 31, 0.8));
  backdrop-filter: blur(var(--td-glass-blur, 16px));
  -webkit-backdrop-filter: blur(var(--td-glass-blur, 16px));
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3) var(--td-space-4);
}

.td-inbox-detail__header h2 {
  font-family: 'Manrope', system-ui, sans-serif;
  color: var(--td-text-primary);
}

.td-inbox-detail__meta {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  margin-top: var(--td-space-1);
  flex-wrap: wrap;
}

.td-inbox-detail__timestamp {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-xs);
}

.td-inbox-detail__spinner {
  display: flex;
  justify-content: center;
  padding: var(--td-space-6);
}

.td-inbox__detail-feedback {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 300px;
  padding: var(--td-space-4);
}

.td-inbox__detail-skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  width: 100%;
}

.td-inbox__detail-skeleton-actions {
  display: flex;
  gap: var(--td-space-2);
  justify-content: flex-end;
}

.td-inbox-detail__content {
  flex: 1;
}

.td-inbox-detail__text {
  white-space: pre-wrap;
  word-break: break-word;
  background: var(--td-surface-lowest, #0e0e0e);
  border: 0.5px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
  min-height: 320px;
  margin: 0;
  font-size: var(--td-font-sm);
  line-height: 1.45;
  color: var(--td-text-primary);
}

.td-inbox-detail__edit-btn {
  margin-top: var(--td-space-2);
}

.td-inbox-detail__edit-label {
  display: block;
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  margin-bottom: var(--td-space-1);
  font-weight: 600;
}

.td-inbox-detail__edit-textarea {
  width: 100%;
  min-height: 240px;
  background: var(--td-surface-lowest, #0e0e0e);
  border: 0.5px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
  font-size: var(--td-font-sm);
  line-height: 1.45;
  color: var(--td-text-primary);
  resize: vertical;
  font-family: inherit;
  margin-bottom: var(--td-space-3);
}

.td-inbox-detail__edit-textarea:focus {
  outline: none;
  border-color: var(--td-color-ember, #ff4d4d);
}

.td-inbox-detail__edit-input {
  width: 100%;
  background: var(--td-surface-lowest, #0e0e0e);
  border: 0.5px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-2) var(--td-space-3);
  font-size: var(--td-font-sm);
  color: var(--td-text-primary);
  margin-bottom: var(--td-space-3);
}

.td-inbox-detail__edit-input:focus {
  outline: none;
  border-color: var(--td-color-ember, #ff4d4d);
}

.td-inbox-detail__edit-actions {
  display: flex;
  gap: var(--td-space-2);
}

.td-inbox-detail__actions {
  display: flex;
  gap: var(--td-space-2);
  justify-content: flex-end;
}

/* ---- Error banner ---- */

.td-inbox-detail__error-title {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.2em;
  margin: 0 0 var(--td-space-1) 0;
  font-weight: 600;
}

.td-inbox-detail__error-msg {
  color: var(--td-text-primary);
  font-size: var(--td-font-sm);
  line-height: 1.5;
  margin: 0 0 var(--td-space-1) 0;
  word-break: break-word;
}

.td-inbox-detail__error-hint {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-xs);
  line-height: 1.5;
  margin: 0;
}

.td-inbox-detail__proposal-link-content {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  font-size: var(--td-font-sm);
  flex-wrap: wrap;
}

/* ---- Responsive ---- */

@media (max-width: 1024px) {
  .td-inbox__detail-panel {
    min-height: 320px;
  }
}

@media (max-width: 640px) {
  .td-inbox__detail-panel {
    min-height: auto;
    border-radius: var(--td-radius-md);
    padding: var(--td-space-3);
  }

  .td-inbox-detail__header {
    flex-direction: column;
    gap: var(--td-space-2);
    padding: var(--td-space-3);
  }

  .td-inbox-detail__text {
    min-height: 200px;
    font-size: var(--td-font-sm);
  }

  .td-inbox-detail__actions {
    flex-direction: column;
  }

  .td-inbox-detail__actions .td-btn {
    width: 100%;
    min-height: 44px;
    justify-content: center;
  }

  .td-inbox-detail__proposal-link-content {
    flex-direction: column;
    align-items: stretch;
    text-align: center;
  }

  .td-inbox-detail__proposal-link-content .td-btn {
    min-height: 44px;
    justify-content: center;
  }
}
</style>
