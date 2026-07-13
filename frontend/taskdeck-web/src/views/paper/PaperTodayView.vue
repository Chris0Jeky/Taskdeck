<script setup lang="ts">
import { computed } from 'vue'
import { formatLocalDossierDate, useTodayDossier } from '../../composables/useTodayDossier'
import { useSessionStore } from '../../store/sessionStore'
import { useToastStore } from '../../store/toastStore'
import { useWorkspaceStore } from '../../store/workspaceStore'

import TodayCover from './today/TodayCover.vue'
import TodayStats from './today/TodayStats.vue'
import TodayCadence from './today/TodayCadence.vue'
import TodayLedger from './today/TodayLedger.vue'
import TodayDecisions from './today/TodayDecisions.vue'
import TodayBoards from './today/TodayBoards.vue'
import TodayCarryOver from './today/TodayCarryOver.vue'
import TodayStreak from './today/TodayStreak.vue'
import TodayLineForTomorrow from './today/TodayLineForTomorrow.vue'

/**
 * PaperTodayView — orchestrator for the Paper "Today / End-of-day dossier".
 * Reads live Today summary, cadence, streak, seal, and tomorrow-note data from
 * `useTodayDossier`. Sections without a shipped query render explicit empty
 * states instead of inferred activity.
 *
 * The Obsidian `TodayView` continues to render when paper mode is off; the
 * `TodayView.vue` shell delegates to this component when `paperThemeStore
 * .isOn`.
 */
const { dossier, sealed, sealDay, saveLineForTomorrow } = useTodayDossier()
const session = useSessionStore()
const toast = useToastStore()
const workspace = useWorkspaceStore()

const ledgerSummary = computed(() => dossier.value.ledger.length > 0
  ? `Every meaningful event today · ${dossier.value.ledger.length} entries`
  : 'Live ledger unavailable')
const carryOverSummary = computed(() => {
  const total = workspace.todaySummary?.summary.overdueCards
  if (total === undefined) return 'Live carry-over unavailable'

  const visible = dossier.value.carryOver.length
  return total > visible
    ? `Showing ${visible} of ${total} live overdue cards`
    : `${total} live overdue card${total === 1 ? '' : 's'}`
})
const lineForTomorrowStorageKey = computed(() => {
  const userPart = encodeURIComponent(session.userId?.trim() || 'anonymous')
  const dayPart = formatLocalDossierDate(dossier.value.date)
  return `td.paper.line-for-tomorrow:${userPart}:${dayPart}`
})
const lineForTomorrowSaveDate = computed(() => formatLocalDossierDate(dossier.value.date))

async function onSeal() {
  const result = await sealDay()
  if (result.inProgress) {
    return
  }
  if (!result.sealed) {
    toast.error('Failed to seal the day. Please try again.')
    return
  }
  if (result.alreadySealed) {
    toast.info('Day is already sealed.')
    return
  }
  toast.success('Day sealed. The dossier is archived.')
}

function onWriteNote() {
  // Note-writing surface lives outside this slice; we surface a hint so
  // the affordance is discoverable.
  toast.info('Notes will land in tomorrow’s morning briefing.')
}

async function retryTodaySummary() {
  try {
    await workspace.fetchTodaySummary()
  } catch {
    // The workspace store owns todayError; leaving it set keeps this retry visible.
  }
}

</script>

