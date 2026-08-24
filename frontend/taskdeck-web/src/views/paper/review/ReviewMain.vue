<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperConfidenceDial from '../../../components/paper/PaperConfidenceDial.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import ReviewDecisionRail, { type ApplyPhase, type EditLock } from './ReviewDecisionRail.vue'
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
const props = withDefaults(
  defineProps<{
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
    /**
     * Which half of the ADR-0003 two-phase apply the primary action will run.
     * `execute` means the proposal is already approved and the board has NOT been
     * touched yet — the state the #1818 banner exists to make legible.
     */
    applyPhase?: ApplyPhase
    /**
     * Whether the revision composer holds the shared decision lock, so the rail
     * can explain the greyed-out row and carry the exit (GH-1964).
     */
    editLock?: EditLock
    /** The just-recorded outcome, retained at its original decision locus. */
    decisionReceipt?: 'approved' | 'applied' | 'rejected' | 'deferred' | null
  }>(),
  { applyPhase: 'approve', editLock: 'off', decisionReceipt: null },
)

const { t } = useI18n()

/**
 * The keyboard hint must name the phase the key will actually run (#1818 AC2):
 * ⏎ on an approved proposal opens the apply confirmation, it does not re-approve.
 */
const keyHint = computed(() => {
  if (props.dismissable) return t('review.main.keyHint.fileAway')
  return props.applyPhase === 'execute'
    ? t('review.main.keyHint.confirmApply')
    : t('review.main.keyHint.approve')
})

const emit = defineEmits<{
  (event: 'apply'): void
  (event: 'reject'): void
  (event: 'request-edit'): void
  (event: 'defer'): void
  (event: 'dismiss'): void
  (event: 'cancel-edit'): void
  (event: 'report', proposalId: string): void
}>()

const dialSubline = computed(() =>
  props.confidence.overall >= props.confidence.threshold
    ? t('review.main.dial.above')
    : t('review.main.dial.below'),
)
</script>

<template>
  <div class="paper-review-main" data-testid="paper-review-main">
    <header class="paper-review-main__header">
      <div class="paper-review-main__header-text">
        <div class="paper-review-main__tagrow">
          <PaperTagstamp tone="ember">{{ $t('review.main.tagstamp') }}</PaperTagstamp>
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
          :caption="$t('review.main.dial.caption')"
          :subline="dialSubline"
          data-testid="paper-review-confidence-dial"
        />
        <div class="tk-meta paper-review-main__dial-threshold">
          {{ $t('review.main.dial.threshold', { value: confidence.threshold.toFixed(2) }) }}
        </div>
      </div>
    </header>

    <!-- #1818: approve is phase 1 of 2 and does NOT touch the board. Without this
         banner the only feedback was a quiet status-line change, so a first-run
         user reasonably read "approved" as "applied". role="status" so the state
         change is announced, not just drawn. -->
    <p
      v-if="!dismissable && applyPhase === 'execute' && decisionReceipt !== 'approved'"
      class="paper-review-main__approved-banner"
      role="status"
      data-testid="paper-review-approved-banner"
    >
      <strong>{{ $t('review.main.approvedBanner.title') }}</strong>
      {{
        $t('review.main.approvedBanner.body', {
          action: $t('review.decisionRail.apply.execute'),
        })
      }}
    </p>

    <p
      v-if="decisionReceipt"
      class="paper-review-main__decision-receipt"
      role="status"
      data-testid="paper-review-decision-receipt"
      :data-decision="decisionReceipt"
    >
      <template v-if="decisionReceipt === 'approved'">
        <strong>{{ $t('review.main.decisionReceipt.approved.title') }}</strong>
        {{ $t('review.main.decisionReceipt.approved.body', { action: $t('review.decisionRail.apply.execute') }) }}
      </template>
      <template v-else-if="decisionReceipt === 'applied'">
        <strong>{{ $t('review.main.decisionReceipt.applied.title') }}</strong>
        {{ $t('review.main.decisionReceipt.applied.body') }}
      </template>
      <template v-else-if="decisionReceipt === 'rejected'">
        <strong>{{ $t('review.main.decisionReceipt.rejected.title') }}</strong>
        {{ $t('review.main.decisionReceipt.rejected.body') }}
      </template>
      <template v-else>
        <strong>{{ $t('review.main.decisionReceipt.deferred.title') }}</strong>
        {{ $t('review.main.decisionReceipt.deferred.body') }}
      </template>
    </p>

    <ReviewDecisionRail
      v-if="!decisionReceipt || decisionReceipt === 'approved'"
      :summary="decisionSummary"
      :busy="busy"
      :dismissable="dismissable"
      :apply-phase="applyPhase"
      :edit-lock="editLock"
      :apply-only="decisionReceipt === 'approved'"
      data-testid="paper-review-decision-rail"
      @apply="emit('apply')"
      @reject="emit('reject')"
      @request-edit="emit('request-edit')"
      @defer="emit('defer')"
      @dismiss="emit('dismiss')"
      @cancel-edit="emit('cancel-edit')"
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
      <span class="tk-serial">{{ $t('review.main.footer', { serial }) }}</span>
      <span
        v-if="!decisionReceipt || decisionReceipt === 'approved'"
        class="tk-serial"
        data-testid="paper-review-key-hint"
      >{{ keyHint }}</span>
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
.paper-review-main__approved-banner {
  margin: 18px 0 0;
  padding: 10px 14px;
  border: 1px solid var(--ember);
  border-left-width: 4px;
  background: var(--ember-tint);
  color: var(--ember-ink);
  font-size: 13px;
  line-height: 1.45;
}
.paper-review-main__decision-receipt {
  margin: 18px 0 0;
  padding: 10px 14px;
  border: 1px solid var(--ember);
  border-left-width: 4px;
  background: var(--ember-tint);
  color: var(--ember-ink);
  font-size: 13px;
  line-height: 1.45;
}
.paper-review-main__footer {
  margin-top: 36px;
  padding-top: 14px;
  border-top: 1px solid var(--line);
  display: flex;
  justify-content: space-between;
}
</style>
