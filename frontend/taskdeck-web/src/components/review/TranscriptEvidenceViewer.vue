<script setup lang="ts">
import { computed, nextTick, ref, watch, onScopeDispose } from 'vue'
import { useI18n } from 'vue-i18n'
import { transcriptsApi, type TranscriptDto } from '../../api/transcriptsApi'

/**
 * Opens one stored transcript and highlights the character span an evidence link
 * points at, scrolled into view. Read-only: nothing here mutates a proposal or a
 * board.
 *
 * Render bound (NOT a code change — measurement task, #1837 item 3): the body below renders the
 * whole transcript as three text nodes in one `<pre>`, with no truncation or virtualization, up
 * to the documented cap of 200,000 characters (`Transcript.MaxTextLength`, mirrored by
 * `CaptureModal`'s `MAX_TRANSCRIPT_LENGTH`). That is fine on modern engines by inspection but has
 * never been measured. Measure once with a max-size transcript during dogfooding before adding
 * any virtualization here; issue #1837 item 3 tracks the measurement.
 *
 * Span arithmetic note: the backend records offsets as .NET `char` indices into the
 * transcript's LF-normalized text. .NET `char` and JavaScript string indices are both
 * UTF-16 code units, so `String.prototype.slice` reproduces the same substring for
 * multi-byte text — provided we never index by code point (no spread, no `Array.from`).
 */
const props = defineProps<{
  transcriptId: string
  spanStart: number
  spanEnd: number
  /** Quote or field label shown above the transcript body. */
  label?: string
}>()

const emit = defineEmits<{ close: [] }>()

const { t } = useI18n()

const transcript = ref<TranscriptDto | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)
const highlightRef = ref<HTMLElement | null>(null)

let requestGeneration = 0
let abortController: AbortController | null = null

/**
 * Nudges an offset off the trailing half of a surrogate pair so a highlight can
 * never split an astral-plane character into replacement glyphs.
 */
function alignToCodePointStart(text: string, index: number): number {
  if (index <= 0 || index >= text.length) return index
  const code = text.charCodeAt(index)
  const previous = text.charCodeAt(index - 1)
  const isLowSurrogate = code >= 0xdc00 && code <= 0xdfff
  const previousIsHighSurrogate = previous >= 0xd800 && previous <= 0xdbff
  return isLowSurrogate && previousIsHighSurrogate ? index - 1 : index
}

const bounds = computed(() => {
  const text = transcript.value?.text ?? ''
  const start = alignToCodePointStart(text, Math.min(Math.max(props.spanStart, 0), text.length))
  const rawEnd = Math.min(Math.max(props.spanEnd, start), text.length)
  const end = alignToCodePointStart(text, rawEnd)
  return { start, end: Math.max(end, start) }
})

const before = computed(() => (transcript.value?.text ?? '').slice(0, bounds.value.start))
const highlighted = computed(() =>
  (transcript.value?.text ?? '').slice(bounds.value.start, bounds.value.end),
)
const after = computed(() => (transcript.value?.text ?? '').slice(bounds.value.end))
const hasHighlight = computed(() => bounds.value.end > bounds.value.start)

/** Speaker attribution for the line the span starts on, when the transcript carries segments. */
const speakerLabel = computed(() => {
  const loaded = transcript.value
  if (!loaded || !hasHighlight.value) return null
  const line = before.value.split('\n').length - 1
  const segment = loaded.segments.find((s) => line >= s.startLine && line <= s.endLine)
  return segment?.speaker ?? null
})

async function load() {
  const generation = ++requestGeneration
  abortController?.abort()
  const controller = new AbortController()
  abortController = controller

  loading.value = true
  errorMessage.value = null
  transcript.value = null

  try {
    const loaded = await transcriptsApi.getById(props.transcriptId, { signal: controller.signal })
    if (generation !== requestGeneration) return
    transcript.value = loaded
  } catch (error) {
    if (generation !== requestGeneration) return
    if (controller.signal.aborted) return
    errorMessage.value = describeError(error)
  } finally {
    if (generation === requestGeneration) loading.value = false
  }

  if (generation !== requestGeneration || !transcript.value) return
  await nextTick()
  highlightRef.value?.scrollIntoView?.({ block: 'center' })
}

