<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import CardEstimateField from '../../../components/board/CardEstimateField.vue'
import { parseEstimatedEffort } from '../../../utils/estimatedEffort'

/**
 * PaperCardComposer — the inline "add a card" form inside a Paper column.
 *
 * This is the DIRECT lane (#1945 / ADR-0056): submitting creates a real card
 * immediately. It is not a capture and it does not produce a proposal — the
 * `+ capture` button next to it is the door into the review lane.
 *
 * The `data-action` hooks are a contract, not decoration.
 * `useBoardKeyboardNav.createCardInSelectedColumn` (the `n` shortcut) finds a
 * column by `[data-column-id]`, clicks `[data-action="toggle-add-card"]`, then
 * focuses `[data-action="add-card-input"]`. Renaming either attribute silently
 * breaks the shortcut, which is why `PaperBoardManagement.spec.ts` drives that
 * exact DOM path rather than calling the handler.
 *
 * Local draft state lives here on purpose: the parent owns *which* column is
 * composing, this component owns the text. A successful create closes the
 * composer (parity with the Legacy `ColumnLane` form), so the draft is
 * discarded by unmount and never has to be reset across opens.
 */
const props = withDefaults(
  defineProps<{
    /** Stable column id — used to build a unique label/textarea id pair. */
    columnId: string
    busy?: boolean
    error?: string | null
  }>(),
  { busy: false, error: null },
)

const emit = defineEmits<{
  (event: 'submit', title: string, estimatedEffortMinutes?: number): void
  (event: 'cancel'): void
}>()

const { t } = useI18n()

const title = ref('')
const estimateHours = ref('')
const estimateMinutes = ref('')
const estimate = computed(() => parseEstimatedEffort(estimateHours.value, estimateMinutes.value))
const input = ref<HTMLTextAreaElement | null>(null)

onMounted(() => {
  input.value?.focus()
})

function submit() {
  const trimmed = title.value.trim()
  // A whitespace-only title is a no-op, never a request the server has to reject.
  if (!trimmed || props.busy || estimate.value.error) return
  if (estimate.value.value === null) emit('submit', trimmed)
  else emit('submit', trimmed, estimate.value.value)
}

function cancel() {
  emit('cancel')
}
</script>

<template>
  <form class="paper-card-composer" data-testid="paper-card-composer" @submit.prevent="submit">
    <label class="sr-only" :for="`paper-card-composer-${columnId}`">
      {{ t('boardDetail.card.inputLabel') }}
    </label>
    <textarea
      :id="`paper-card-composer-${columnId}`"
      ref="input"
      v-model="title"
      data-action="add-card-input"
      class="paper-card-composer__input"
      rows="2"
      :placeholder="t('boardDetail.card.placeholder')"
      :disabled="busy"
      @keydown.enter.exact.prevent="submit"
      @keydown.esc.stop.prevent="cancel"
    ></textarea>

    <details class="text-sm">
      <summary class="cursor-pointer">Add estimate (optional)</summary>
      <CardEstimateField v-model:hours="estimateHours" v-model:minutes="estimateMinutes" :read-only="busy" />
    </details>

    <div class="paper-card-composer__actions">
      <PaperHLBtn
        type="submit"
        variant="primary"
        :label="t('boardDetail.card.submit')"
        :disabled="busy || title.trim().length === 0 || !!estimate.error"
        data-testid="paper-card-composer-submit"
      />
      <PaperHLBtn
        type="button"
        variant="ghost"
        :label="t('boardDetail.card.cancel')"
        data-action="cancel-add-card"
        data-testid="paper-card-composer-cancel"
        @click="cancel"
      />
    </div>

    <p v-if="error" class="paper-card-composer__error" role="alert" data-testid="paper-card-composer-error">
      {{ error }}
    </p>
  </form>
</template>

<style scoped>
.paper-card-composer {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 8px;
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: var(--r-2);
}

.paper-card-composer__input {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--line-soft);
  border-radius: var(--r-1);
  background: var(--paper);
  color: var(--ink);
  font-family: var(--serif);
  font-size: 14px;
  resize: vertical;
}

.paper-card-composer__input::placeholder {
  font-family: var(--serif);
  font-style: italic;
  color: var(--mute);
}

.paper-card-composer__input:disabled {
  opacity: 0.6;
  cursor: progress;
}

.paper-card-composer__actions {
  display: flex;
  gap: 6px;
}

.paper-card-composer__error {
  margin: 0;
  color: var(--ember-ink);
  font-family: var(--mono);
  font-size: 10.5px;
}
</style>
