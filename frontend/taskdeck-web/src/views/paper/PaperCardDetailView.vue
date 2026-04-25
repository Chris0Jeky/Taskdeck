<script setup lang="ts">
import { computed } from 'vue'
import PaperHLBtn from '../../components/paper/PaperHLBtn.vue'
import PaperIcon from '../../components/paper/PaperIcon.vue'
import PaperSubtaskLedger from '../../components/paper/PaperSubtaskLedger.vue'
import type { PaperSubtaskItem } from '../../components/paper/PaperSubtaskLedger.vue'
import type { Card, CardCaptureProvenance } from '../../types/board'

/**
 * PaperCardDetailView — focus-mode route view for a single card.  Mirrors
 * `CardDetailSurface` in
 * `design_handoff_taskdeck_paper/paper/surface-misc.jsx`.
 *
 * Layout:
 *   - Wide single column, max-width 720px on `--paper-2`, centered.
 *   - Title is serif 28px, body 15px Inter.
 *   - Subtasks render as a `PaperSubtaskLedger` checklist.
 *   - Activity log is a vertical ledger to the right.
 *   - When `proposal.proposalStatus === 'PendingReview'` (or numeric 0) a
 *     pending-proposal banner sits at the top.
 *
 * Data flow is intentionally prop-based so this view can be reused in three
 * call sites without dragging in route-level fetching:
 *
 *   1. Routed surface that resolves the card from store and passes it in.
 *   2. CardModal-as-route adapter that mounts this in lieu of the dialog.
 *   3. Test smoke render that stubs the card.
 *
 * Subtasks are not part of the canonical `Card` shape yet (see
 * `types/board.ts`) — callers can derive them from card metadata or pass an
 * empty array.
 */

type ActivityEntry = {
  serial: string
  text: string
  age: string
}

const props = withDefaults(
  defineProps<{
    card: Card
    /** Subtasks rendered as a paper checklist.  Empty array hides the section. */
    subtasks?: PaperSubtaskItem[]
    /** Most-recent-first activity entries. */
    activity?: ActivityEntry[]
    /** Capture/proposal provenance.  When the proposal is pending, the banner shows. */
    provenance?: CardCaptureProvenance | null
    /** Optional column / board labels for the eyebrow line. */
    columnName?: string
    boardName?: string
    /** Card status — drives the eyebrow ("in progress", "to do" ...). */
    statusLabel?: string
    /** Card serial id, displayed in the eyebrow (e.g. "C-090"). */
    serial?: string
    /** Assignee name (right rail). */
    assignee?: string | null
  }>(),
  {
    subtasks: () => [],
    activity: () => [],
    provenance: null,
    columnName: '',
    boardName: '',
    statusLabel: 'open',
    serial: '',
    assignee: null,
  },
)

const emit = defineEmits<{
  close: []
  'open-proposal': [proposalId: string]
  'toggle-subtask': [id: string]
}>()

const subtaskCount = computed(() => props.subtasks.length)
const subtaskDoneCount = computed(() => props.subtasks.filter((s) => s.done).length)

const eyebrow = computed(() => {
  const parts: string[] = []
  if (props.serial) parts.push(props.serial)
  if (props.statusLabel) parts.push(props.statusLabel)
  if (props.boardName) parts.push(props.boardName)
  return parts.join(' · ')
})

const hasPendingProposal = computed(() => {
  const p = props.provenance
  if (!p) return false
  // Numeric enum: 0 = PendingReview; string variant: 'PendingReview'.
  return p.proposalStatus === 'PendingReview' || p.proposalStatus === 0
})

function handleToggleSubtask(id: string) {
  emit('toggle-subtask', id)
}

function handleOpenProposal() {
  if (props.provenance?.proposalId) {
    emit('open-proposal', props.provenance.proposalId)
  }
}
</script>

<template>
  <div class="paper-card-detail" data-paper-card-detail>
    <article class="paper-card-detail__sheet card-lift">
      <div v-if="hasPendingProposal" class="paper-card-detail__banner" data-pending-proposal>
        <div class="paper-card-detail__banner-content">
          <span class="tk-eyebrow paper-card-detail__banner-eyebrow">Pending proposal</span>
          <p class="paper-card-detail__banner-text">
            A haiku proposal is waiting in <em>Review</em> for this card.
          </p>
        </div>
        <PaperHLBtn label="Open in Review" kbd="⏎" variant="ember" @click="handleOpenProposal" />
      </div>

      <header class="paper-card-detail__head">
        <div class="paper-card-detail__head-text">
          <div v-if="eyebrow" class="tk-eyebrow paper-card-detail__eyebrow">{{ eyebrow }}</div>
          <h1 class="paper-card-detail__title">{{ card.title }}</h1>
        </div>
        <button
          type="button"
          class="paper-card-detail__close"
          aria-label="Close card"
          @click="emit('close')"
        >
          <PaperIcon name="x" />
        </button>
      </header>

      <div class="paper-card-detail__body">
        <p v-if="card.description" class="paper-card-detail__description">
          {{ card.description }}
        </p>

        <section v-if="subtaskCount > 0" class="paper-card-detail__section">
          <div class="tk-eyebrow paper-card-detail__section-eyebrow">
            Subtasks · {{ subtaskDoneCount }} of {{ subtaskCount }}
          </div>
          <PaperSubtaskLedger :subtasks="subtasks" @toggle="handleToggleSubtask" />
        </section>

        <section v-if="activity.length > 0" class="paper-card-detail__section">
          <div class="tk-eyebrow paper-card-detail__section-eyebrow">Activity ledger</div>
          <div class="rule-ledger paper-card-detail__activity">
            <div
              v-for="entry in activity"
              :key="entry.serial"
              class="paper-card-detail__activity-row"
            >
              <span class="tk-serial paper-card-detail__activity-serial">{{ entry.serial }}</span>
              <span class="paper-card-detail__activity-text">{{ entry.text }}</span>
              <span class="paper-card-detail__activity-age">{{ entry.age }}</span>
            </div>
          </div>
        </section>
      </div>

      <aside class="paper-card-detail__rail">
        <div class="paper-card-detail__meta card">
          <div class="paper-card-detail__meta-row">
            <span class="tk-eyebrow">Status</span>
            <span class="paper-card-detail__meta-value">{{ statusLabel }}</span>
          </div>
          <div v-if="assignee" class="paper-card-detail__meta-row">
            <span class="tk-eyebrow">Assignee</span>
            <span class="paper-card-detail__meta-value">{{ assignee }}</span>
          </div>
          <div v-if="card.dueDate" class="paper-card-detail__meta-row">
            <span class="tk-eyebrow">Due</span>
            <span class="paper-card-detail__meta-value">{{ card.dueDate }}</span>
          </div>
          <div v-if="card.labels && card.labels.length > 0" class="paper-card-detail__meta-row">
            <span class="tk-eyebrow">Labels</span>
            <span class="paper-card-detail__meta-value">
              {{ card.labels.map((l) => l.name).join(' · ') }}
            </span>
          </div>
          <div v-if="subtaskCount > 0" class="paper-card-detail__meta-row">
            <span class="tk-eyebrow">Subtasks</span>
            <span class="paper-card-detail__meta-value">{{ subtaskDoneCount }}/{{ subtaskCount }}</span>
          </div>
          <div v-if="provenance?.captureItemId" class="paper-card-detail__meta-row">
            <span class="tk-eyebrow">Source</span>
            <span class="paper-card-detail__meta-value">Capture {{ provenance.captureItemId }}</span>
          </div>
        </div>
      </aside>
    </article>
  </div>