<template>
  <div
    class="paper-today"
    data-paper-today
    :aria-busy="workspace.todayLoading ? 'true' : undefined"
  >
    <section
      v-if="workspace.todayLoading && !workspace.todaySummary"
      class="paper-today__load-state"
      data-testid="paper-today-loading"
      role="status"
      aria-live="polite"
    >
      <span class="tk-serial">TODAY · LIVE SUMMARY</span>
      <h1 class="tk-h2">Loading today’s dossier…</h1>
      <p>Checking Inbox, Review, and board deadlines before showing today’s totals.</p>
    </section>

    <template v-else>
      <section
        v-if="workspace.todayError && !workspace.todaySummary"
        class="paper-today__summary-state paper-today__summary-state--error"
        data-testid="paper-today-error"
        role="alert"
      >
        <div>
          <strong>Today’s live summary could not be loaded.</strong>
          <p>{{ workspace.todayError }} The independent dossier sections remain available below.</p>
        </div>
        <button
          type="button"
          class="paper-today__retry"
          :disabled="workspace.todayLoading"
          @click="retryTodaySummary"
        >
          Retry live summary
        </button>
      </section>
      <section
        v-else-if="workspace.todayLoading && workspace.todaySummary"
        class="paper-today__summary-state"
        data-testid="paper-today-refreshing"
        role="status"
        aria-live="polite"
      >
        Refreshing today’s live summary. Previously loaded data remains visible until it completes.
      </section>
      <section
        v-else-if="workspace.todayError && workspace.todaySummary"
        class="paper-today__summary-state paper-today__summary-state--error"
        data-testid="paper-today-stale"
        role="alert"
      >
        <div>
          <strong>Today’s live summary could not be refreshed.</strong>
          <p>Showing previously loaded data, which may be stale. {{ workspace.todayError }}</p>
        </div>
        <button
          type="button"
          class="paper-today__retry"
          :disabled="workspace.todayLoading"
          @click="retryTodaySummary"
        >
          Retry live summary
        </button>
      </section>

    <TodayCover
      :serial="dossier.serial"
      :cards-moved="dossier.headlineCardsMoved"
      :lede="dossier.lede"
      :auto-seals-in="dossier.autoSealsIn"
      :sealed="sealed"
      @seal="onSeal"
      @note="onWriteNote"
    />

    <TodayStats v-if="dossier.stats.length > 0" :stats="dossier.stats" />
    <p v-else class="paper-today__empty paper-today__empty--stats" data-empty-state="stats">
      Today's live totals are unavailable. Inbox and Review remain the source of truth.
    </p>

    <section class="paper-today__body">
      <div class="paper-today__col paper-today__col--left">
        <div class="card paper-today__card">
          <header class="paper-today__section-head">
            <span class="tk-serial paper-today__section-num">§ I</span>
            <h3 class="tk-h3 paper-today__section-title">Cadence</h3>
            <span class="tk-meta paper-today__section-sub">When you worked · 24h strip</span>
          </header>
          <TodayCadence v-if="dossier.cadenceAvailable" :cadence="dossier.cadence" />
          <p v-else class="paper-today__empty" data-empty-state="cadence">
            Cadence data is unavailable. No work pattern is being inferred.
          </p>
        </div>

        <div class="card paper-today__card paper-today__card--ledger">
          <header class="paper-today__section-head paper-today__section-head--inline">
            <span class="tk-serial paper-today__section-num">§ II</span>
            <h3 class="tk-h3 paper-today__section-title">Ledger</h3>
            <span class="tk-meta paper-today__section-sub">{{ ledgerSummary }}</span>
          </header>
          <TodayLedger v-if="dossier.ledger.length > 0" :entries="dossier.ledger" />
          <p v-else class="paper-today__empty paper-today__empty--inset" data-empty-state="ledger">
            A live day ledger is not available yet. No events are being invented.
          </p>
        </div>

        <div class="card paper-today__card">
          <header class="paper-today__section-head">
            <span class="tk-serial paper-today__section-num">§ III</span>
            <h3 class="tk-h3 paper-today__section-title">Decisions</h3>
            <span class="tk-meta paper-today__section-sub">Proposals you weighed today</span>
          </header>
          <TodayDecisions v-if="dossier.decisions.length > 0" :decisions="dossier.decisions" />
          <p v-else class="paper-today__empty" data-empty-state="decisions">
            Today's decisions are not summarized here yet. Open Review for live proposals.
          </p>
        </div>
      </div>

      <div class="paper-today__col paper-today__col--right">
        <div class="card paper-today__card">
          <header class="paper-today__section-head">
            <span class="tk-serial paper-today__section-num">§ IV</span>
            <h3 class="tk-h3 paper-today__section-title">Boards touched</h3>
            <span class="tk-meta paper-today__section-sub">Touch summary</span>
          </header>
          <TodayBoards v-if="dossier.boards.length > 0" :boards="dossier.boards" />
          <p v-else class="paper-today__empty" data-empty-state="boards">
            Board-touch history is not available yet. Open Boards for live state.
          </p>
        </div>

        <div class="card paper-today__card paper-today__card--carry">
          <header class="paper-today__section-head">
            <span class="tk-serial paper-today__section-num">§ V</span>
            <h3 class="tk-h3 paper-today__section-title">Carry-over</h3>
            <span class="tk-meta paper-today__section-sub">{{ carryOverSummary }}</span>
          </header>
          <TodayCarryOver v-if="dossier.carryOver.length > 0" :cards="dossier.carryOver" />
          <p v-else class="paper-today__empty" data-empty-state="carry-over">
            {{ dossier.stats.length > 0
              ? "No overdue cards in today's live summary."
              : 'Carry-over data is unavailable. Open Boards for live cards.' }}
          </p>
        </div>

        <div class="card paper-today__card paper-today__card--streak">
          <header class="paper-today__section-head">
            <span class="tk-serial paper-today__section-num">§ VI</span>
            <h3 class="tk-h3 paper-today__section-title">Streak</h3>
            <span class="tk-meta paper-today__section-sub">Days in a row · this quarter</span>
          </header>
          <TodayStreak v-if="dossier.streakAvailable" :streak="dossier.streak" />
          <p v-else class="paper-today__empty" data-empty-state="streak">
            Streak data is unavailable. No activity history is being inferred.
          </p>
        </div>

        <div class="card paper-today__card">
          <header class="paper-today__section-head">
            <span class="tk-serial paper-today__section-num">§ VII</span>
            <h3 class="tk-h3 paper-today__section-title">A line for tomorrow</h3>
            <span class="tk-meta paper-today__section-sub">A note your tomorrow-self will see at first open</span>
          </header>
          <TodayLineForTomorrow
            :initial="dossier.lineForTomorrow"
            :storage-key="lineForTomorrowStorageKey"
            :save-date="lineForTomorrowSaveDate"
            :save="saveLineForTomorrow"
            :use-stored-draft="false"
          />
        </div>
      </div>
    </section>

    <footer class="paper-today__footer">
      <span class="tk-serial">DOSSIER · {{ dossier.serial }} · YEAR LEDGER</span>
      <span class="tk-serial">SEAL ABOVE · LEDGER IN § II</span>
    </footer>
    </template>
  </div>
