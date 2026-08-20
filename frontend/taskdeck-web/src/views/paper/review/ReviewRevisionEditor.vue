<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'

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

const fields = ref<EditableField[]>([])
const reason = ref('')
const parseError = ref(false)

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
  <div class="revision-editor card" data-testid="revision-editor">
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
