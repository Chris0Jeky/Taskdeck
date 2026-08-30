<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useBoardStore } from '../../../store/boardStore'
import PaperBoardDialogShell from './PaperBoardDialogShell.vue'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import type { Column } from '../../../types/board'
import { logError } from '../../../utils/errorReporting'

/**
 * PaperColumnSettingsDialog — Paper-skinned rename / WIP-limit / delete for one
 * column. The Paper counterpart of `components/board/ColumnEditModal.vue`; it
 * drives the SAME `boardStore` actions (`updateColumn`, `deleteColumn`), so
 * persistence, toasts and realtime behaviour are identical and only the
 * chrome differs.
 *
 * Two deliberate departures from the Legacy modal:
 *
 * 1. Delete asks for confirmation IN the dialog instead of `window.confirm`,
 *    and the "this column still has cards" refusal is an inline message rather
 *    than `window.alert`. Same refusal, same guard — but assertable, and it
 *    does not depend on a browser dialog the Paper surface never otherwise uses.
 * 2. The card-count guard reads the board view's live `cardCount` prop, not
 *    `column.cardCount`. The latter is only stamped during `fetchBoard`, so
 *    after an in-session move it can claim a column is empty when it is not.
 */
const props = defineProps<{
  column: Column
  boardId: string
  isOpen: boolean
  /** Live number of cards rendered in this column — see the note above. */
  cardCount: number
}>()

const emit = defineEmits<{
  (event: 'close'): void
  (event: 'updated'): void
}>()

const { t } = useI18n()
const boardStore = useBoardStore()

const name = ref('')
const hasWipLimit = ref(false)
const wipLimit = ref<number | null>(null)
const confirmingDelete = ref(false)
const busy = ref(false)
const error = ref<string | null>(null)

/**
 * Re-seed the form whenever the dialog opens or the target column changes, so a
 * cancelled edit never leaks into the next open.
 */
watch(
  [() => props.column, () => props.isOpen],
  ([column, isOpen]) => {
    if (!column || !isOpen) return
    name.value = column.name
    hasWipLimit.value = column.wipLimit != null && column.wipLimit > 0
    wipLimit.value = column.wipLimit
    confirmingDelete.value = false
    error.value = null
  },
  { immediate: true },
)

const canDelete = computed(() => props.cardCount === 0)

const isValid = computed(() => {
  if (name.value.trim().length === 0) return false
  if (hasWipLimit.value && (wipLimit.value === null || wipLimit.value <= 0)) return false
  return true
})

function close() {
  emit('close')
}

async function save() {
  if (!isValid.value || busy.value) return

  busy.value = true
  error.value = null
  try {
    await boardStore.updateColumn(props.boardId, props.column.id, {
      // `null` means "leave unchanged" in UpdateColumnDto — mirrors ColumnEditModal.
      name: name.value.trim() !== props.column.name ? name.value.trim() : null,
      wipLimit: hasWipLimit.value ? wipLimit.value : null,
      position: null,
    })
    emit('updated')
    close()
  } catch (e) {
    logError('Failed to update column (paper):', e)
    error.value = t('boardDetail.columnDialog.saveError')
  } finally {
    busy.value = false
  }
}

function requestDelete() {
  if (!canDelete.value) return
  confirmingDelete.value = true
}

function cancelDelete() {
  confirmingDelete.value = false
}