function describeError(error: unknown): string {
  const status = (error as { response?: { status?: number } })?.response?.status
  if (status === 404) return t('review.transcript.error.notFound')
  if (status === 401 || status === 403) return t('review.transcript.error.unauthorized')
  return t('review.transcript.error.generic')
}

watch(() => props.transcriptId, load, { immediate: true })

onScopeDispose(() => {
  requestGeneration++
  abortController?.abort()
  abortController = null
})
</script>

<template>
  <section class="transcript-evidence" data-testid="transcript-evidence-viewer">
    <header class="transcript-evidence__header">
      <h4 class="transcript-evidence__title">{{ $t('review.transcript.title') }}</h4>
      <button
        type="button"
        class="transcript-evidence__close"
        data-testid="transcript-evidence-close"
        @click="emit('close')"
      >
        {{ $t('review.transcript.close') }}
      </button>
    </header>

    <p v-if="label" class="transcript-evidence__label">{{ label }}</p>
    <p v-if="speakerLabel" class="transcript-evidence__speaker">
      {{ $t('review.transcript.speaker', { name: speakerLabel }) }}
    </p>

    <p v-if="loading" class="transcript-evidence__status" data-testid="transcript-evidence-loading">
      {{ $t('review.transcript.loading') }}
    </p>
    <p
      v-else-if="errorMessage"
      class="transcript-evidence__status transcript-evidence__status--error"
      role="alert"
      data-testid="transcript-evidence-error"
    >
      {{ errorMessage }}
    </p>
    <template v-else-if="transcript">
      <p
        v-if="!hasHighlight"
        class="transcript-evidence__status"
        data-testid="transcript-evidence-unresolved"
      >
        {{ $t('review.transcript.unresolved') }}
      </p>
      <pre class="transcript-evidence__body" data-testid="transcript-evidence-body"><span>{{
        before
      }}</span><mark
        v-if="hasHighlight"
        ref="highlightRef"
        class="transcript-evidence__mark"
        data-testid="transcript-evidence-highlight"
      >{{ highlighted }}</mark><span>{{ after }}</span></pre>
    </template>
  </section>
</template>

<style scoped>
.transcript-evidence {
  margin-top: 12px;
  border: 1px solid var(--td-border-default, #ddd);
  border-radius: 8px;
  padding: 12px;
  /* Fallback must equal what `--td-surface-sunken` resolves to at `:root`
     (the Obsidian container-lowest hex) — guarded by
     tests/legacy-surface-depth-tokens.spec.ts (#1814). Paper re-declares the
     alias on its own scope, so the light surface comes from the bridge, not
     from this fallback. */
  background: var(--td-surface-sunken, #0e0e0e);
}

.transcript-evidence__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.transcript-evidence__title {
  margin: 0;
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--td-text-secondary, #666);
}

.transcript-evidence__close {
  border: 1px solid var(--td-border-default, #ddd);
  background: var(--td-surface-container, #fff);
  color: var(--td-text-primary, #333);
  border-radius: 6px;
  font-size: 12px;
  padding: 4px 10px;
  cursor: pointer;
}

.transcript-evidence__label,
.transcript-evidence__speaker {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--td-text-secondary, #666);
}

.transcript-evidence__status {
  margin: 10px 0 0;
  font-size: 12px;
  color: var(--td-text-secondary, #666);
}

.transcript-evidence__status--error {
  color: var(--td-error, #c00);
}

.transcript-evidence__body {
  margin: 10px 0 0;
  max-height: 260px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: var(--td-font-mono, monospace);
  font-size: 12px;
  line-height: 1.55;
  color: var(--td-text-primary, #333);
}

.transcript-evidence__mark {
  background: var(--td-highlight, #fde68a);
  color: inherit;
  border-radius: 2px;
}
</style>
