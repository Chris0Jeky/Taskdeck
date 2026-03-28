<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { starterPacksApi } from '../../api/starterPacksApi'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { useBoardStore } from '../../store/boardStore'
import { useToastStore } from '../../store/toastStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { getErrorMessage } from '../../utils/errorMessage'
import { workspaceSetupOptions } from './workspaceSetupOptions'

const props = defineProps<{
  isOpen: boolean
}>()

const emit = defineEmits<{
  close: []
  created: [payload: { boardId: string; templateId: string }]
}>()

const router = useRouter()
const boardStore = useBoardStore()
const toast = useToastStore()
const workspace = useWorkspaceStore()

const boardName = ref('')
const selectedSetupId = ref(workspaceSetupOptions[0]?.id ?? 'blank-board')
const setupError = ref<string | null>(null)
const submitting = ref(false)

const selectedSetup = computed(() =>
  workspaceSetupOptions.find((option) => option.id === selectedSetupId.value) ?? workspaceSetupOptions[0]
)
const canSubmit = computed(() => boardName.value.trim().length > 0 && !submitting.value)

function resetState() {
  boardName.value = ''
  selectedSetupId.value = workspaceSetupOptions[0]?.id ?? 'blank-board'
  setupError.value = null
  submitting.value = false
}

function closeModal() {
  if (submitting.value) {
    return
  }

  emit('close')
}

async function applyStarterPack(boardId: string, starterPackId: string): Promise<void> {
  const catalog = await starterPacksApi.getCatalog(boardId)
  const selectedPack = catalog.find((entry) => entry.id === starterPackId)
  if (!selectedPack) {
    throw new Error('The selected starter pack is no longer available.')
  }

  const result = await starterPacksApi.applyStarterPack(boardId, {
    manifest: selectedPack.manifest,
    dryRun: false,
  })

  if (result.hasBlockingConflicts || !result.applied) {
    throw new Error('The starter pack could not be applied to the new board.')
  }

  if (result.hasConflicts) {
    toast.warning('Board created with template warnings. Review the board before sharing it.')
  } else {
    toast.success(`Applied ${selectedPack.title}.`)
  }
}

async function submitSetup() {
  if (!canSubmit.value || !selectedSetup.value) {
    return
  }

  submitting.value = true
  setupError.value = null
  const nextBoardName = boardName.value.trim()

  try {
    const board = await boardStore.createBoard({ name: nextBoardName })

    if (selectedSetup.value.starterPackId) {
      try {
        await applyStarterPack(board.id, selectedSetup.value.starterPackId)
      } catch (error: unknown) {
        const message = getErrorMessage(error, 'Board created, but the starter pack could not be applied')
        toast.warning(`${message}. You can still finish setup from the board view.`)
      }
    }

    workspace.clearHomeSummary()
    workspace.clearTodaySummary()
    emit('created', { boardId: board.id, templateId: selectedSetup.value.id })
    emit('close')
    void router.push(`/workspace/boards/${board.id}`)
    resetState()
  } catch (error: unknown) {
    setupError.value = getErrorMessage(error, 'Failed to create the board')
  } finally {
    submitting.value = false
  }
}

watch(
  () => props.isOpen,
  (isOpen, _, onCleanup) => {
    if (!isOpen) {
      resetState()
      return
    }

    const unregisterEscapeHandler = registerEscapeHandler(closeModal)
    onCleanup(() => {
      unregisterEscapeHandler()
    })
  },
)
</script>

