<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'

/**
 * TodayLineForTomorrow — italic serif textarea, autosaved with debounce
 * to localStorage (until the backend ships per-day storage; see #1018).
 *
 * Persistence is keyed by `storageKey`, defaulting to `td.paper.line-for-tomorrow`.
 * `debounceMs` is exposed so tests can drop the timer without forcing real
 * 500ms waits.
 */
const props = withDefaults(
  defineProps<{
    initial?: string
    storageKey?: string
    debounceMs?: number
  }>(),
  {
    initial: '',
    storageKey: 'td.paper.line-for-tomorrow',
    debounceMs: 500,
  },
)

const emit = defineEmits<{
  (event: 'save', value: string): void
}>()

function readStored(): string {
  if (typeof window === 'undefined') return props.initial
  try {
    const raw = window.localStorage.getItem(props.storageKey)
    if (typeof raw === 'string') return raw
  } catch {
    // localStorage may throw in private mode
  }
  return props.initial
}

const text = ref<string>(readStored())
const status = ref<'idle' | 'saving' | 'saved'>('saved')

let timer: ReturnType<typeof setTimeout> | null = null

function flush() {
  if (typeof window === 'undefined') return
  try {
    window.localStorage.setItem(props.storageKey, text.value)
  } catch {
    // ignore quota / private mode failures
  }
  status.value = 'saved'
  emit('save', text.value)
}

watch(text, () => {
  status.value = 'saving'
  if (timer) clearTimeout(timer)
  timer = setTimeout(flush, props.debounceMs)
})

onBeforeUnmount(() => {
  if (timer) {
    clearTimeout(timer)
    // Flush pending writes before the component goes away — otherwise
    // a quick remount loses the in-flight edit.
    flush()
  }
})
</script>

<template>
  <div class="today-line" data-section="line-for-tomorrow">
    <textarea
      v-model="text"
      class="today-line__input"
      data-testid="line-for-tomorrow-input"
      :aria-label="'A line for tomorrow'"
      rows="3"
    />
    <div class="tk-meta today-line__meta">
      <span data-testid="line-for-tomorrow-status">
        <template v-if="status === 'saving'">Saving…</template>
        <template v-else>Saved · auto</template>
      </span>
      <span>shows on tomorrow's open</span>
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
