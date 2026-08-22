<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import { getErrorDisplay } from '../../../composables/useErrorMapper'
import { useCaptureStore } from '../../../store/captureStore'
import type { CaptureItem } from '../../../types/capture'

/**
 * PaperTriageRowEdit — inline pre-triage text editor for one capture row
 * (GH-1951).
 *
 * A port, not a new feature: the Legacy detail panel
 * (`components/inbox/InboxDetailPanel.vue`, `suggestion-edit-*`) has had this
 * affordance since the capture-edit endpoint shipped, and Paper simply had no
 * detail surface to hang it on. Nothing is added to the backend — the text is
 * read through `captureStore.fetchDetail` and written through
 * `captureStore.updateSuggestion` (`PUT /api/capture/items/{id}/suggestion`).
 *
 * The editor is INLINE in the row rather than behind a drawer, following the
 * board picker in `PaperTriageTable` — the idiom this surface already uses when
 * a row needs a little extra state before a decision. It reaches into the
 * capture store directly for the same reason the table reaches into the board
 * store: the data is row-local and exists only while the affordance is open,
 * so threading it through the view as props would buy nothing.
 *
 * Why a fetch at all: a row summary carries `textExcerpt`, never `rawText`.
 * Offering the excerpt for editing would SAVE the truncation, so the textarea
 * does not appear until the full text lands.
 *
 * `canEditSuggestion` is the server's own answer to "would this write be
 * accepted" — it is false for a transcript-linked capture whatever its status,
 * and absent on an API older than the field. Anything other than an explicit
 * `true` renders the explanation instead of a textarea whose Save would 409:
 * the same never-enabled-and-silent rule the rest of this surface follows.
 *
 * Provenance is untouched. The request carries text only, and the server keeps
 * an existing title hint when the field is omitted (`TitleHint ?? current`), so
 * a text-only edit cannot silently clear it.
 */
const props = defineProps<{
  itemId: string
}>()

const emit = defineEmits<{
  (event: 'close'): void
  (event: 'saved', itemId: string): void
}>()

const captureStore = useCaptureStore()
const { t } = useI18n()

/**
 * `blocked` is the server saying no BEFORE anything is offered; `error` is the
 * fetch itself failing. They are separate states because the answers differ:
 * one is final for this capture, the other is worth retrying.
 */
type LoadState = 'loading' | 'ready' | 'blocked' | 'error'

const loadState = ref<LoadState>('loading')
const loadErrorMessage = ref<string | null>(null)
const saveErrorMessage = ref<string | null>(null)
const saving = ref(false)
const draft = ref('')
const originalText = ref('')

const textareaId = computed(() => `capture-edit-text-${props.itemId}`)
const saveReasonId = computed(() => `capture-edit-reason-${props.itemId}`)

/**
 * Why Save is off, or `null` when it is live.
 *
 * One source of truth for the `disabled` binding, the guard inside `save()` and
 * the visible reason — exactly as `boardPickBlock` is in `PaperTriageTable`, so
 * the button can never end up off for a reason nobody stated.
 *
 * `empty` is reported first: an emptied textarea is also `unchanged` when the
 * capture was empty to begin with, and "text can't be empty" is the reason the
 * server would give.
 */
type SaveBlock = 'empty' | 'unchanged'

const saveBlock = computed<SaveBlock | null>(() => {
  if (draft.value.trim().length === 0) return 'empty'
  if (draft.value === originalText.value) return 'unchanged'
  return null
})

const saveBlockMessage = computed(() =>
  saveBlock.value ? t(`inbox.triage.edit.blocked.${saveBlock.value}`) : '',
)

async function load() {
  loadState.value = 'loading'
  loadErrorMessage.value = null
  saveErrorMessage.value = null
  try {
    // `forceRefresh` on purpose: a cached detail can predate a triage attempt,
    // and BOTH the text being edited and the permission to edit it have to be
    // the server's current answer rather than a remembered one.
    //
    // The store's shared `detailError` and error toast are suppressed: they
    // belong to the Legacy detail panel, and this failure has a place of its
    // own to be shown, right under the row it happened on.
    //
    // Annotated rather than inferred: `fetchDetail`'s demo-mode branch returns
    // an object literal built from a summary, so the inferred return is a union
    // in which the optional `canEditSuggestion` is absent from one arm. The
    // literal is assignable to `CaptureItem` (the field is optional), and an
    // absent flag is exactly the conservative "not editable" answer below.
    const detail: CaptureItem = await captureStore.fetchDetail(props.itemId, {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })
    if (detail.canEditSuggestion !== true) {
      loadState.value = 'blocked'
      return
    }
    originalText.value = detail.rawText
    draft.value = detail.rawText
    loadState.value = 'ready'
  } catch (e: unknown) {
    loadErrorMessage.value = getErrorDisplay(e, t('inbox.triage.edit.unknownReason')).message
    loadState.value = 'error'
  }
}