<template>
  <div
    v-if="isOpen"
    class="td-overlay"
    role="dialog"
    aria-label="Workspace setup"
    aria-modal="true"
    @click.self="closeModal"
  >
    <div class="td-setup-modal">
      <header class="td-setup-modal__header">
        <div>
          <p class="td-setup-modal__eyebrow">First Useful Board</p>
          <h2>Start from a board you can use today</h2>
          <p class="td-setup-modal__subtitle">
            Name the board, pick the shape you want, then move straight into capture and review.
          </p>
        </div>
        <button class="td-btn td-btn--ghost" @click="closeModal">Close</button>
      </header>

      <div class="td-setup-modal__body">
        <label class="td-field">
          <span class="td-field__label">Board name</span>
          <input
            v-model="boardName"
            class="td-input"
            type="text"
            maxlength="100"
            placeholder="For example: Product Sprint"
          />
        </label>

        <fieldset class="td-setup-modal__options">
          <legend class="td-field__label">Setup shape</legend>
          <label
            v-for="option in workspaceSetupOptions"
            :key="option.id"
            :class="[
              'td-setup-option',
              selectedSetupId === option.id ? 'td-setup-option--selected' : '',
            ]"
          >
            <input
              v-model="selectedSetupId"
              class="td-setup-option__radio"
              type="radio"
              name="workspace-setup-option"
              :value="option.id"
            />
            <div class="td-setup-option__content">
              <span class="td-setup-option__title">{{ option.title }}</span>
              <span class="td-setup-option__summary">{{ option.summary }}</span>
              <span class="td-setup-option__helper">{{ option.helper }}</span>
            </div>
          </label>
        </fieldset>

        <div v-if="selectedSetup" class="td-setup-modal__note">
          <strong>Next step:</strong>
          {{ selectedSetup.starterPackId ? 'The board will open with a starter workflow applied.' : 'The board will open blank so you can shape it yourself.' }}
        </div>

        <div v-if="setupError" class="td-alert td-alert--error" role="alert">
          {{ setupError }}
        </div>
      </div>

      <footer class="td-setup-modal__footer">
        <button class="td-btn td-btn--secondary" @click="closeModal" :disabled="submitting">Cancel</button>
        <button class="td-btn td-btn--primary" @click="submitSetup" :disabled="!canSubmit">
          {{ submitting ? 'Creating...' : 'Create Board' }}
        </button>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.td-overlay {
  position: fixed;
  inset: 0;
  background: rgba(10, 15, 24, 0.52);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--td-space-4);
  z-index: 60;
}

.td-setup-modal {
  width: min(760px, 100%);
  max-height: min(90vh, 100%);
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-xl);
  box-shadow: var(--td-shadow-xl);
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
  padding: var(--td-space-5);
}

.td-setup-modal__header {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-3);
}

.td-setup-modal__eyebrow {
  margin: 0 0 var(--td-space-1);
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

.td-setup-modal__header h2 {
  margin: 0;
  font-size: var(--td-font-2xl);
}

.td-setup-modal__subtitle {
  margin: var(--td-space-2) 0 0;
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-setup-modal__body {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
  overflow-y: auto;
  min-height: 0;
}

.td-field {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-field__label {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-text-tertiary);
}

.td-input {
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-primary);
  color: var(--td-text-primary);
  padding: 0.8rem 0.9rem;
  font-size: var(--td-font-base);
}

.td-setup-modal__options {
  border: none;
  padding: 0;
  margin: 0;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--td-space-3);
}

.td-setup-option {
  position: relative;
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  background: var(--td-surface-secondary);
  cursor: pointer;
  padding: var(--td-space-3);
}

.td-setup-option--selected {
  border-color: var(--td-color-primary);
  box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--td-color-primary) 45%, transparent);
  background: color-mix(in srgb, var(--td-color-primary) 7%, var(--td-surface-primary));
}

.td-setup-option__radio {
  position: absolute;
  inset-inline-start: var(--td-space-3);
  inset-block-start: var(--td-space-3);
}

.td-setup-option__content {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  padding-inline-start: 1.75rem;
}

.td-setup-option__title {
  font-size: var(--td-font-base);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-setup-option__summary {
  font-size: var(--td-font-sm);
  color: var(--td-text-primary);
  line-height: 1.5;
}

.td-setup-option__helper {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-setup-modal__note {
  border-radius: var(--td-radius-lg);
  border: 1px solid var(--td-border-default);
  background: var(--td-surface-secondary);
  padding: var(--td-space-3);
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-setup-modal__footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--td-space-2);
}

.td-alert {
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
}

.td-alert--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 1px solid transparent;
  cursor: pointer;
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border-color: var(--td-border-default);
}

.td-btn--ghost {
  background: transparent;
  border-color: var(--td-border-default);
  color: var(--td-text-secondary);
}

.td-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

@media (max-width: 720px) {
  .td-setup-modal {
    padding: var(--td-space-4);
  }

  .td-setup-modal__header {
    flex-direction: column;
  }

  .td-setup-modal__footer {
    flex-direction: column-reverse;
  }
}
</style>
