<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'

/**
 * ReviewRevisionEditor — the "Request edit" composer.
 *
 * ENTRY IS PART OF THE COMPONENT (GH-1964). This is rendered `v-if` at the
 * bottom of the deep-review column, below the diff section, and it takes the
 * shared decision lock the moment it exists. Before this, activating "Request
 * edit" on a real proposal mounted the composer far below the fold with no
 * scroll and no focus move: the visible result was four decision buttons going
 * grey and the whole review keymap going silent, which reads as a brick rather
 * than as "a composer opened". The mount hook below is therefore load-bearing
 * UX, not a nicety — it is what makes the lock's cause visible.
 *
 * It lives here rather than in the orchestrator so EVERY entry path (the rail
 * button, the `E` shortcut, any future caller) gets it: the component mounts
 * exactly once per entry.
 */
const props = defineProps<{
  operationsPayload: string
  saving?: boolean
}>()

const emit = defineEmits<{
  (event: 'save', payload: { revisedPayload: string; reason: string }): void
  (event: 'cancel'): void
}>()

interface EditableField {
  key: string
  value: string
  valueKind: 'string' | 'json'
}

const { t } = useI18n()

const rootRef = ref<HTMLElement | null>(null)
const fields = ref<EditableField[]>([])
const reason = ref('')
const parseError = ref(false)

/**
 * Bring the composer to the reviewer on entry (GH-1964).
 *
 * `block: 'nearest'` matches the diff pane's existing scroll idiom: it scrolls
 * only as far as it must, so a composer already on screen does not jump.
 * `scrollIntoView` is called optionally because happy-dom (and older browsers)
 * do not implement it — a missing scroll must never cost the focus move, which
 * is the half that also announces the composer to assistive tech.
 *
 * Focus goes to the first EDITABLE control rather than the container: the
 * reviewer asked to edit, so they can type immediately, and moving focus inside
 * the composer is what silences the review keymap by intent (`isEditableTarget`)
 * instead of only by the shared busy lock.
 */
onMounted(() => {
  const root = rootRef.value
  if (!root) return
  root.scrollIntoView?.({ behavior: 'smooth', block: 'nearest' })
  // Textareas precede the reason input in DOM order, so a payload with fields
  // focuses its first field and an empty payload falls through to the reason.
  root.querySelector<HTMLElement>('textarea, input')?.focus?.()
})

const jsonFieldErrors = computed(() => {
  const errors: Record<string, string> = {}
  for (const field of fields.value) {
    if (field.valueKind !== 'json') continue

    try {
      JSON.parse(field.value)
    } catch {
      errors[field.key] = t('review.revisionEditor.jsonError')
    }
  }

  return errors
})

const hasJsonFieldErrors = computed(() => Object.keys(jsonFieldErrors.value).length > 0)

function parsePayload() {
  try {
    const parsed = JSON.parse(props.operationsPayload) as Record<string, unknown>
    fields.value = Object.entries(parsed).map(([key, value]) => ({
      key,
      value: typeof value === 'string' ? value : JSON.stringify(value),
      valueKind: typeof value === 'string' ? 'string' : 'json',
    }))
    parseError.value = false
  } catch {
    fields.value = [{ key: 'payload', value: props.operationsPayload, valueKind: 'string' }]
    parseError.value = true
  }
}

watch(() => props.operationsPayload, parsePayload, { immediate: true })

const canSave = computed(() => {
  return reason.value.trim().length > 0 && !props.saving && !hasJsonFieldErrors.value
})

function onSave() {
  if (!canSave.value) return

  let revisedPayload: string
  if (parseError.value) {
    revisedPayload = fields.value[0]?.value ?? ''
  } else {
    const obj: Record<string, unknown> = {}
    for (const field of fields.value) {
      if (field.valueKind === 'string') {
        obj[field.key] = field.value
        continue
      }

      try {
        obj[field.key] = JSON.parse(field.value) as unknown
      } catch {
        return
      }
    }
    revisedPayload = JSON.stringify(obj)
  }

  emit('save', { revisedPayload, reason: reason.value.trim() })
}
</script>

<template>
  <div
    ref="rootRef"
    class="revision-editor card"
    role="region"
    :aria-label="$t('review.revisionEditor.regionLabel')"
    data-testid="revision-editor"
  >
    <div class="revision-editor__header">
      <PaperTagstamp tone="ember">{{ $t('review.revisionEditor.stamp') }}</PaperTagstamp>
    </div>

    <div class="revision-editor__fields">
      <div v-for="field in fields" :key="field.key" class="revision-editor__field">
        <label :for="`revision-field-${field.key}`" class="revision-editor__label">{{ field.key }}</label>
        <textarea
          :id="`revision-field-${field.key}`"
          v-model="field.value"
          class="revision-editor__input"
          rows="2"
          :data-testid="`revision-field-${field.key}`"
        />
        <p
          v-if="jsonFieldErrors[field.key]"
          class="revision-editor__error"
          :data-testid="`revision-field-${field.key}-error`"
        >
          {{ jsonFieldErrors[field.key] }}
        </p>
      </div>
    </div>

    <div class="revision-editor__reason">
      <label for="revision-reason" class="revision-editor__label">{{
        $t('review.revisionEditor.reasonLabel')
      }}</label>
      <input
        id="revision-reason"
        v-model="reason"
        type="text"
        class="revision-editor__reason-input"
        :placeholder="$t('review.revisionEditor.reasonPlaceholder')"
        data-testid="revision-reason"
      />
    </div>

    <div class="revision-editor__actions">
      <PaperHLBtn
        :label="$t('review.revisionEditor.cancel')"
        :disabled="saving"
        data-testid="revision-cancel"
        @click="emit('cancel')"
      />
      <PaperHLBtn
        :label="$t('review.revisionEditor.save')"
        variant="ember"
        :disabled="!canSave"
        data-testid="revision-save"
        @click="onSave"
      />
    </div>
  </div>
</template>

<style scoped>
.revision-editor {
  margin-top: 16px;
  padding: 16px;
}
.revision-editor__header {
  margin-bottom: 12px;
}
.revision-editor__fields {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.revision-editor__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.revision-editor__label {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-2);
}
.revision-editor__input {
  font-family: var(--mono);
  font-size: 13px;
  padding: 8px;
  border: 1px solid var(--line);
  border-radius: 4px;
  background: var(--paper);
  color: var(--text);
  resize: vertical;
}
.revision-editor__error {
  margin: 0;
  font-size: 12px;
  color: var(--td-color-error);
}
.revision-editor__reason {
  margin-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.revision-editor__reason-input {
  font-size: 13px;
  padding: 8px;
  border: 1px solid var(--line);
  border-radius: 4px;
  background: var(--paper);
  color: var(--text);
}
.revision-editor__actions {
  margin-top: 12px;
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}
</style>