</template>

<style scoped>
.paper-today {
  display: flex;
  flex-direction: column;
  background: var(--paper);
  color: var(--ink);
  min-height: 100%;
}

.paper-today__body {
  padding: 20px 56px 0;
  display: grid;
  grid-template-columns: 1.5fr 1fr;
  gap: 28px;
}
.paper-today__col {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.paper-today__card {
  padding: 22px;
}
.paper-today__card--ledger {
  padding: 0;
  overflow: hidden;
}
.paper-today__card--carry {
  border-color: var(--overdue);
  border-left: 2px solid var(--overdue);
}
.paper-today__card--streak {
  background: var(--paper-2);
}

.paper-today__load-state {
  display: grid;
  gap: 12px;
  margin: 56px;
  padding: 36px;
  border: 1px solid var(--line);
  background: var(--paper-card);
}
.paper-today__load-state h1,
.paper-today__load-state p,
.paper-today__summary-state p {
  margin: 0;
}
.paper-today__summary-state {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  margin: 24px 56px 0;
  padding: 14px 18px;
  border: 1px solid var(--line);
  background: var(--paper-2);
  color: var(--ink-2);
  font-size: 13px;
  line-height: 1.5;
}
.paper-today__summary-state--error {
  border-left: 3px solid var(--overdue);
}
.paper-today__summary-state strong {
  color: var(--ink);
}
.paper-today__retry {
  flex: 0 0 auto;
  padding: 8px 12px;
  border: 1px solid var(--line);
  background: var(--paper-card);
  color: var(--ink);
  font: inherit;
  font-weight: 700;
  cursor: pointer;
}
.paper-today__retry:hover {
  border-color: var(--ink-2);
}
.paper-today__retry:focus-visible {
  outline: 2px solid var(--ink);
  outline-offset: 2px;
}
.paper-today__retry:disabled {
  cursor: wait;
  opacity: 0.65;
}

.paper-today__empty {
  margin: 8px 0 0;
  color: var(--ink-2);
  font-size: 13px;
  line-height: 1.5;
}
.paper-today__empty--stats {
  margin: 28px 56px 12px;
  padding: 18px;
  border: 1px solid var(--line-soft);
  background: var(--paper-card);
}
.paper-today__empty--inset {
  margin: 0;
  padding: 18px 22px;
}

.paper-today__section-head {
  display: flex;
  align-items: baseline;
  gap: 14px;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-today__card--ledger .paper-today__section-head--inline {
  margin-bottom: 0;
  padding: 16px 22px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-today__section-num {
  color: var(--faint);
}
.paper-today__section-title {
  margin: 0;
  font-size: 17px;
}
.paper-today__section-sub {
  margin-left: auto;
}

.paper-today__footer {
  padding: 30px 56px 20px;
  margin-top: 36px;
  border-top: 1px solid var(--line);
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
}

@media (max-width: 1100px) {
  .paper-today__load-state {
    margin: 24px;
  }
  .paper-today__summary-state {
    align-items: flex-start;
    flex-direction: column;
    margin: 20px 24px 0;
  }
  .paper-today__body {
    grid-template-columns: 1fr;
    padding: 20px 24px 0;
  }
  .paper-today__empty--stats {
    margin: 24px 24px 12px;
  }
  .paper-today__footer {
    padding: 24px 24px 16px;
  }
}
</style>
