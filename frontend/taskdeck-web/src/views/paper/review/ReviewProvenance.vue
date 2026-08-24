<script setup lang="ts">
import { ref, computed } from 'vue'
import type { ProvenanceRow, ProvenanceWeight } from '../../../composables/usePaperReviewSelectors'
import ProvenanceDrawer from '../../../components/review/ProvenanceDrawer.vue'
import type { ProvenanceMetadata, EvidenceLink } from '../../../components/review/ProvenanceDrawer.vue'
import { classifyProvenanceActor, formatProvenanceActorLabel } from './provenanceActor'

const props = withDefaults(defineProps<{
  rows: ProvenanceRow[]
  metadata?: ProvenanceMetadata | null
  evidenceLinks?: EvidenceLink[]
  proposalId: string
  readOnly?: boolean
}>(), { readOnly: false })

const emit = defineEmits<{
  report: [proposalId: string]
}>()

const empty = computed(() => props.rows.length === 0)
const drawerOpen = ref(false)

/**
 * The footnote sentence, chosen by the provenance the backend actually recorded for this
 * proposal rather than by a constant (GH-1963).
 *
 * Returns null — and the footnote sentence is then not rendered at all — whenever the
 * recorded provenance is absent or incoherent. This surface exists to tell the user what
 * read their text and who saw it, so an unsupported claim here is worse than silence.
 *
 * Resolved as a key + params so the template's `$t` re-renders it on a language switch
 * (ADR-0054); `label` is backend wire text and is interpolated verbatim, never translated.
 */
const footnote = computed<{ key: string; params: Record<string, string> } | null>(() => {
  const actor = classifyProvenanceActor(props.metadata)
  if (actor.kind === 'unknown') return null
  return {
    key: `review.provenance.footnote.${actor.kind}`,
    params: { label: formatProvenanceActorLabel(actor) },
  }
})

function tone(weight: ProvenanceWeight): string {
  switch (weight) {
    case 'primary':
      return 'var(--ink)'
    case 'excluded':
      return 'var(--faint)'
    case 'inferred':
      return 'var(--ember)'
    case 'contextual':
    default:
      return 'var(--ink-2, var(--ink))'
  }
}
</script>

<template>
  <section class="paper-review-prov">
    <header class="paper-review-prov__header">
      <span class="tk-serial paper-review-prov__serial">§ II</span>
      <h3 class="tk-h3 paper-review-prov__title">{{ $t('review.provenance.title') }}</h3>
      <span class="tk-meta paper-review-prov__sub">
        {{ $t('review.provenance.sub') }}
      </span>
    </header>
    <div class="card paper-review-prov__card">
      <div v-if="empty" class="paper-review-prov__empty tk-meta">
        {{ $t('review.provenance.empty') }}
      </div>
      <div
        v-for="row in rows"
        :key="`${row.weight}:${row.key}`"
        class="paper-review-prov__row"
      >
        <span class="paper-review-prov__icon" :style="{ color: tone(row.weight) }">{{ row.icon }}</span>
        <span class="paper-review-prov__key" :style="{ color: tone(row.weight) }">{{ row.key }}</span>
        <span class="paper-review-prov__value">{{ row.value }}</span>
      </div>
    </div>
    <p class="tk-meta paper-review-prov__footnote">
      <span v-if="footnote" data-testid="paper-review-provenance-footnote">{{
        $t(footnote.key, footnote.params)
      }}</span>
      <a href="#" class="paper-review-prov__more" @click.prevent="drawerOpen = true">{{
        $t('review.provenance.viewAll')
      }}</a>
    </p>

    <ProvenanceDrawer
      :open="drawerOpen"
      :rows="rows"
      :metadata="metadata ?? null"
      :evidence-links="evidenceLinks ?? []"
      :proposal-id="proposalId"
      :read-only="props.readOnly"
      @close="drawerOpen = false"
      @report="emit('report', $event)"
    />
  </section>
</template>

<style scoped>
.paper-review-prov {
  margin-top: 28px;
}
.paper-review-prov__header {
  display: flex;
  align-items: baseline;
  gap: 14px;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-review-prov__serial {
  color: var(--faint);
}
.paper-review-prov__title {
  margin: 0;
}
.paper-review-prov__sub {
  margin-left: auto;
}
.paper-review-prov__card {
  padding: 0;
  overflow: hidden;
}
.paper-review-prov__empty {
  padding: 16px;
}
.paper-review-prov__row {
  display: grid;
  grid-template-columns: 32px 200px 1fr;
  gap: 12px;
  padding: 11px 16px;
  border-bottom: 1px solid var(--line-soft);
  align-items: flex-start;
}
.paper-review-prov__row:last-child {
  border-bottom: 0;
}
.paper-review-prov__icon {
  font-size: 14px;
  line-height: 1.3;
}
.paper-review-prov__key {
  font-family: var(--serif);
  font-style: italic;
  font-size: 13px;
}
.paper-review-prov__value {
  font-size: 12.5px;
  color: var(--ink-2, var(--ink));
}
.paper-review-prov__footnote {
  margin-top: 8px;
  font-size: 11px;
}
.paper-review-prov__footnote b {
  color: var(--ink);
  font-weight: 500;
}
.paper-review-prov__more {
  color: var(--ember);
  border-bottom: 1px solid var(--ember);
  text-decoration: none;
  margin-left: 4px;
}
</style>