</template>

<style scoped>
.paper-card-detail {
  background: var(--paper-2);
  min-height: 100%;
  padding: 32px 16px;
  display: flex;
  justify-content: center;
  font-family: var(--sans);
}

.paper-card-detail__sheet {
  width: 100%;
  max-width: 720px;
  display: grid;
  grid-template-columns: 1fr 240px;
  grid-template-areas:
    'banner banner'
    'head head'
    'body rail';
  gap: 0;
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: 4px;
  overflow: hidden;
}

.paper-card-detail__banner {
  grid-area: banner;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
  padding: 14px 24px;
  background: var(--ember-tint);
  border-bottom: 1px solid var(--ember);
}

.paper-card-detail__banner-content {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.paper-card-detail__banner-eyebrow {
  color: var(--ember-ink, var(--ember));
}

.paper-card-detail__banner-text {
  margin: 0;
  font-size: 12.5px;
  color: var(--ember-ink, var(--ink));
}

.paper-card-detail__head {
  grid-area: head;
  padding: 22px 24px;
  border-bottom: 1px solid var(--line-soft);
  display: flex;
  align-items: flex-start;
  gap: 18px;
}

.paper-card-detail__head-text {
  flex: 1;
  min-width: 0;
}

.paper-card-detail__title {
  margin: 6px 0 0;
  font-family: var(--serif);
  font-size: 28px;
  font-weight: 500;
  color: var(--ink-deep);
  line-height: 1.15;
}

.paper-card-detail__close {
  background: transparent;
  border: 1px solid var(--line);
  border-radius: 4px;
  padding: 6px;
  cursor: pointer;
  color: var(--ink-2);
}

.paper-card-detail__close:hover {
  color: var(--ember);
  border-color: var(--ember);
}

.paper-card-detail__body {
  grid-area: body;
  padding: 22px 24px;
  border-right: 1px solid var(--line-soft);
}

.paper-card-detail__description {
  margin: 0 0 18px;
  font-size: 15px;
  line-height: 1.55;
  color: var(--ink);
  font-family: var(--sans);
  white-space: pre-wrap;
}

.paper-card-detail__section + .paper-card-detail__section {
  margin-top: 22px;
}

.paper-card-detail__section-eyebrow {
  margin-bottom: 8px;
}

.paper-card-detail__activity {
  padding: 0 0 6px;
}

.paper-card-detail__activity-row {
  display: grid;
  grid-template-columns: 70px 1fr 80px;
  align-items: baseline;
  padding: 5px 0;
  font-family: var(--mono);
  font-size: 11px;
  color: var(--mute);
}

.paper-card-detail__activity-serial {
  font-family: var(--mono);
}

.paper-card-detail__activity-text {
  color: var(--ink-2);
  font-family: var(--sans);
  font-size: 12.5px;
}

.paper-card-detail__activity-age {
  text-align: right;
  color: var(--faint);
}

.paper-card-detail__rail {
  grid-area: rail;
  padding: 22px 22px 22px 18px;
}

.paper-card-detail__meta {
  padding: 14px;
  border: 1px solid var(--line);
  border-radius: 3px;
  background: var(--paper-card);
}

.paper-card-detail__meta-row {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 6px 0;
  border-bottom: 1px solid var(--line-soft);
  font-size: 13px;
}

.paper-card-detail__meta-row:last-child {
  border-bottom: none;
}

.paper-card-detail__meta-value {
  font-family: var(--serif);
  font-style: italic;
  font-size: 13px;
  color: var(--ink-deep);
  text-align: right;
}

@media (max-width: 720px) {
  .paper-card-detail__sheet {
    grid-template-columns: 1fr;
    grid-template-areas:
      'banner'
      'head'
      'body'
      'rail';
  }
  .paper-card-detail__body {
    border-right: none;
    border-bottom: 1px solid var(--line-soft);
  }
}
</style>
