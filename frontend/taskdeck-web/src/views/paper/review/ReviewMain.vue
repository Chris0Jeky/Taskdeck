<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
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
import ReviewAppliedDecisionRecord from '../../../components/review/ReviewAppliedDecisionRecord.vue'
import type { Proposal } from '../../../types/automation'
import { normalizeProposalStatus } from '../../../utils/automation'
import type {
  ConfidenceBreakdown,
  ConflictRow,
  EvidenceLink,
  HistoryRow,
  ProvenanceMetadata,
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
    /** Server-recorded producer metadata for this capture-linked proposal. */
    metadata?: ProvenanceMetadata | null
    /** Suppress only the inline producer sentence for an effective saved revision. */
    suppressProducerFootnote?: boolean
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
    readOnly?: boolean
    /** The just-recorded outcome, retained at its original decision locus. */
    decisionReceipt?: 'approved' | 'applied' | 'rejected' | 'deferred' | null
    /** The exact Applied proposal when this surface is a historical, read-only record. */
    appliedProposal?: Proposal | null
  }>(),
  {
    applyPhase: 'approve',
    editLock: 'off',
    readOnly: false,
    decisionReceipt: null,
    appliedProposal: null,
    metadata: null,
    suppressProducerFootnote: false,
  },
)

const { t } = useI18n()
const reviewMainEl = ref<HTMLElement | null>(null)
const decisionReceiptEl = ref<HTMLElement | null>(null)
const provenanceExpanded = ref(false)

const isAppliedRecord = computed(
  () =>
    !!props.appliedProposal &&
    normalizeProposalStatus(props.appliedProposal.status) === 'Applied',
)

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

function toggleProvenance() {
  provenanceExpanded.value = !provenanceExpanded.value
}

defineExpose({ toggleProvenance })

const hasNumericConfidence = computed(
  () =>
    props.confidence.overall !== null &&
    Number.isFinite(props.confidence.overall) &&
    (props.confidence.source === 'model-reported' || props.confidence.source === 'derived'),
)

const confidenceCaption = computed(() =>
  props.confidence.source === 'model-reported'
    ? t('review.main.dial.modelCaption')
    : t('review.main.dial.derivedCaption'),
)

const confidenceSubline = computed(() =>
  props.confidence.source === 'model-reported'
    ? t('review.main.dial.modelReported')
    : t('review.main.dial.derived'),
)

const confidenceWithoutNumber = computed(() =>
  props.confidence.source === 'deterministic'
    ? t('review.main.dial.deterministic')
    : t('review.main.dial.notReported'),
)

/**
 * A receipt replaces the controls that created it. Keep the reviewer at that
 * decision locus instead of leaving focus on a removed button or a global
 * keymap. Approval is the sole exception: its receipt truthfully leaves one
 * explicit control, Apply to board, so focus advances only to that control.
 */
watch(
  () => props.decisionReceipt,
  async (receipt, previousReceipt) => {
    if (!receipt || receipt === previousReceipt) return
    await nextTick()

    // Decision requests are asynchronous, and the queue plus disclosure
    // controls remain available while one is pending. Respect a reviewer who
    // moved to another control; redirect only when focus fell back to the
    // document because the initiating control was disabled or removed.
    const activeElement = document.activeElement
    if (
      activeElement instanceof HTMLElement &&
      activeElement !== document.body &&
      activeElement !== document.documentElement &&
      activeElement.isConnected
    ) {
      return
    }

    if (receipt === 'approved') {
      reviewMainEl.value?.querySelector<HTMLButtonElement>('[data-testid="decision-apply"]')?.focus()
      return
    }

    decisionReceiptEl.value?.focus()
  },
)
</script>

