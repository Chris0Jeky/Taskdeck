<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'

/**
 * ReviewDecisionRail — sticky bar with the four decision actions
 * (Reject ⌫ · Request edit E · Defer D · Approve/Apply to board ⏎). The ⏎
 * action is rendered in the ember variant and its label follows `applyPhase`
 * (#1818), because it runs a different half of the two-phase apply depending
 * on the proposal's status. Disabled state propagates while a network call is
 * in flight.
 *
 * Both label geometries are pinned (GH-1942 / GH-1943): the primary button
 * reserves the width of the widest phase label so the phase flip cannot shift
 * the row, and no button's label may wrap, so all four share one height.
 *
 * In a terminal state (`dismissable` — the proposal is Applied / Rejected /
 * Failed / Expired / Approved-then-expired per the shared
 * `isProposalDismissable` rule, #1124 / ADR-0038 / #1161) the four decision
 * buttons are meaningless, so the rail becomes a *filing* rail: a single
 * "File away" button reuses the ⌫ key. The status stamp already tells the
 * story, so the eyebrow stamp reads SETTLED.
 */
/**
 * Which half of the ADR-0003 two-phase apply the ⏎ / primary button will run
 * (#1818):
 *  - `approve` — the proposal is still pending; the click records the approval
 *                and does NOT touch the board.
 *  - `execute` — the proposal is already approved; the click opens the
 *                confirmation that finally writes to the board.
 */
export type ApplyPhase = 'approve' | 'execute'

const props = withDefaults(
  defineProps<{
    summary: string
    busy?: boolean
    /** When true the proposal is settled; the rail shows only "File away". */
    dismissable?: boolean
    applyPhase?: ApplyPhase
  }>(),
  { applyPhase: 'approve' },
)

const { t } = useI18n()

/**
 * Both phase labels are rendered, always, into the SAME grid cell of the
 * primary button: its width is therefore max(approve, execute) in every phase,
 * so flipping the phase can never resize the button and shift the rest of the
 * rail (GH-1942). Only the active face is visible — the inactive one is
 * `visibility: hidden` (plus `aria-hidden`), which keeps it out of the
 * accessibility tree while it still contributes its intrinsic width.
 *
 * A fixed `min-width` was rejected: the labels are translated (it/es run
 * longer than en), so any hardcoded reservation is wrong in some locale.
 *
 * The button must never claim to do what the other phase does. #1818
 */
const applyFaces = computed(() => [
  { phase: 'approve' as const, label: t('review.decisionRail.apply.approve') },
  { phase: 'execute' as const, label: t('review.decisionRail.apply.execute') },
])

const applyAriaLabel = computed(() =>
  props.applyPhase === 'execute'
    ? t('review.decisionRail.apply.executeLabel')
    : t('review.decisionRail.apply.approveLabel'),
)

const emit = defineEmits<{
  (event: 'apply'): void
  (event: 'reject'): void
  (event: 'request-edit'): void
  (event: 'defer'): void
  (event: 'dismiss'): void
}>()
</script>