async function confirmDelete() {
  if (!canDelete.value || busy.value) return

  busy.value = true
  error.value = null
  try {
    await boardStore.deleteColumn(props.boardId, props.column.id)
    emit('updated')
    close()
  } catch (e) {
    logError('Failed to delete column (paper):', e)
    error.value = t('boardDetail.columnDialog.deleteError')
    confirmingDelete.value = false
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <PaperBoardDialogShell
    :is-open="isOpen"
    :eyebrow="t('boardDetail.columnDialog.eyebrow')"
    :title="t('boardDetail.columnDialog.title')"
    :close-label="t('boardDetail.columnDialog.close')"
    testid="paper-column-dialog"
    @close="close"
  >
    <div class="paper-board-field">
      <label class="paper-board-field__label" for="paper-column-name">
        {{ t('boardDetail.columnDialog.nameLabel') }}
      </label>
      <input
        id="paper-column-name"
        v-model="name"
        type="text"
        class="paper-board-field__input"
        :placeholder="t('boardDetail.columnDialog.namePlaceholder')"
        :disabled="busy"
        data-testid="paper-column-dialog-name"
      />
    </div>

    <div class="paper-board-field paper-board-field--boxed">
      <label class="paper-board-field__check">
        <input
          v-model="hasWipLimit"
          type="checkbox"
          :disabled="busy"
          data-testid="paper-column-dialog-wip-toggle"
        />
        <span>{{ t('boardDetail.columnDialog.wipToggle') }}</span>
      </label>

      <template v-if="hasWipLimit">
        <label class="paper-board-field__label" for="paper-column-wip">
          {{ t('boardDetail.columnDialog.wipLabel') }}
        </label>
        <input
          id="paper-column-wip"
          v-model.number="wipLimit"
          type="number"
          min="1"
          class="paper-board-field__input"
          :disabled="busy"
          data-testid="paper-column-dialog-wip"
        />
      </template>

      <p class="paper-board-field__hint">{{ t('boardDetail.columnDialog.wipHint') }}</p>
    </div>

    <p
      v-if="!canDelete"
      class="paper-board-field__hint"
      data-testid="paper-column-dialog-delete-blocked"
    >
      {{ t('boardDetail.columnDialog.deleteBlocked') }}
    </p>

    <div
      v-if="confirmingDelete"
      class="paper-board-confirm"
      role="alert"
      data-testid="paper-column-dialog-delete-confirm"
    >
      <p class="paper-board-confirm__copy">
        {{ t('boardDetail.columnDialog.deleteConfirm', { name: column.name }) }}
      </p>
      <div class="paper-board-confirm__actions">
        <PaperHLBtn
          variant="ember"
          :label="t('boardDetail.columnDialog.deleteConfirmAction')"
          :disabled="busy"
          data-testid="paper-column-dialog-delete-confirm-yes"
          @click="confirmDelete"
        />
        <PaperHLBtn
          variant="ghost"
          :label="t('boardDetail.columnDialog.deleteConfirmCancel')"
          :disabled="busy"
          data-testid="paper-column-dialog-delete-confirm-no"
          @click="cancelDelete"
        />
      </div>
    </div>

    <p v-if="error" class="paper-board-error" role="alert" data-testid="paper-column-dialog-error">
      {{ error }}
    </p>

    <template #footer>
      <PaperHLBtn
        variant="ghost"
        :label="t('boardDetail.columnDialog.delete')"
        :disabled="busy || !canDelete || confirmingDelete"
        data-testid="paper-column-dialog-delete"
        @click="requestDelete"
      />
      <div class="paper-board-dialog__foot-primary">
        <PaperHLBtn
          :label="t('boardDetail.columnDialog.cancel')"
          :disabled="busy"
          data-testid="paper-column-dialog-cancel"
          @click="close"
        />
        <PaperHLBtn
          variant="primary"
          :label="t('boardDetail.columnDialog.save')"
          :disabled="busy || !isValid"
          data-testid="paper-column-dialog-save"
          @click="save"
        />
      </div>
    </template>
  </PaperBoardDialogShell>
</template>

<style scoped>
.paper-board-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.paper-board-field--boxed {
  border: 1px solid var(--line-soft);
  border-radius: var(--r-2);
  padding: 10px 12px;
}

.paper-board-field__label {
  font-family: var(--mono);
  font-size: 10.5px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--mute);
}

.paper-board-field__input {
  padding: 6px 10px;
  border: 1px solid var(--line-soft);
  border-radius: var(--r-2);
  background: var(--paper);
  color: var(--ink);
  font-family: var(--serif);
  font-size: 14px;
}

.paper-board-field__input::placeholder {
  font-family: var(--serif);
  font-style: italic;
  color: var(--mute);
}

.paper-board-field__input:disabled {
  opacity: 0.6;
  cursor: progress;
}

.paper-board-field__check {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-family: var(--sans);
  font-size: 13px;
  color: var(--ink);
}

.paper-board-field__hint {
  margin: 0;
  font-family: var(--sans);
  font-size: 12px;
  color: var(--mute);
}

.paper-board-confirm {
  border: 1px solid var(--ember);
  background: var(--ember-tint);
  border-radius: var(--r-2);
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.paper-board-confirm__copy {
  margin: 0;
  font-family: var(--serif);
  font-size: 14px;
  color: var(--ember-ink);
}

.paper-board-confirm__actions {
  display: flex;
  gap: 8px;
}

.paper-board-error {
  margin: 0;
  color: var(--ember-ink);
  font-family: var(--mono);
  font-size: 11px;
}

.paper-board-dialog__foot-primary {
  display: flex;
  gap: 8px;
  margin-left: auto;
}
</style>
