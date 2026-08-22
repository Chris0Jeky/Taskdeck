<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useBoardStore } from '../../../store/boardStore'
import PaperBoardDialogShell from './PaperBoardDialogShell.vue'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import type { Board } from '../../../types/board'
import { logError } from '../../../utils/errorReporting'

/**
 * PaperBoardSettingsDialog — Paper-skinned board rename / description /
 * archive / restore. The Paper counterpart of
 * `components/board/BoardSettingsModal.vue`, driving the same `boardStore`
 * actions (`updateBoard`, `deleteBoard`).
 *
 * There is no permanent-delete control here because Taskdeck has none:
 * `DELETE /api/boards/{id}` calls `board.Archive()` (a soft delete) and
 * `boardCrudStore.deleteBoard` toasts "Board archived successfully". Labelling
 * that button "Delete" would promise a destruction the server does not perform,
 * so it says "Move to archive" — same wording as Legacy, same truth.
 *
 * The archive ORDER is load-bearing and copied from the Legacy modal (#519):
 * navigate away FIRST, then run the store mutation. `deleteBoard` nulls
 * `currentBoard`, cards, labels and presence one ref at a time; doing that
 * while the board view is still mounted cascades through every computed and
 * froze the UI for ~30s.
 */
const props = defineProps<{
  board: Board
  isOpen: boolean
}>()

const emit = defineEmits<{
  (event: 'close'): void
  (event: 'updated'): void
}>()

const { t } = useI18n()
const boardStore = useBoardStore()
const router = useRouter()

const name = ref('')
const description = ref('')
const confirmingArchive = ref(false)
const busy = ref(false)
const error = ref<string | null>(null)

watch(
  [() => props.board, () => props.isOpen],
  ([board, isOpen]) => {
    if (!board || !isOpen) return
    name.value = board.name
    description.value = board.description ?? ''
    confirmingArchive.value = false
    error.value = null
  },
  { immediate: true },
)

const isValid = computed(() => name.value.trim().length > 0)

function close() {
  emit('close')
}

async function save() {
  if (!isValid.value || busy.value) return

  busy.value = true
  error.value = null
  try {
    await boardStore.updateBoard(props.board.id, {
      // `null` means "leave unchanged" in UpdateBoardDto — mirrors BoardSettingsModal.
      name: name.value.trim() !== props.board.name ? name.value.trim() : null,
      description: description.value !== (props.board.description ?? '') ? description.value : null,
      isArchived: null,
    })
    emit('updated')
    close()
  } catch (e) {
    logError('Failed to update board (paper):', e)
    error.value = t('boardDetail.boardDialog.saveError')
  } finally {
    busy.value = false
  }
}

function requestArchive() {
  confirmingArchive.value = true
}

function cancelArchive() {
  confirmingArchive.value = false
}

async function confirmArchive() {
  if (busy.value) return

  busy.value = true
  error.value = null
  try {
    // See the #519 note above: unmount the board view before the store teardown.
    emit('updated')
    close()
    await router.push({ name: 'workspace-boards' })
    await boardStore.deleteBoard(props.board.id)
  } catch (e) {
    logError('Failed to archive board (paper):', e)
    error.value = t('boardDetail.boardDialog.archiveError')
  } finally {
    busy.value = false
    confirmingArchive.value = false
  }
}

async function restore() {
  if (busy.value) return

  busy.value = true
  error.value = null
  try {
    await boardStore.updateBoard(props.board.id, { isArchived: false })
    emit('updated')
    close()
  } catch (e) {
    logError('Failed to restore board (paper):', e)
    error.value = t('boardDetail.boardDialog.restoreError')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <PaperBoardDialogShell
    :is-open="isOpen"
    :eyebrow="t('boardDetail.boardDialog.eyebrow')"
    :title="t('boardDetail.boardDialog.title')"
    :close-label="t('boardDetail.boardDialog.close')"
    testid="paper-board-dialog"
    @close="close"
  >
    <div class="paper-board-field">
      <label class="paper-board-field__label" for="paper-board-name">
        {{ t('boardDetail.boardDialog.nameLabel') }}
      </label>
      <input
        id="paper-board-name"
        v-model="name"
        type="text"
        class="paper-board-field__input"
        :placeholder="t('boardDetail.boardDialog.namePlaceholder')"
        :disabled="busy"
        data-testid="paper-board-dialog-name"
      />
    </div>

    <div class="paper-board-field">
      <label class="paper-board-field__label" for="paper-board-description">
        {{ t('boardDetail.boardDialog.descriptionLabel') }}
      </label>
      <textarea
        id="paper-board-description"
        v-model="description"
        rows="3"
        class="paper-board-field__input paper-board-field__input--area"
        :placeholder="t('boardDetail.boardDialog.descriptionPlaceholder')"
        :disabled="busy"
        data-testid="paper-board-dialog-description"
      ></textarea>
    </div>

    <div class="paper-board-field paper-board-field--boxed">
      <span class="paper-board-field__label">{{ t('boardDetail.boardDialog.lifecycle') }}</span>
      <span class="paper-board-field__state" data-testid="paper-board-dialog-state">
        {{
          board.isArchived
            ? t('boardDetail.boardDialog.stateArchived')
            : t('boardDetail.boardDialog.stateActive')
        }}
      </span>
      <p class="paper-board-field__hint">
        {{
          board.isArchived
            ? t('boardDetail.boardDialog.restoreHint')
            : t('boardDetail.boardDialog.archiveHint')
        }}
      </p>
    </div>

    <div
      v-if="confirmingArchive"
      class="paper-board-confirm"
      role="alert"
      data-testid="paper-board-dialog-archive-confirm"
    >
      <p class="paper-board-confirm__copy">
        {{ t('boardDetail.boardDialog.archiveConfirm', { name: board.name }) }}
      </p>
      <div class="paper-board-confirm__actions">
        <PaperHLBtn
          variant="ember"
          :label="t('boardDetail.boardDialog.archiveConfirmAction')"
          :disabled="busy"
          data-testid="paper-board-dialog-archive-confirm-yes"
          @click="confirmArchive"
        />
        <PaperHLBtn
          variant="ghost"
          :label="t('boardDetail.boardDialog.archiveConfirmCancel')"
          :disabled="busy"
          data-testid="paper-board-dialog-archive-confirm-no"
          @click="cancelArchive"
        />
      </div>
    </div>

    <p v-if="error" class="paper-board-error" role="alert" data-testid="paper-board-dialog-error">
      {{ error }}
    </p>

    <template #footer>
      <PaperHLBtn
        v-if="board.isArchived"
        variant="ghost"
        :label="t('boardDetail.boardDialog.restore')"
        :disabled="busy"
        data-testid="paper-board-dialog-restore"
        @click="restore"
      />
      <PaperHLBtn
        v-else
        variant="ghost"
        :label="t('boardDetail.boardDialog.archive')"
        :disabled="busy || confirmingArchive"
        data-testid="paper-board-dialog-archive"
        @click="requestArchive"
      />
      <div class="paper-board-dialog__foot-primary">
        <PaperHLBtn
          :label="t('boardDetail.boardDialog.cancel')"
          :disabled="busy"
          data-testid="paper-board-dialog-cancel"
          @click="close"
        />
        <PaperHLBtn
          variant="primary"
          :label="t('boardDetail.boardDialog.save')"
          :disabled="busy || !isValid"
          data-testid="paper-board-dialog-save"
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

.paper-board-field__state {
  font-family: var(--serif);
  font-size: 14px;
  color: var(--ink-deep);
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

.paper-board-field__input--area {
  resize: vertical;
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