<template>
  <div ref="reviewMainEl" class="paper-review-main" data-testid="paper-review-main">
    <header class="paper-review-main__header">
      <div class="paper-review-main__header-text">
        <div class="paper-review-main__tagrow">
          <PaperTagstamp :tone="isAppliedRecord ? 'mute' : 'ember'">{{
            isAppliedRecord ? $t('review.appliedRecord.tagstamp') : $t('review.main.tagstamp')
          }}</PaperTagstamp>
          <span class="tk-meta">{{ serial }} · {{ meta }}</span>
        </div>
        <h1 class="tk-h1 paper-review-main__title">
          <template v-for="(part, idx) in titleParts" :key="idx">
            <em v-if="part.emphasis">{{ part.text }}</em>
            <template v-else>{{ part.text }}</template>
          </template>
        </h1>
        <p class="tk-lede paper-review-main__lede">
          {{ isAppliedRecord ? $t('review.appliedRecord.lede') : lede }}
        </p>
      </div>
      <div v-if="!isAppliedRecord" class="paper-review-main__dial card">
        <PaperConfidenceDial
          v-if="hasNumericConfidence && confidence.overall !== null"
          :value="confidence.overall"
          :caption="confidenceCaption"
          :subline="confidenceSubline"
          data-testid="paper-review-confidence-dial"
        />
        <div
          v-else
          class="paper-review-main__confidence-badge"
          data-testid="paper-review-confidence-source"
        >
          <strong>{{ confidenceWithoutNumber }}</strong>
          <span class="tk-meta">{{ $t('review.main.dial.noModelNumber') }}</span>
        </div>
      </div>
    </header>

    <!-- #1818: approve is phase 1 of 2 and does NOT touch the board. Without this
         banner the only feedback was a quiet status-line change, so a first-run
         user reasonably read "approved" as "applied". role="status" so the state
         change is announced, not just drawn. -->
    <p
      v-if="
        !readOnly &&
        !isAppliedRecord &&
        !dismissable &&
        applyPhase === 'execute' &&
        decisionReceipt !== 'approved'
      "
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
      v-if="readOnly"
      class="paper-review-main__history-notice tk-meta"
      role="status"
      data-testid="paper-review-history-mode"
    >
      {{ $t('review.historyMode.notice') }}
    </p>

    <!--
      A decision receipt reports an action THIS session just took. Archived
      history takes none, so it is suppressed there rather than left to depend
      on the parent never setting it (#1973).
    -->
    <p
      v-else-if="decisionReceipt"
      class="paper-review-main__decision-receipt"
      role="status"
      tabindex="-1"
      ref="decisionReceiptEl"
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

    <ReviewAppliedDecisionRecord
      v-if="isAppliedRecord && appliedProposal"
      :proposal="appliedProposal"
    />

    <ReviewDecisionRail
      v-if="
        !readOnly &&
        (isAppliedRecord
          ? dismissable && !decisionReceipt
          : !decisionReceipt || decisionReceipt === 'approved')
      "
      :summary="isAppliedRecord ? $t('review.appliedRecord.filingSummary') : decisionSummary"
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
      :applied="isAppliedRecord"
    />

    <ReviewProvenance
      :rows="provenance"
      :metadata="metadata"
      :suppress-producer-footnote="suppressProducerFootnote"
      :evidence-links="evidenceLinks"
      :proposal-id="proposalId"
      :read-only="readOnly"
      :details-expanded="provenanceExpanded"
      @update:details-expanded="provenanceExpanded = $event"
      @report="emit('report', $event)"
    />
    <ReviewSideEffects :data="sideEffects" />
    <ReviewConflicts :rows="conflicts" />
    <ReviewHistory :rows="history" />

    <footer class="paper-review-main__footer">
      <span class="tk-serial">{{ $t('review.main.footer', { serial }) }}</span>
      <span
        v-if="!readOnly && !isAppliedRecord && (!decisionReceipt || decisionReceipt === 'approved')"
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
.paper-review-main__confidence-badge {
  min-height: 86px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 6px;
  text-align: center;
}
.paper-review-main__confidence-badge strong {
  color: var(--ink-deep);
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.08em;
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
.paper-review-main__history-notice {
  margin: 18px 0 0;
  padding: 10px 14px;
  border: 1px solid var(--line);
  background: var(--paper-2);
  color: var(--ink-2);
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