async function save() {
  // Belt and braces behind the disabled button — every branch that stops the
  // write also renders its reason above the button (`saveBlock`).
  if (saveBlock.value !== null || saving.value) return
  saving.value = true
  saveErrorMessage.value = null
  try {
    await captureStore.updateSuggestion(props.itemId, { text: draft.value })
    // The store caches the updated detail and rewrites the row summary from it,
    // so the excerpt behind this editor is already the saved text as it closes.
    emit('saved', props.itemId)
    emit('close')
  } catch (e: unknown) {
    // The draft is deliberately KEPT. A failed save must not cost the user the
    // text they just wrote, and closing on failure would look like a success.
    // The store raises its own toast; this keeps the reason next to the control
    // that failed, where the retry is.
    saveErrorMessage.value = getErrorDisplay(e, t('inbox.triage.edit.unknownReason')).message
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="paper-triage-edit" data-testid="capture-edit" :data-edit-state="loadState">
    <div
      v-if="loadState === 'loading'"
      class="paper-triage-edit__status"
      role="status"
      data-testid="capture-edit-loading"
    >
      <span class="tk-meta">{{ t('inbox.triage.edit.loading') }}</span>
    </div>

    <div
      v-else-if="loadState === 'error'"
      class="paper-triage-edit__status"
      data-testid="capture-edit-load-error"
    >
      <p class="paper-triage-edit__reason" role="alert">
        {{ t('inbox.triage.edit.loadFailed', { reason: loadErrorMessage }) }}
      </p>
      <div class="paper-triage-edit__actions">
        <PaperHLBtn
          :label="t('inbox.triage.edit.retry')"
          data-action="edit-retry"
          @click="load"
        />
        <PaperHLBtn
          :label="t('inbox.triage.edit.cancel')"
          variant="ghost"
          data-action="edit-cancel"
          @click="emit('close')"
        />
      </div>
    </div>

    <div
      v-else-if="loadState === 'blocked'"
      class="paper-triage-edit__status"
      data-testid="capture-edit-blocked"
    >
      <p class="paper-triage-edit__reason" role="status">
        {{ t('inbox.triage.edit.blocked.notEditable') }}
      </p>
      <div class="paper-triage-edit__actions">
        <PaperHLBtn
          :label="t('inbox.triage.edit.close')"
          variant="ghost"
          data-action="edit-cancel"
          @click="emit('close')"
        />
      </div>
    </div>

    <template v-else>
      <label class="paper-triage-edit__label tk-eyebrow" :for="textareaId">
        {{ t('inbox.triage.edit.label') }}
      </label>
      <textarea
        :id="textareaId"
        v-model="draft"
        class="paper-triage-edit__textarea"
        rows="5"
        data-testid="capture-edit-textarea"
        :placeholder="t('inbox.triage.edit.placeholder')"
      />
      <p class="paper-triage-edit__hint tk-meta">{{ t('inbox.triage.edit.hint') }}</p>
      <p
        v-if="saveErrorMessage"
        class="paper-triage-edit__reason"
        role="alert"
        data-testid="capture-edit-save-error"
      >
        {{ t('inbox.triage.edit.saveFailed', { reason: saveErrorMessage }) }}
      </p>
      <p
        v-else-if="saveBlock"
        :id="saveReasonId"
        class="paper-triage-edit__reason"
        role="status"
        data-testid="capture-edit-save-reason"
        :data-reason="saveBlock"
      >
        {{ saveBlockMessage }}
      </p>
      <div class="paper-triage-edit__actions">
        <PaperHLBtn
          :label="saving ? t('inbox.triage.edit.saving') : t('inbox.triage.edit.save')"
          variant="ember"
          :disabled="saveBlock !== null || saving"
          :aria-describedby="saveBlock ? saveReasonId : undefined"
          data-action="edit-save"
          @click="save"
        />
        <PaperHLBtn
          :label="t('inbox.triage.edit.cancel')"
          variant="ghost"
          :disabled="saving"
          data-action="edit-cancel"
          @click="emit('close')"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.paper-triage-edit {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 10px;
  padding: 12px 14px;
  border: 1px solid var(--line-soft);
  border-left: 2px solid var(--ember);
  border-radius: 3px;
  background: var(--paper);
}
.paper-triage-edit__status {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.paper-triage-edit__label {
  color: var(--mute);
}
.paper-triage-edit__textarea {
  width: 100%;
  min-height: 96px;
  padding: 8px 10px;
  border: 1px solid var(--line-soft);
  border-bottom-color: var(--line);
  border-radius: 2px;
  background: var(--paper-card);
  font-family: var(--sans);
  font-size: 13.5px;
  line-height: 1.5;
  color: var(--ink);
  resize: vertical;
  outline: none;
}
.paper-triage-edit__textarea:focus {
  border-color: var(--ember);
}
.paper-triage-edit__hint {
  margin: 0;
  color: var(--mute);
}
.paper-triage-edit__reason {
  margin: 0;
  max-width: 60ch;
  font-family: var(--sans);
  font-size: 12px;
  line-height: 1.4;
  color: var(--overdue);
}
.paper-triage-edit__actions {
  display: flex;
  gap: 6px;
}
</style>
