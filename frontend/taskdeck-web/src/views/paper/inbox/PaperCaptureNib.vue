<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'
import { formatShortcut } from '../../../utils/keyboardShortcuts'

/**
 * PaperCaptureNib — variant A of the Paper Inbox capture surface.
 *
 * Focus-mode capture: a single, large italic-serif input centered in the
 * viewport.  Pressing Enter dispatches a `submit` event; Shift+Enter inserts
 * a newline (the surrounding wrapper grows to fit).
 *
 * After a successful submit the parent flips a flag that drives the static
 * "ember placeholder" state — a hairline-bordered card with an ember halo
 * that stands in for the future ink-bleed motion.
 *
 * TODO(PAPER-07/PAPER-10): wire ink bleed once #1010 merges.  For now we
 * render a static ember placeholder at the same position so the intended
 * structure is reviewable.
 */
const props = defineProps<{
  /**
   * When true, suppress the textarea and render the static ember placeholder
   * for ~1.4s.  The parent owns the timer so it can also reset state.
   */
  bleeding?: boolean
  submitting?: boolean
  /**
   * A capture submitted from this nib failed and its inspectable receipt is
   * showing (GH-1938). Marks the input invalid and points assistive tech at
   * the receipt so the failure is announced against the field, not just as a
   * toast that expires.
   */
  invalid?: boolean
  /** DOM id of the failure receipt to associate via `aria-describedby`. */
  errorId?: string | null
}>()

const emit = defineEmits<{
  (event: 'submit', text: string): void
}>()

const text = ref('')
const inputRef = ref<HTMLTextAreaElement | null>(null)

function onKeydown(event: KeyboardEvent) {
  if (event.isComposing) {
    return
  }
  // Shift+Enter — let the textarea insert a newline (default behaviour).
  if (event.key === 'Enter' && event.shiftKey) {
    return
  }
  if (event.key === 'Enter') {
    event.preventDefault()
    submit()
  }
}

function submit() {
  if (props.submitting) return
  const value = text.value.trim()
  if (!value) return
  emit('submit', value)
}

function resetDraft() {
  text.value = ''
}

onMounted(async () => {
  await nextTick()
  inputRef.value?.focus()
})

defineExpose({ focus: () => inputRef.value?.focus(), resetDraft })
</script>

<template>
  <div class="paper-nib">
    <div class="paper-nib__eyebrow tk-eyebrow">Quick capture · {{ formatShortcut('mod+;') }}</div>

    <div v-if="bleeding" class="paper-nib__bleed" data-testid="paper-nib-bleed">
      <!-- TODO(PAPER-07/PAPER-10): wire ink bleed once #1010 merges. -->
      <span class="paper-nib__bleed-ember" aria-hidden="true" />
      <span class="paper-nib__bleed-label">Captured</span>
    </div>

    <textarea
      v-else
      ref="inputRef"
      v-model="text"
      class="paper-nib__input"
      rows="1"
      aria-label="Quick capture input"
      placeholder="What's on your mind, quickly?"
      :disabled="submitting"
      :aria-invalid="invalid ? 'true' : undefined"
      :aria-describedby="errorId ?? undefined"
      @keydown="onKeydown"
    />

    <div class="paper-nib__rule" aria-hidden="true" />
  </div>
</template>

<style scoped>
.paper-nib {
  position: relative;
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: 4px;
  padding: 22px 28px 28px;
  box-shadow: var(--shadow-lift, 0 1px 0 var(--line));
}

.paper-nib__eyebrow {
  margin-bottom: 12px;
}

.paper-nib__input {
  display: block;
  width: 100%;
  max-width: 80ch;
  margin: 0 auto;
  padding: 8px 0;
  border: 0;
  background: transparent;
  resize: none;
  outline: none;

  font-family: var(--serif);
  font-style: italic;
  font-weight: 400;
  font-size: clamp(44px, 5vw, 64px);
  line-height: 1.18;
  letter-spacing: -0.005em;
  color: var(--ink-deep);
  caret-color: var(--ember);
  word-wrap: break-word;
  overflow-wrap: break-word;
}

.paper-nib__input::placeholder {
  color: var(--whisper);
  font-style: italic;
}

.paper-nib__rule {
  margin-top: 16px;
  height: 1px;
  background: var(--line);
}

.paper-nib__bleed {
  display: flex;
  align-items: center;
  gap: 12px;
  min-height: 80px;
  padding: 10px 0;
  font-family: var(--serif);
  font-style: italic;
  font-size: 28px;
  color: var(--ember-ink, var(--ember));
}

.paper-nib__bleed-ember {
  display: inline-block;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  background: var(--ember);
  box-shadow: 0 0 0 4px var(--ember-bloom, transparent);
}

.paper-nib__bleed-label {
  font-family: var(--mono);
  font-style: normal;
  font-size: 11px;
  letter-spacing: 0.22em;
  text-transform: uppercase;
  color: var(--ember);
}
</style>
