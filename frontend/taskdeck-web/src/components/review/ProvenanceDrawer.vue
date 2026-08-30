<script setup lang="ts">
import { ref, computed, watch, nextTick, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import TranscriptEvidenceViewer from './TranscriptEvidenceViewer.vue'
import {
  TRANSCRIPT_EVIDENCE_SOURCE_TYPE,
  type EvidenceLink as ProvenanceEvidenceLink,
  type ProvenanceRow,
  type ProvenanceWeight,
} from '../../composables/usePaperReviewSelectors'

export interface ProvenanceMetadata {
  model: string
  provider: string
  confidence: number
  latencyMs: number
  promptVersion: string | null
}

/**
 * Re-exported from its canonical home in `usePaperReviewSelectors` so existing
 * importers keep resolving `EvidenceLink` from this component.
 */
export type EvidenceLink = ProvenanceEvidenceLink

/**
 * Provenance drawer for the Paper deep-Review surface: source rows, model metadata, and the
 * evidence links behind a proposal, with an optional inline transcript viewer.
 *
 * CALLER CONTRACT — `evidenceLinks` must be a STABLE reference. An open transcript viewer is
 * reset whenever the `evidenceLinks` REFERENCE changes (not its contents), because a different
 * proposal's links occupy the same indices. A caller that mints a fresh array on every render
 * therefore collapses the viewer on every render. Pass a `computed`/`ref` that only changes
 * when the underlying data does — as `usePaperReviewSelectors().evidenceLinks` does today
 * (#1837 item 4).
 */
const props = withDefaults(defineProps<{
  open: boolean
  rows: ProvenanceRow[]
  metadata: ProvenanceMetadata | null
  /** Stable reference required — see the caller contract above. */
  evidenceLinks: EvidenceLink[]
  proposalId: string
  readOnly?: boolean
}>(), { readOnly: false })

const emit = defineEmits<{
  close: []
  report: [proposalId: string]
}>()

const { t } = useI18n()

const drawerRef = ref<HTMLElement | null>(null)
const copied = ref(false)
let unregisterEscape: (() => void) | null = null
let previouslyFocusedElement: HTMLElement | null = null

function trapFocus(event: KeyboardEvent) {
  if (event.key !== 'Tab' || !drawerRef.value) return

  const focusableSelector =
    'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'
  const focusableElements = Array.from(
    drawerRef.value.querySelectorAll<HTMLElement>(focusableSelector),
  )

  if (focusableElements.length === 0) {
    event.preventDefault()
    return
  }

  const first = focusableElements[0]!
  const last = focusableElements[focusableElements.length - 1]!

  if (event.shiftKey) {
    if (document.activeElement === first) {
      event.preventDefault()
      last.focus()
    }
  } else {
    if (document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }
}

const groupedSources = computed(() => {
  const groups: Record<ProvenanceWeight, ProvenanceRow[]> = {
    primary: [],
    contextual: [],
    inferred: [],
    excluded: [],
  }
  for (const row of props.rows) {
    groups[row.weight].push(row)
  }
  return groups
})

const provenanceJson = computed(() => {
  return JSON.stringify(
    {
      sources: props.rows,
      metadata: props.metadata,
      evidenceLinks: props.evidenceLinks,
    },
    null,
    2,
  )
})

/**
 * A transcript evidence link is deep-linkable only when it names a transcript, carries a
 * resolved character span, and the server marked it viewable for THIS caller; anything else
 * renders as plain metadata.
 *
 * `viewable` is server-computed from claims because the client cannot tell whether the caller
 * owns the transcript: provenance is board-authorized while `GET /api/transcripts/{id}` is
 * owner-only. Without this gate a board collaborator gets a button that can only land on
 * "no longer available" (#1837 item 1). An absent flag is treated as not viewable.
 */
function transcriptTargetOf(link: EvidenceLink): { transcriptId: string; span: [number, number] } | null {
  if (link.sourceType !== TRANSCRIPT_EVIDENCE_SOURCE_TYPE) return null
  if (!link.sourceId || !link.span) return null
  if (link.viewable !== true) return null
  return { transcriptId: link.sourceId, span: link.span }
}

const openEvidenceIndex = ref<number | null>(null)

const openEvidence = computed(() => {
  const index = openEvidenceIndex.value
  if (index === null) return null
  const link = props.evidenceLinks[index]
  if (!link) return null
  const target = transcriptTargetOf(link)
  return target === null ? null : { link, ...target }
})

function toggleTranscript(index: number) {
  openEvidenceIndex.value = openEvidenceIndex.value === index ? null : index
}

// A different proposal's links occupy the same indices; close the viewer so it can
// never show the previous proposal's transcript against the new list.
//
// This watcher fires on any REFERENCE change of the prop, contents identical or not — the
// index-based viewer state has no cheaper way to know the list is a different one. Callers
// must therefore pass a stable ref (see the caller contract on `defineProps`); a caller that
// rebuilds the array each render would close the viewer under the user (#1837 item 4).
watch(
  () => props.evidenceLinks,
  () => {
    openEvidenceIndex.value = null
  },
)

const copyError = ref(false)

async function copyJson() {
  try {
    await navigator.clipboard.writeText(provenanceJson.value)
    copied.value = true
    setTimeout(() => {
      copied.value = false
    }, 2000)
  } catch {
    copyError.value = true
    setTimeout(() => {
      copyError.value = false
    }, 3000)
  }
}

function reportBadSuggestion() {
  emit('report', props.proposalId)
}

function weightLabel(weight: ProvenanceWeight): string {
  switch (weight) {
    case 'primary':
      return t('review.provenanceDrawer.weight.primary')
    case 'contextual':
      return t('review.provenanceDrawer.weight.contextual')
    case 'inferred':
      return t('review.provenanceDrawer.weight.inferred')
    case 'excluded':
      return t('review.provenanceDrawer.weight.excluded')
  }
}

function weightColor(weight: ProvenanceWeight): string {
  switch (weight) {
    case 'primary':
      return 'var(--td-text-primary, var(--ink))'
    case 'contextual':
      return 'var(--td-text-secondary, var(--ink-2))'
    case 'inferred':
      return 'var(--td-warning, var(--ember))'
    case 'excluded':
      return 'var(--td-text-disabled, var(--faint))'
  }
}

watch(
  () => props.open,
  async (isOpen) => {
    if (isOpen) {
      previouslyFocusedElement = document.activeElement as HTMLElement | null
      unregisterEscape = registerEscapeHandler(() => emit('close'))
      await nextTick()
      drawerRef.value?.focus()
    } else {
      openEvidenceIndex.value = null
      unregisterEscape?.()
      unregisterEscape = null
      previouslyFocusedElement?.focus()
      previouslyFocusedElement = null
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  unregisterEscape?.()
  previouslyFocusedElement?.focus()
  previouslyFocusedElement = null
})
</script>

<template>
  <Teleport to="body">
    <Transition name="prov-drawer">
      <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -->
      <div
        v-if="open"
        class="prov-drawer-backdrop"
        @click.self="emit('close')"
        @keydown.escape="emit('close')"
      >
        <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -->
        <aside
          ref="drawerRef"
          class="prov-drawer"
          role="dialog"
          aria-modal="true"
          :aria-label="$t('review.provenanceDrawer.ariaLabel')"
          tabindex="-1"
          @keydown="trapFocus"
        >
          <header class="prov-drawer__header">
            <h2 class="prov-drawer__title">{{ $t('review.provenanceDrawer.title') }}</h2>
            <button
              class="prov-drawer__close"
              :aria-label="$t('review.provenanceDrawer.close')"
              @click="emit('close')"
            >
              &times;
            </button>
          </header>

          <div v-if="metadata" class="prov-drawer__meta">
            <div class="prov-drawer__meta-row">
              <span class="prov-drawer__meta-label">{{ $t('review.provenanceDrawer.meta.model') }}</span>
              <span class="prov-drawer__meta-value">{{ metadata.provider }}/{{ metadata.model }}</span>
            </div>
            <div class="prov-drawer__meta-row">
              <span class="prov-drawer__meta-label">{{
                $t('review.provenanceDrawer.meta.confidence')
              }}</span>
              <span class="prov-drawer__meta-value">{{
                $t('review.provenanceDrawer.meta.confidenceValue', {
                  value: (metadata.confidence * 100).toFixed(0),
                })
              }}</span>
            </div>
            <div class="prov-drawer__meta-row">
              <span class="prov-drawer__meta-label">{{
                $t('review.provenanceDrawer.meta.latency')
              }}</span>
              <span class="prov-drawer__meta-value">{{
                $t('review.provenanceDrawer.meta.latencyValue', { value: metadata.latencyMs })
              }}</span>
            </div>
            <div v-if="metadata.promptVersion" class="prov-drawer__meta-row">
              <span class="prov-drawer__meta-label">{{
                $t('review.provenanceDrawer.meta.promptVersion')
              }}</span>
              <span class="prov-drawer__meta-value">{{ metadata.promptVersion }}</span>
            </div>
          </div>

          <div class="prov-drawer__sources">
            <template v-for="weight in (['primary', 'contextual', 'inferred', 'excluded'] as const)" :key="weight">
              <section v-if="groupedSources[weight].length > 0" class="prov-drawer__group">
                <h3 class="prov-drawer__group-title" :style="{ color: weightColor(weight) }">
                  {{ weightLabel(weight) }}
                </h3>
                <div
                  v-for="row in groupedSources[weight]"
                  :key="`${row.weight}:${row.key}`"
                  class="prov-drawer__source-row"
                >
                  <span class="prov-drawer__source-icon" :style="{ color: weightColor(weight) }">{{ row.icon }}</span>
                  <span class="prov-drawer__source-key">{{ row.key }}</span>
                  <span class="prov-drawer__source-value">{{ row.value }}</span>
                </div>
              </section>
            </template>
          </div>

          <section v-if="evidenceLinks.length > 0" class="prov-drawer__evidence">
            <h3 class="prov-drawer__section-title">
              {{ $t('review.provenanceDrawer.evidenceTitle') }}
            </h3>
            <div
              v-for="(link, idx) in evidenceLinks"
              :key="idx"
              class="prov-drawer__evidence-row"
            >
              <span class="prov-drawer__evidence-source" :style="{ color: weightColor(link.weight) }">
                {{ link.sourceKey }}
              </span>
              <span v-if="link.span" class="prov-drawer__evidence-span">
                {{
                  $t('review.provenanceDrawer.evidenceSpan', {
                    start: link.span[0],
                    end: link.span[1],
                  })
                }}
              </span>
              <span class="prov-drawer__evidence-reason">{{ link.reason }}</span>
              <button
                v-if="transcriptTargetOf(link)"
                type="button"
                class="prov-drawer__evidence-open"
                :aria-expanded="openEvidenceIndex === idx"
                :data-testid="`provenance-view-in-transcript-${idx}`"
                @click="toggleTranscript(idx)"
              >
                {{
                  openEvidenceIndex === idx
                    ? $t('review.provenanceDrawer.hideTranscript')
                    : $t('review.provenanceDrawer.viewTranscript')
                }}
              </button>
              <TranscriptEvidenceViewer
                v-if="openEvidence && openEvidenceIndex === idx"
                class="prov-drawer__evidence-viewer"
                :transcript-id="openEvidence.transcriptId"
                :span-start="openEvidence.span[0]"
                :span-end="openEvidence.span[1]"
                :label="openEvidence.link.reason"
                @close="openEvidenceIndex = null"
              />
            </div>
          </section>

          <footer class="prov-drawer__footer">
            <button class="prov-drawer__action prov-drawer__action--copy" @click="copyJson">
              {{
                copyError
                  ? $t('review.provenanceDrawer.copyFailed')
                  : copied
                    ? $t('review.provenanceDrawer.copied')
                    : $t('review.provenanceDrawer.copyJson')
              }}
            </button>
            <button v-if="!props.readOnly" class="prov-drawer__action prov-drawer__action--report" @click="reportBadSuggestion">
              {{ $t('review.provenanceDrawer.report') }}
            </button>
          </footer>
        </aside>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.prov-drawer-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  z-index: 70;
  display: flex;
  justify-content: flex-end;
}

.prov-drawer {
  width: min(480px, 100%);
  height: 100%;
  background: var(--td-surface-container, #fff);
  border-left: 1px solid var(--td-border-default, #e5e5e5);
  box-shadow: var(--td-shadow-xl, -4px 0 24px rgba(0, 0, 0, 0.08));
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  padding: 24px;
}

.prov-drawer:focus {
  outline: none;
}

.prov-drawer__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 20px;
}

.prov-drawer__title {
  font-size: 18px;
  font-weight: 600;
  margin: 0;
  color: var(--td-text-primary, #111);
}

.prov-drawer__close {
  background: none;
  border: none;
  font-size: 24px;
  cursor: pointer;
  color: var(--td-text-secondary, #666);
  padding: 4px 8px;
  border-radius: 4px;
}

.prov-drawer__close:hover {
  background: var(--td-surface-hover, #f5f5f5);
}

.prov-drawer__meta {
  background: var(--td-surface-sunken, #0e0e0e);
  border-radius: 8px;
  padding: 12px 16px;
  margin-bottom: 20px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.prov-drawer__meta-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 13px;
}

.prov-drawer__meta-label {
  color: var(--td-text-secondary, #666);
}

.prov-drawer__meta-value {
  font-weight: 500;
  color: var(--td-text-primary, #111);
}

.prov-drawer__sources {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-bottom: 20px;
}

.prov-drawer__group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.prov-drawer__group-title {
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin: 0;
}

.prov-drawer__source-row {
  display: grid;
  grid-template-columns: 24px 1fr 1.5fr;
  gap: 8px;
  padding: 6px 0;
  font-size: 13px;
  align-items: flex-start;
}

.prov-drawer__source-icon {
  font-size: 13px;
}

.prov-drawer__source-key {
  font-style: italic;
  color: var(--td-text-secondary, #666);
}

.prov-drawer__source-value {
  color: var(--td-text-primary, #333);
}

.prov-drawer__evidence {
  margin-bottom: 20px;
}

.prov-drawer__section-title {
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin: 0 0 10px;
  color: var(--td-text-secondary, #666);
}

.prov-drawer__evidence-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding: 8px 0;
  border-bottom: 1px solid var(--td-border-ghost, #eee);
  font-size: 12px;
  align-items: baseline;
}

.prov-drawer__evidence-row:last-child {
  border-bottom: none;
}

.prov-drawer__evidence-source {
  font-weight: 500;
}

.prov-drawer__evidence-span {
  color: var(--td-text-disabled, #999);
  font-family: monospace;
  font-size: 11px;
}

.prov-drawer__evidence-reason {
  color: var(--td-text-secondary, #666);
  flex: 1;
}

.prov-drawer__evidence-open {
  border: 1px solid var(--td-border-default, #ddd);
  background: var(--td-surface-container, #fff);
  color: var(--td-text-primary, #333);
  border-radius: 6px;
  font-size: 11px;
  padding: 3px 9px;
  cursor: pointer;
  white-space: nowrap;
}

.prov-drawer__evidence-open:hover {
  background: var(--td-surface-hover, #f5f5f5);
}

.prov-drawer__evidence-viewer {
  flex-basis: 100%;
}

.prov-drawer__footer {
  display: flex;
  gap: 8px;
  padding-top: 16px;
  border-top: 1px solid var(--td-border-ghost, #eee);
  margin-top: auto;
}

.prov-drawer__action {
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  border: 1px solid var(--td-border-default, #ddd);
  background: var(--td-surface-container, #fff);
  color: var(--td-text-primary, #333);
  transition: background 150ms;
}

.prov-drawer__action:hover {
  background: var(--td-surface-hover, #f5f5f5);
}

.prov-drawer__action--report {
  color: var(--td-error, #c00);
  border-color: var(--td-error, #c00);
}

.prov-drawer__action--report:hover {
  background: rgba(200, 0, 0, 0.05);
}

/* ── Transition ── */
.prov-drawer-enter-active,
.prov-drawer-leave-active {
  transition: opacity 200ms ease;
}

.prov-drawer-enter-active .prov-drawer,
.prov-drawer-leave-active .prov-drawer {
  transition: transform 250ms cubic-bezier(0.4, 0, 0.2, 1);
}

.prov-drawer-enter-from,
.prov-drawer-leave-to {
  opacity: 0;
}

.prov-drawer-enter-from .prov-drawer {
  transform: translateX(100%);
}

.prov-drawer-leave-to .prov-drawer {
  transform: translateX(100%);
}

@media (max-width: 640px) {
  .prov-drawer {
    width: 100%;
  }
}
</style>
