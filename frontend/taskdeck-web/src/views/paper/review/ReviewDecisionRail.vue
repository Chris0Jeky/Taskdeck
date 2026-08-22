<script setup lang="ts">
import { computed, useId } from 'vue'
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
 * the row, and no button's label may wrap, so all four share one height. The
 * row itself wraps instead — the rail is a meta group plus an actions group,
 * and the actions group drops onto its own line rather than overflowing a
 * narrow column. See the wrap comment in the stylesheet.
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

/**
 * Why the shared decision lock is held, when it is held by the revision
 * composer (GH-1964):
 *  - `off`     — no edit in progress; the lock, if any, is a short network
 *                round trip and the disabled treatment carries it alone.
 *  - `editing` — the composer is open and holds the lock indefinitely. The rail
 *                must say so and must offer the exit ITSELF: the composer's own
 *                Cancel sits below the diff, off-screen on a real proposal,
 *                which is exactly how this became a brick.
 *  - `saving`  — the revision is being written. Same explanation, but there is
 *                nothing to cancel: the exit reappears if the save fails.
 */
export type EditLock = 'off' | 'editing' | 'saving'

const props = withDefaults(
  defineProps<{
    summary: string
    busy?: boolean
    /** When true the proposal is settled; the rail shows only "File away". */
    dismissable?: boolean
    applyPhase?: ApplyPhase
    editLock?: EditLock
  }>(),
  { applyPhase: 'approve', editLock: 'off' },
)

const { t } = useI18n()

/**
 * The lock note is only rendered on a decision rail (a settled proposal has no
 * decisions to explain), so a single id per mounted rail is enough to describe
 * the disabled buttons. `useId` keeps it unique even when a spec mounts two.
 */
const lockNoteId = `paper-review-decision-lock-${useId()}`

const showEditLock = computed(() => !props.dismissable && props.editLock !== 'off')

const editLockNote = computed(() =>
  props.editLock === 'saving'
    ? t('review.decisionRail.editLock.saving')
    : t('review.decisionRail.editLock.editing'),
)

/**
 * `aria-describedby` is only attached while the explanation exists — a dangling
 * reference to an absent id is worse than none, because assistive tech reports
 * nothing and the markup claims otherwise.
 */
const decisionDescribedBy = computed(() => (showEditLock.value ? lockNoteId : undefined))

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
  (event: 'cancel-edit'): void
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
    <!-- The rail is two groups, not one flat row (GH-1943): the meta group
         absorbs the free space the old spacer used to, and the actions group is
         the single flex item whose width decides whether the rail stays on one
         line. See the wrap rules in the stylesheet below. -->
    <div class="paper-review-decision__meta">
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
      <!-- GH-1964 — the lock says its own name. `role="status"` so the reason a
           whole toolbar went inert is announced, not only drawn. -->
      <span
        v-if="showEditLock"
        :id="lockNoteId"
        class="tk-meta paper-review-decision__lock"
        role="status"
        data-testid="decision-lock-note"
      >{{ editLockNote }}</span>
    </div>

    <div class="paper-review-decision__actions">
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
        <!-- GH-1964 — the exit lives ON the rail, never only inside the composer
             it is exiting. This button is deliberately NOT `:disabled="busy"`:
             the lock it cancels is the very thing making everything else grey,
             so gating it on `busy` would recreate the dead end. It is gated on
             the save instead, which is the one window where there is nothing
             left to cancel. -->
        <PaperHLBtn
          v-if="showEditLock"
          :label="$t('review.decisionRail.editLock.cancel')"
          :disabled="editLock === 'saving'"
          data-testid="decision-cancel-edit"
          @click="emit('cancel-edit')"
        />
        <PaperHLBtn
          :label="$t('review.decisionRail.reject')"
          kbd="⌫"
          :disabled="busy"
          :aria-describedby="decisionDescribedBy"
          data-testid="decision-reject"
          @click="emit('reject')"
        />
        <PaperHLBtn
          :label="$t('review.decisionRail.requestEdit')"
          kbd="E"
          :disabled="busy"
          :aria-describedby="decisionDescribedBy"
          data-testid="decision-edit"
          @click="emit('request-edit')"
        />
        <PaperHLBtn
          :label="$t('review.decisionRail.defer')"
          kbd="D"
          :disabled="busy"
          :aria-describedby="decisionDescribedBy"
          data-testid="decision-defer"
          @click="emit('defer')"
        />
        <PaperHLBtn
          kbd="⏎"
          variant="ember"
          :disabled="busy"
          :aria-describedby="decisionDescribedBy"
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
  </div>
</template>

<style scoped>
/* GH-1943 — the wrap path.
 *
 * Pinning every button's width (`flex: none` below) is what stops one long
 * label driving the row's height, but it also leaves an unshrinkable block of
 * four buttons with nowhere to go when the column narrows: without a wrap path
 * the rail overflows horizontally instead, and the longer it/es labels
 * ("Applica alla bacheca", "Chiedi modifica") reach that point sooner.
 *
 * The break is driven by the ACTIONS group alone. Flexbox collects lines from
 * items' hypothetical main sizes BEFORE any shrinking, so a content-sized meta
 * group would push the buttons onto a second line while there was still room to
 * ellipsise the summary; `flex: 1 1 0` on the meta group makes it contribute
 * nothing to that decision. The rail therefore stays on one line for exactly as
 * long as the buttons fit and drops them onto their own line the moment they do
 * not — no breakpoint, no magic width, same behaviour in every locale.
 *
 * GH-1943 survives the wrap: each button keeps `flex: none`, the shared
 * min-height and its nowrap label, so the four stay the same size on whichever
 * line they land, and the primary button's width reservation is per-button. */
.paper-review-decision {
  margin-top: 18px;
  padding: 12px 16px;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  position: sticky;
  top: 0;
  z-index: 2;
}
/* The text half. `flex: 1 1 0` is load-bearing twice over: it keeps the group
 * out of the line-breaking decision (above) and it absorbs the leftover width,
 * which is the job the old `__spacer` element did. */
.paper-review-decision__meta {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1 1 0;
  min-width: 0;
  /* min-width: 0 lets the box shrink below its content; without clipping, the
     tagstamp's min-content width would paint over the actions group in the
     band just above the wrap threshold. */
  overflow: hidden;
}
/* The button half. It wraps internally too, so at the narrowest columns the
 * four buttons stack into rows rather than overflowing. `margin-left: auto`
 * keeps them right-aligned on the line they end up on. */
.paper-review-decision__actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 12px;
  margin-left: auto;
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
/* GH-1964 — the lock explanation. It may wrap (it is a sentence, not a stamp),
 * but it must not push the actions group off the rail, so it shares the meta
 * group's shrink budget. */
.paper-review-decision__lock {
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