<template>
  <div
    class="card-lift halo-ember paper-review-decision"
    role="toolbar"
    :aria-label="
      dismissable
        ? $t('review.decisionRail.toolbar.filing')
        : $t('review.decisionRail.toolbar.decision')
    "
    :data-apply-phase="dismissable ? 'settled' : applyPhase"
  >
    <PaperTagstamp :tone="dismissable ? 'mute' : 'ember'">{{
      dismissable
        ? $t('review.decisionRail.stamp.settled')
        : $t('review.decisionRail.stamp.decision')
    }}</PaperTagstamp>
    <span class="tk-meta paper-review-decision__summary">{{ summary }}</span>
    <span
      v-if="!dismissable"
      class="tk-meta paper-review-decision__step"
      data-testid="decision-step-hint"
    >{{
      applyPhase === 'execute'
        ? $t('review.decisionRail.step.execute')
        : $t('review.decisionRail.step.approve')
    }}</span>
    <span class="paper-review-decision__spacer" />

    <template v-if="dismissable">
      <PaperHLBtn
        :label="$t('review.decisionRail.fileAway.label')"
        kbd="⌫"
        :disabled="busy"
        data-testid="decision-file-away"
        :aria-label="$t('review.decisionRail.fileAway.ariaLabel')"
        @click="emit('dismiss')"
      />
    </template>
    <template v-else>
      <PaperHLBtn
        :label="$t('review.decisionRail.reject')"
        kbd="⌫"
        :disabled="busy"
        data-testid="decision-reject"
        @click="emit('reject')"
      />
      <PaperHLBtn
        :label="$t('review.decisionRail.requestEdit')"
        kbd="E"
        :disabled="busy"
        data-testid="decision-edit"
        @click="emit('request-edit')"
      />
      <PaperHLBtn
        :label="$t('review.decisionRail.defer')"
        kbd="D"
        :disabled="busy"
        data-testid="decision-defer"
        @click="emit('defer')"
      />
      <PaperHLBtn
        kbd="⏎"
        variant="ember"
        :disabled="busy"
        data-testid="decision-apply"
        :data-apply-phase="applyPhase"
        :aria-label="applyAriaLabel"
        @click="emit('apply')"
      >
        <!-- Width-reserving label stack (GH-1942): both faces occupy one grid
             cell, only the active one is visible. -->
        <span class="paper-review-decision__apply-label">
          <span
            v-for="face in applyFaces"
            :key="face.phase"
            class="paper-review-decision__apply-face"
            :data-active="face.phase === applyPhase ? 'true' : 'false'"
            :aria-hidden="face.phase === applyPhase ? undefined : 'true'"
            :data-testid="
              face.phase === applyPhase ? 'decision-apply-label' : 'decision-apply-reserve'
            "
          >{{ face.label }}</span>
        </span>
      </PaperHLBtn>
    </template>
  </div>
</template>

<style scoped>
.paper-review-decision {
  margin-top: 18px;
  padding: 12px 16px;
  display: flex;
  align-items: center;
  gap: 12px;
  position: sticky;
  top: 0;
  z-index: 2;
}
.paper-review-decision__summary {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  /* A flex item's default `min-width: auto` floors it at its content width, so
   * without this the summary refuses to shrink and squeezes the buttons. */
  min-width: 0;
}
/* GH-1943 — no single decision button may drive the row's height.
 *
 * Measured cause: `.phlbtn-label` is `inline-flex` with no `white-space`, so
 * "Request edit" wrapped onto two lines while Reject / Defer / Approve stayed
 * on one, making that one button visibly taller and the row asymmetric. The
 * labels stay on one line here, and every button in the rail shares one
 * min-height, so all four are the same size — in BOTH apply phases (GH-1942),
 * and for the longer it/es translations too. `flex: none` stops the row from
 * shrinking a button instead of ellipsising the summary. */
.paper-review-decision :deep(.pbtn) {
  flex: none;
  min-height: 36px;
}
.paper-review-decision :deep(.phlbtn-label) {
  white-space: nowrap;
}
.paper-review-decision__spacer {
  flex: 1;
}
/* Phase hint sits next to the summary; it must stay readable but never push the
 * decision buttons off the sticky rail. */
.paper-review-decision__step {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  color: var(--ember-ink);
  font-weight: 600;
}
/* GH-1942 — the primary button reserves the width of the WIDEST phase label:
 * both faces share one grid cell, so the approve→execute flip repaints the
 * button without resizing it. `visibility: hidden` keeps the inactive face out
 * of the paint and the a11y tree while it still sets the cell's width. */
.paper-review-decision__apply-label {
  display: grid;
  grid-template-areas: 'phase';
  align-items: center;
  justify-items: center;
}
.paper-review-decision__apply-face {
  grid-area: phase;
  white-space: nowrap;
}
.paper-review-decision__apply-face[data-active='false'] {
  visibility: hidden;
}
/* Phase 2 is the one that writes to the board — give the rail a visibly warmer
 * ground so "approved, not yet applied" is never mistaken for "pending". */
.paper-review-decision[data-apply-phase='execute'] {
  background: var(--ember-tint);
  border-color: var(--ember);
}
</style>
