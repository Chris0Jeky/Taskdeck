<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
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
}

const fields = ref<EditableField[]>([])
const reason = ref('')
const parseError = ref(false)

onMounted(() => {
  try {
    const parsed = JSON.parse(props.operationsPayload) as Record<string, unknown>
    fields.value = Object.entries(parsed).map(([key, value]) => ({
      key,
      value: typeof value === 'string' ? value : JSON.stringify(value),
    }))
    parseError.value = false
  } catch {
    fields.value = [{ key: 'payload', value: props.operationsPayload }]
    parseError.value = true
  }
})

const canSave = computed(() => {
  return reason.value.trim().length > 0 && !props.saving
})

function onSave() {
  if (!canSave.value) return

  let revisedPayload: string
  if (parseError.value) {
    revisedPayload = fields.value[0]?.value ?? ''
  } else {
    const obj: Record<string, unknown> = {}
    for (const field of fields.value) {
      try {
        obj[field.key] = JSON.parse(field.value) as unknown
      } catch {
        obj[field.key] = field.value
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
      <PaperTagstamp tone="ember">EDIT BEFORE APPROVE</PaperTagstamp>
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
      </div>
    </div>

    <div class="revision-editor__reason">
      <label for="revision-reason" class="revision-editor__label">Reason for edit</label>
      <input
        id="revision-reason"
        v-model="reason"
        type="text"
        class="revision-editor__reason-input"
        placeholder="Why are you editing this proposal?"
        data-testid="revision-reason"
      />
    </div>

    <div class="revision-editor__actions">
      <PaperHLBtn
        label="Cancel"
        :disabled="saving"
        data-testid="revision-cancel"
        @click="emit('cancel')"
      />
      <PaperHLBtn
        label="Save revision"
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
