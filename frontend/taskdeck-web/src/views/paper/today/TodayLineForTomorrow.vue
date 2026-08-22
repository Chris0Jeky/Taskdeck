<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * TodayLineForTomorrow — italic serif textarea with debounced persistence.
 * Paper Today supplies the shipped per-day backend save; localStorage remains
 * an opt-in fallback for standalone component consumers.
 *
 * Persistence is keyed by `storageKey`, defaulting to `td.paper.line-for-tomorrow`.
 * `debounceMs` is exposed so tests can drop the timer without forcing real
 * 500ms waits.
 *
 * Lifecycle copy (issue 1939): the note is saved under, and read back for, the
 * SAME date — no day shift happens anywhere in the chain (issue 1640). The meta
 * therefore describes that, and must not promise a tomorrow hand-off. If 1640
 * decides in favour of the shift, this copy moves with it.
 */
const props = withDefaults(
  defineProps<{
    initial?: string
    storageKey?: string
    debounceMs?: number
    useStoredDraft?: boolean
    saveDate?: string
    save?: (value: string, saveDate?: string) => void | Promise<void>
  }>(),
  {
    initial: '',
    storageKey: 'td.paper.line-for-tomorrow',
    debounceMs: 500,
    useStoredDraft: true,
    saveDate: undefined,
    save: undefined,
  },
)

const emit = defineEmits<{
  (event: 'save', value: string): void
}>()

function readStored(): string {
  if (!props.useStoredDraft) return props.initial
  if (typeof window === 'undefined') return props.initial
  try {
    const raw = window.localStorage.getItem(props.storageKey)
    if (typeof raw === 'string') return raw
  } catch {
    // localStorage may throw in private mode
  }
  return props.initial
}

const { t } = useI18n()

const text = ref<string>(readStored())
const status = ref<'idle' | 'saving' | 'saved' | 'error'>('saved')
const input = ref<HTMLTextAreaElement | null>(null)

/**
 * Focus the field. Exposed so the cover's "Write a note" button can name its
 * own destination by moving the caret here instead of describing it in a toast.
 */
function focus() {
  const el = input.value
  if (!el) return
  el.focus()
  // happy-dom and older browsers do not implement scrollIntoView.
  if (typeof el.scrollIntoView === 'function') {
    el.scrollIntoView({ block: 'center' })
  }
}

defineExpose({ focus })

let timer: ReturnType<typeof setTimeout> | null = null
let suppressNextSave = false
let pendingSaveDate: string | undefined
let localEditPending = false
let flushGeneration = 0
let lastStorageKey = props.storageKey
let lastUseStoredDraft = props.useStoredDraft

async function flush() {
  const generation = ++flushGeneration
  if (props.useStoredDraft) {
    if (typeof window === 'undefined') return
    try {
      window.localStorage.setItem(props.storageKey, text.value)
    } catch {
      if (generation === flushGeneration) status.value = 'error'
      return
    }
  }
  try {
    if (props.save) {
      await props.save(text.value, pendingSaveDate)
    }
    if (generation !== flushGeneration) return
    localEditPending = false
    status.value = 'saved'
    emit('save', text.value)
  } catch {
    if (generation === flushGeneration) status.value = 'error'
  }
}

watch(text, () => {
  if (suppressNextSave) {
    suppressNextSave = false
    return
  }
  status.value = 'saving'
  localEditPending = true
  flushGeneration += 1
  pendingSaveDate = props.saveDate
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => {
    timer = null
    void flush()
  }, props.debounceMs)
})

watch(
  () => [props.storageKey, props.initial, props.useStoredDraft] as const,
  () => {
    const storageScopeChanged =
      props.storageKey !== lastStorageKey || props.useStoredDraft !== lastUseStoredDraft
    lastStorageKey = props.storageKey
    lastUseStoredDraft = props.useStoredDraft

    if (!props.useStoredDraft && !storageScopeChanged && localEditPending) {
      return
    }

    if (timer) {
      clearTimeout(timer)
      timer = null
    }
    const nextText = readStored()
    suppressNextSave = nextText !== text.value
    text.value = nextText
    localEditPending = false
    status.value = 'saved'
  },
)

onBeforeUnmount(() => {
  if (timer) {
    clearTimeout(timer)
    // Flush pending writes before the component goes away — otherwise
    // a quick remount loses the in-flight edit.
    void flush()
  }
})
</script>

<template>
  <div class="today-line" data-section="line-for-tomorrow">
    <textarea
      ref="input"
      v-model="text"
      class="today-line__input"
      data-testid="line-for-tomorrow-input"
      :aria-label="'A line for tomorrow'"
      rows="3"
    />
    <div class="tk-meta today-line__meta">
      <span data-testid="line-for-tomorrow-status">
        <template v-if="status === 'saving'">Saving…</template>
        <template v-else-if="status === 'error'">Save unavailable</template>
        <template v-else>Saved · auto</template>
      </span>
      <span data-testid="line-for-tomorrow-lifecycle">{{ t('today.note.meta') }}</span>
    </div>
  </div>
</template>

<style scoped>
.today-line__input {
  width: 100%;
  margin-top: 8px;
  min-height: 80px;
  border: 1px solid var(--line);
  border-radius: 2px;
  padding: 10px;
  background: var(--paper-card);
  font-family: var(--serif);
  font-size: 14px;
  line-height: 1.5;
  color: var(--ink-deep);
  font-style: italic;
  resize: vertical;
}
.today-line__input:focus {
  outline: none;
  border-color: var(--ember);
}
.today-line__meta {
  margin-top: 6px;
  font-size: 10.5px;
  display: flex;
  justify-content: space-between;
}
</style>
