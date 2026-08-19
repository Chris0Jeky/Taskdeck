<script setup lang="ts">
import { computed } from 'vue'
import PaperConfidenceDial from '../../../components/paper/PaperConfidenceDial.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import ReviewDecisionRail from './ReviewDecisionRail.vue'
import ReviewChangeSection, {
  type ChangeBeforeCard,
  type ChangeAfterCard,
  type FieldDiff,
} from './ReviewChangeSection.vue'
import ReviewProvenance from './ReviewProvenance.vue'
import ReviewSideEffects from './ReviewSideEffects.vue'
import ReviewConflicts from './ReviewConflicts.vue'
import ReviewHistory from './ReviewHistory.vue'
import type {
  ConfidenceBreakdown,
  ConflictRow,
  EvidenceLink,
  HistoryRow,
  ProvenanceRow,
  SideEffects,
} from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewMain — the centre column: header + decision rail + 5 sections.
 *
 * NOTE on ink-bleed: the header proposal dial would normally trigger the
 * ink-bleed motion while the assistant is composing.  PAPER-10 (the bleed
 * primitive) is parallel work and not merged into this branch yet, so for
 * now we render the static dried/stamped state.  Once PAPER-10 ships, the
 * orchestrator can wire the bleed component above the header on awaiting
 * proposals.
 * TODO(#996): replace the static dial with the BleedStage motion when
 * PAPER-10 lands.
 */
const props = defineProps<{
  /** Pre-formatted serial like `#2026-04-25-014`. */
  serial: string
  /** Pre-formatted timestamp + status like `11:42 PT · awaiting decision`. */
  meta: string
  /** Title fragments — `text` parts render plain, `em` parts render in serif italic ember. */
  titleParts: Array<{ text: string; emphasis?: boolean }>
  lede: string
  decisionSummary: string
  busy?: boolean
  confidence: ConfidenceBreakdown
  before: ChangeBeforeCard
  after: ChangeAfterCard[]
  fields: FieldDiff[]
  changeSubTitle: string
  provenance: ProvenanceRow[]
  /** Evidence links behind the provenance rows; drives the drawer's transcript deep link. */
  evidenceLinks?: EvidenceLink[]
  proposalId: string
  sideEffects: SideEffects
  conflicts: ConflictRow[]
  history: HistoryRow[]
  /** When true the active proposal is settled; the rail offers "File away" only. */
  dismissable?: boolean
}>()

const emit = defineEmits<{
  (event: 'apply'): void
  (event: 'reject'): void
  (event: 'request-edit'): void
  (event: 'defer'): void
  (event: 'dismiss'): void
  (event: 'report', proposalId: string): void
}>()

const dialSubline = computed(() =>
  props.confidence.overall >= props.confidence.threshold
    ? 'Above your apply threshold'
    : 'Below your apply threshold',
)
</script>

<template>
  <div class="paper-review-main" data-testid="paper-review-main">
    <header class="paper-review-main__header">
      <div class="paper-review-main__header-text">
        <div class="paper-review-main__tagrow">
          <PaperTagstamp tone="ember">PROPOSED · DIFF</PaperTagstamp>
          <span class="tk-meta">{{ serial }} · {{ meta }}</span>
        </div>
        <h1 class="tk-h1 paper-review-main__title">
          <template v-for="(part, idx) in titleParts" :key="idx">
            <em v-if="part.emphasis">{{ part.text }}</em>
            <template v-else>{{ part.text }}</template>
          </template>
        </h1>
        <p class="tk-lede paper-review-main__lede">{{ lede }}</p>
      </div>
      <div class="paper-review-main__dial card">
        <PaperConfidenceDial
          :value="confidence.overall"
          caption="CONF"
          :subline="dialSubline"
          data-testid="paper-review-confidence-dial"
        />
        <div class="tk-meta paper-review-main__dial-threshold">
          (set {{ confidence.threshold.toFixed(2) }} · Settings)
        </div>
      </div>
    </header>

    <ReviewDecisionRail
      :summary="decisionSummary"
      :busy="busy"
      :dismissable="dismissable"
      data-testid="paper-review-decision-rail"
      @apply="emit('apply')"
      @reject="emit('reject')"
      @request-edit="emit('request-edit')"
      @defer="emit('defer')"
      @dismiss="emit('dismiss')"
    />

    <ReviewChangeSection
      :before="before"
      :after="after"
      :fields="fields"
      :sub-title="changeSubTitle"
    />

    <ReviewProvenance
      :rows="provenance"
      :evidence-links="evidenceLinks"
      :proposal-id="proposalId"
      @report="emit('report', $event)"
    />
    <ReviewSideEffects :data="sideEffects" />
    <ReviewConflicts :rows="conflicts" />
    <ReviewHistory :rows="history" />

    <footer class="paper-review-main__footer">
      <span class="tk-serial">REVIEW · {{ serial }} · LOCAL-FIRST · LEDGER</span>
      <span class="tk-serial">{{ dismissable ? 'PRESS ⌫ TO FILE AWAY' : 'PRESS ⏎ TO APPLY · ⌫ TO REJECT' }}</span>
    </footer>
  </div>
</template>

<style scoped>
.paper-review-main {
  overflow: auto;
  padding: 28px 36px 40px;
  min-width: 0;
}
.paper-review-main__header {
  display: grid;
  grid-template-columns: 1fr auto;
  align-items: flex-start;
  gap: 24px;
}
.paper-review-main__tagrow {
  display: flex;
  gap: 10px;
  align-items: center;
}
.paper-review-main__title {
  margin: 10px 0 6px;
  max-width: 660px;
}
.paper-review-main__lede {
  margin-top: 6px;
}
.paper-review-main__dial {
  padding: 14px;
  width: 200px;
  display: flex;
  flex-direction: column;
  align-items: center;
}
.paper-review-main__dial-threshold {
  font-size: 10px;
  margin-top: 2px;
}
.paper-review-main__footer {
  margin-top: 36px;
  padding-top: 14px;
  border-top: 1px solid var(--line);
  display: flex;
  justify-content: space-between;
}
</style>
