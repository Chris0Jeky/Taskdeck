<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'

/**
 * ReviewDecisionRail — sticky bar with the four decision actions
 * (Reject ⌫ · Request edit E · Defer D · Approve/Confirm apply ⏎). The ⏎
 * action is rendered in the ember variant and its label follows `applyPhase`
 * (#1818), because it runs a different half of the two-phase apply depending
 * on the proposal's status. Disabled state propagates while a network call is
 * in flight.
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

// The button must never claim to do what the other phase does. #1818
const applyLabel = computed(() =>
  props.applyPhase === 'execute'
    ? t('review.decisionRail.apply.execute')
    : t('review.decisionRail.apply.approve'),
)
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
        :label="applyLabel"
        kbd="⏎"
        variant="ember"
        :disabled="busy"
        data-testid="decision-apply"
        :data-apply-phase="applyPhase"
        :aria-label="applyAriaLabel"
        @click="emit('apply')"
      />
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
  color: var(--ember-ink);
  font-weight: 600;
}
/* Phase 2 is the one that writes to the board — give the rail a visibly warmer
 * ground so "approved, not yet applied" is never mistaken for "pending". */
.paper-review-decision[data-apply-phase='execute'] {
  background: var(--ember-tint);
  border-color: var(--ember);
}
</style>
