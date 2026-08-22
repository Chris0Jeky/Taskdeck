<script setup lang="ts">
import { computed, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PaperCard from '../../components/paper/PaperCard.vue'
import PaperEmptyState from '../../components/paper/PaperEmptyState.vue'
import PaperKbd from '../../components/paper/PaperKbd.vue'
import PaperTagstamp from '../../components/paper/PaperTagstamp.vue'
import WorkspaceSetupModal from '../../components/workspace/WorkspaceSetupModal.vue'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { useCaptureStore } from '../../store/captureStore'

/**
 * PaperHomeView — morning-reset surface in the Paper & Graphite, Ember Edition skin.
 *
 * Provides:
 *   • Serif italic greeting whose period (morning / afternoon / evening) is
 *     derived from the local clock at render time.
 *   • A `tk-lede` subtitle summarising today's queue.
 *   • Up to three "queued for you" cards (proposals first — ember-accented —
 *     followed by captures awaiting triage, hairline only).
 *
 *   • A single-line quick capture row that wires through the existing
 *     captureStore so we don't duplicate dispatch logic.
 *
 * Data is read from the existing `workspaceStore.homeSummary` cache —
 * AppShell prefetches it on mount, so we do not refetch here unless the
 * cache is empty.
 *
 * Day-boundary contract (issue #1768): the Home workload counters are pure
 * STATUS counts — `capturesNeedingTriage` is `NewCount + FailedCount` in
 * `WorkspaceService.GetHomeAsync`, with no date predicate anywhere in the
 * chain. This surface must therefore never describe them as belonging to a
 * particular day ("from yesterday", "carry-over", "overnight"): a capture
 * saved seconds ago is `New` and would be mislabelled in every timezone.
 * Copy here stays date-neutral unless the payload grows a real timestamp.
 * That constraint now applies to the `home.ts` catalog of EVERY locale under
 * `src/locales`, not just to this file — each one restates it in its header
 * (ADR-0054).
 */

interface QueueCardModel {
  serial: string
  title: string
  meta: string
  tagLabel: string
  tagTone: 'ember' | 'mute'
  isProposal: boolean
}

const { t } = useI18n()
const router = useRouter()
const session = useSessionStore()
const workspace = useWorkspaceStore()
const capture = useCaptureStore()

const summary = computed(() => workspace.homeSummary)
const onboarding = computed(() => summary.value?.onboarding ?? workspace.onboarding)

// ── Greeting ─────────────────────────────────────────────────────────────

/**
 * Pull a first name from the session username. We accept anything that
 * looks like a single token (alphabetic / hyphen / apostrophe) and capitalise
 * the first letter; anything that looks like an email / handle is ignored.
 */
function resolveFirstName(username: string | null | undefined): string | null {
  if (!username) return null
  const trimmed = username.trim()
  if (!trimmed) return null
  // Strip a trailing email portion if present.
  const beforeAt = trimmed.split('@')[0]
  if (!beforeAt) return null
  // Take the first whitespace- or dot-delimited token.
  const token = beforeAt.split(/[\s._-]+/).find((part) => /^[\p{L}][\p{L}'-]*$/u.test(part))
  if (!token) return null
  return token.toLocaleLowerCase().replace(/^\p{L}/u, (first) => first.toLocaleUpperCase())
}

type Period = 'morning' | 'afternoon' | 'evening'

function periodFor(hour: number): Period {
  if (hour < 12) return 'morning'
  if (hour < 17) return 'afternoon'
  return 'evening'
}

// Static key maps, not `t(\`home.greeting.${period}\`)`. Message keys assembled
// at runtime are invisible to grep and to any future key-usage lint. A typo in a
// static key here is NOT caught by the catalog guard either — that guard only
// checks the en/it/es catalogs against each other, not view references against
// the catalogs, so a key absent from all three still renders its raw path with
// no warning (fallback warnings are off by design). What the static literals buy
// is greppability: they keep every referenced key visible so a future key-usage
// lint can cross-check them against the catalogs, which a runtime-assembled key
// would defeat.
const GREETING_KEYS: Record<Period, string> = {
  morning: 'home.greeting.morning',
  afternoon: 'home.greeting.afternoon',
  evening: 'home.greeting.evening',
}

const PERIOD_KEYS: Record<Period, string> = {
  morning: 'home.period.morning',
  afternoon: 'home.period.afternoon',
  evening: 'home.period.evening',
}

const currentHour = ref(new Date().getHours())

const greeting = computed(() => {
  const period = periodFor(currentHour.value)
  const name = resolveFirstName(session.username)
  // Keyed per period rather than composed from "Good {period}": the greeting is
  // one fixed expression in most languages (Buongiorno, Buenas tardes) and does
  // not survive being assembled from parts.
  const opener = name ? t(GREETING_KEYS[period]) : t('home.greeting.anonymous')
  return { opener, name, period, periodLabel: t(PERIOD_KEYS[period]) }
})

// ── Lede / queue summary ─────────────────────────────────────────────────

const proposalsAwaiting = computed(() => summary.value?.workload.proposalsPendingReview ?? 0)
// Status count, not a dated bucket — see the day-boundary contract above.
const capturesAwaitingTriage = computed(() => summary.value?.workload.capturesNeedingTriage ?? 0)
const showLoadingState = computed(() => workspace.homeLoading && !summary.value)
const showErrorState = computed(() => Boolean(workspace.homeError))

const ledeText = computed(() => {
  if (showErrorState.value) {
    return workspace.homeError ?? t('home.error')
  }
  if (showLoadingState.value || !summary.value) {
    return t('home.loading')
  }
  const p = proposalsAwaiting.value
  const c = capturesAwaitingTriage.value
  if (p === 0 && c === 0) {
    return ''
  }
  const parts: string[] = []
  if (p > 0) {
    parts.push(t('home.lede.awaitingReview', { count: p }))
  }
  if (c > 0) {
    parts.push(t('home.lede.awaitingTriage', { count: c }))
  }
  // The separator is punctuation, not copy — no locale changes a middot.
  return parts.join(' · ')
})

// ── Queue cards ──────────────────────────────────────────────────────────

const queueCards = computed<QueueCardModel[]>(() => {
  const s = summary.value
  if (!s) return []

  const cards: QueueCardModel[] = []

  // Proposals — ember-accented.
  s.recommendedActions
    .filter((action) => action.targetSurface === 'review' || action.actionId === 'review-proposals')
    .slice(0, 3)
    .forEach((action, index) => {
      cards.push({
        serial: `#${String(index + 1).padStart(3, '0')}`,
        // action.title / action.description are SERVER-supplied strings and are
        // not catalog keys — the backend does not know the client's locale.
        // Localizing them is a separate, backend-shaped slice.
        title: action.title,
        meta: action.description,
        tagLabel: t('home.queue.tagProposed'),
        tagTone: 'ember',
        isProposal: true,
      })
    })

  // Captures awaiting triage — hairline only, no ember. Date-neutral copy.
  if (cards.length < 3 && s.workload.capturesNeedingTriage > 0) {
    const remaining = 3 - cards.length
    const triageCount = Math.min(remaining, s.workload.capturesNeedingTriage)
    for (let i = 0; i < triageCount; i += 1) {
      cards.push({
        serial: `#${String(cards.length + 1).padStart(3, '0')}`,
        title: i === 0
          ? t(
              'home.queue.triageCard',
              { count: s.workload.capturesNeedingTriage },
              s.workload.capturesNeedingTriage,
            )
          : t('home.queue.triageCardMore'),
        meta: t('home.queue.triageMeta'),
        tagLabel: t('home.queue.tagTriage'),
        tagTone: 'mute',
        isProposal: false,
      })
    }
  }

  return cards
})

const showFirstBoardSetup = computed(() => summary.value?.boards.totalBoards === 0)
const showEmptyState = computed(() =>
  summary.value !== null && queueCards.value.length === 0 && !showFirstBoardSetup.value,
)
const showSetupModal = ref(false)

// ── Milestones (#1936) ───────────────────────────────────────────────────
//
// The first-loop block is first-run onboarding, so it stays prominent while
// there is anything left to do. Once every step is ticked it has no remaining
// function, and leaving the largest element on the page as a finished tutorial
// is what made Home read as a walkthrough instead of a dashboard. So:
//
//   • incomplete            → full block, unchanged (prominent onboarding);
//   • complete              → auto-collapsed to a single line, expandable for
//                             the session and dismissible for good;
//   • visibility dismissed  → not rendered at all.
//
// Dismissal is NOT a new client-side preference: it reuses the existing
// server-persisted workspace onboarding visibility (`updateOnboarding`, the
// same call Legacy Home's Dismiss makes), so it survives reload, a new device,
// and both skins. Honouring `visibility` here also fixes Paper ignoring a
// dismissal made from the Legacy Home or Today surfaces.

const milestoneSteps = computed(() => onboarding.value?.steps ?? [])
const totalMilestones = computed(() => milestoneSteps.value.length)
const completedMilestones = computed(
  () => milestoneSteps.value.filter((step) => step.isComplete).length,
)

// Complete when the server says so OR when every step is ticked — the two
// agree in practice, and the derived count keeps the collapse honest if a
// payload's flag ever lags its steps.
const milestonesComplete = computed(
  () =>
    totalMilestones.value > 0 &&
    (onboarding.value?.isComplete === true ||
      completedMilestones.value === totalMilestones.value),
)

const milestonesDismissed = computed(() => onboarding.value?.visibility === 'dismissed')
const showMilestones = computed(() => totalMilestones.value > 0 && !milestonesDismissed.value)

// Session-scoped override of the auto-collapse. Deliberately not persisted:
// collapsed is the right default on every visit once the loop is done, and an
// expand is a one-off "let me look again", not a preference.
const milestonesExpandedByUser = ref(false)
const milestonesExpanded = computed(
  () => !milestonesComplete.value || milestonesExpandedByUser.value,
)
const milestonesDismissBusy = ref(false)

function toggleMilestones() {
  milestonesExpandedByUser.value = !milestonesExpandedByUser.value
}

async function dismissMilestones() {
  if (milestonesDismissBusy.value) return
  milestonesDismissBusy.value = true
  try {
    await workspace.updateOnboarding('dismiss')
  } catch {
    // The store applies the dismissal optimistically, keeps it flagged unsaved
    // and raises its own warning toast; nothing to add here.
  } finally {
    milestonesDismissBusy.value = false
  }
}

function openSetupModal() {
  showSetupModal.value = true
}

function closeSetupModal() {
  showSetupModal.value = false
}

function handleSetupCreated() {
  showSetupModal.value = false
}

// ── Quick capture ────────────────────────────────────────────────────────

const captureText = ref('')
const captureBusy = ref(false)
const captureInputRef = ref<HTMLInputElement | null>(null)

async function submitCapture() {
  const text = captureText.value.trim()
  if (!text) {
    // Per spec: do not dispatch on empty Enter.
    return
  }
  if (captureBusy.value) return
  captureBusy.value = true
  try {
    // `refreshWorkload: false` — the full summary fetched below is a superset
    // of the store's workload-only badge refresh, and both hit the same
    // /workspace/home endpoint, so letting the store notify too would double
    // -fetch the heaviest read on this surface for every quick capture
    // (GH-1974). Home needs the FULL summary, not just the counters: a capture
    // also ticks the `capture-first-item` milestone and moves the recommended
    // actions, so this fetch stays.
    await capture.createItem({ boardId: null, text, source: 'Typed' }, { refreshWorkload: false })
    captureText.value = ''
    await workspace.fetchHomeSummary().catch(() => {
      // Home summary errors are reflected via workspace.homeError.
    })
    await nextTick()
    captureInputRef.value?.focus()
  } catch {
    // captureStore already surfaces a toast; keep the typed text so the
    // user can retry without re-typing.
  } finally {
    captureBusy.value = false
  }
}

function onCaptureShortcut(event: KeyboardEvent) {
  // Cmd/Ctrl + ; — jump focus into the quick capture row.
  if ((event.metaKey || event.ctrlKey) && event.key === ';') {
    event.preventDefault()
    captureInputRef.value?.focus()
  }
}

// ── Lifecycle ────────────────────────────────────────────────────────────

function maybeFetchHomeSummary() {
  if (workspace.homeLoading) return
  if (workspace.hasHomeSummary) return
  void workspace.fetchHomeSummary().catch(() => {
    // Errors are reflected via workspace.homeError; we render gracefully.
  })
}

let shortcutListening = false
let greetingTimer: ReturnType<typeof window.setInterval> | null = null

function refreshCurrentHour() {
  currentHour.value = new Date().getHours()
}

function startActiveHomeHandlers() {
  refreshCurrentHour()
  if (!shortcutListening) {
    window.addEventListener('keydown', onCaptureShortcut)
    shortcutListening = true
  }
  if (!greetingTimer) {
    greetingTimer = window.setInterval(refreshCurrentHour, 60_000)
  }
}

function stopActiveHomeHandlers() {
  if (shortcutListening) {
    window.removeEventListener('keydown', onCaptureShortcut)
    shortcutListening = false
  }
  if (greetingTimer) {
    window.clearInterval(greetingTimer)
    greetingTimer = null
  }
}

onMounted(() => {
  startActiveHomeHandlers()
  maybeFetchHomeSummary()
})

onActivated(() => {
  startActiveHomeHandlers()
  maybeFetchHomeSummary()
})

onDeactivated(stopActiveHomeHandlers)

onBeforeUnmount(stopActiveHomeHandlers)

// ── Affordance: open Review when a proposal card is activated ────────────

function openCard(card: QueueCardModel) {
  if (card.isProposal) {
    void router.push('/workspace/review')
  } else {
    void router.push('/workspace/inbox')
  }
}

function onCardKeydown(event: KeyboardEvent, card: QueueCardModel) {
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    openCard(card)
  }
}
</script>

<template>
  <div class="paper-home" data-testid="paper-home">
    <header class="paper-home__hero">
      <p class="tk-eyebrow paper-home__eyebrow" data-testid="paper-home-period">
        {{ $t('home.eyebrow', { period: greeting.periodLabel }) }}
      </p>
      <h1 class="tk-h1 paper-home__greeting" data-testid="paper-home-greeting">
        <template v-if="greeting.name">
          {{ greeting.opener }}, <em>{{ greeting.name }}.</em>
        </template>
        <template v-else>
          {{ greeting.opener }}.
        </template>
      </h1>
      <p v-if="ledeText" class="tk-lede paper-home__lede" data-testid="paper-home-lede">
        {{ ledeText }}
      </p>
    </header>

    <section
      v-if="showLoadingState"
      class="paper-home__loading"
      data-testid="paper-home-loading"
      aria-live="polite"
      role="status"
    >
      <PaperCard variant="flat">
        <p class="tk-meta paper-home__state-text">
          {{ $t('home.loading') }}
        </p>
      </PaperCard>
    </section>

    <section
      v-else-if="showErrorState"
      class="paper-home__error"
      data-testid="paper-home-error"
      role="alert"
    >
      <PaperCard variant="flat">
        <p class="tk-meta paper-home__state-text">
          {{ workspace.homeError }}
        </p>
      </PaperCard>
    </section>

    <section
      v-else-if="showFirstBoardSetup"
      class="paper-home__first-board"
      data-testid="paper-home-first-board"
    >
      <PaperEmptyState tone="ember" mark="✦">
        <template #title>{{ $t('home.firstBoard.title') }}</template>
        {{ $t('home.firstBoard.body') }}
        <template #cta>
          <button
            type="button"
            class="paper-home__setup-button"
            data-testid="paper-home-setup-cta"
            @click="openSetupModal"
          >
            {{ $t('home.firstBoard.cta') }}
          </button>
        </template>
      </PaperEmptyState>
    </section>

    <section
      v-else-if="showEmptyState"
      class="paper-home__empty"
      data-testid="paper-home-empty"
    >
      <PaperCard variant="flat">
        <p class="tk-meta paper-home__empty-text">
          {{ $t('home.empty') }}
        </p>
      </PaperCard>
    </section>

    <section v-else class="paper-home__queue" :aria-label="$t('home.queue.label')">
      <h2 class="tk-eyebrow paper-home__queue-title">{{ $t('home.queue.title') }}</h2>
      <div class="paper-home__queue-grid">
        <!--
          `carryover` here is a legacy structural id meaning "not a proposal";
          it carries no date claim. User-visible copy stays date-neutral (#1768).
        -->
        <PaperCard
          v-for="card in queueCards"
          :key="card.serial"
          variant="lift"
          :halo="card.isProposal"
          class="paper-home__card"
          :data-testid="card.isProposal ? 'paper-home-card-proposal' : 'paper-home-card-carryover'"
          :data-card-kind="card.isProposal ? 'proposal' : 'carryover'"
        >
          <button
            type="button"
            class="paper-home__card-button"
            @click="openCard(card)"
            @keydown="(event) => onCardKeydown(event, card)"
          >
            <span class="tk-serial paper-home__card-serial">{{ card.serial }}</span>
            <span class="paper-home__card-title">{{ card.title }}</span>
            <span class="tk-meta paper-home__card-meta">{{ card.meta }}</span>
            <span class="paper-home__card-tag">
              <PaperTagstamp :tone="card.tagTone">{{ card.tagLabel }}</PaperTagstamp>
            </span>
          </button>
        </PaperCard>
      </div>
    </section>

    <section
      v-if="showMilestones"
      :class="['paper-home__milestones', { 'paper-home__milestones--collapsed': !milestonesExpanded }]"
      aria-labelledby="paper-home-milestones-title"
      data-testid="paper-home-milestones"
      :data-milestones-state="milestonesExpanded ? 'expanded' : 'collapsed'"
    >
      <div class="paper-home__milestones-heading">
        <div>
          <p v-if="milestonesExpanded" class="tk-eyebrow">{{ $t('home.milestones.eyebrow') }}</p>
          <h2 id="paper-home-milestones-title" class="paper-home__milestones-title">
            {{ milestonesComplete ? $t('home.milestones.completeTitle') : $t('home.milestones.title') }}
          </h2>
        </div>
        <div class="paper-home__milestones-controls">
          <span class="tk-meta" data-testid="paper-home-milestones-progress">
            {{ $t('home.milestones.progress', { completed: completedMilestones, total: totalMilestones }) }}
          </span>
          <!--
            Offered only once the loop is done: while it is unfinished the
            block is real onboarding and stays whole (#1936).
          -->
          <template v-if="milestonesComplete">
            <button
              type="button"
              class="paper-home__milestones-control"
              :aria-expanded="milestonesExpanded"
              aria-controls="paper-home-milestones-body"
              data-testid="paper-home-milestones-toggle"
              @click="toggleMilestones"
            >
              {{ milestonesExpanded ? $t('home.milestones.collapse') : $t('home.milestones.expand') }}
            </button>
            <button
              type="button"
              class="paper-home__milestones-control"
              :disabled="milestonesDismissBusy"
              data-testid="paper-home-milestones-dismiss"
              @click="dismissMilestones"
            >
              {{ $t('home.milestones.dismiss') }}
            </button>
          </template>
        </div>
      </div>
      <div v-if="milestonesExpanded" id="paper-home-milestones-body">
        <ol class="paper-home__milestone-list">
          <li
            v-for="step in milestoneSteps"
            :key="step.stepId"
            :class="['paper-home__milestone', { 'paper-home__milestone--complete': step.isComplete }]"
          >
            <span class="paper-home__milestone-mark" aria-hidden="true">
              {{ step.isComplete ? '✓' : '○' }}
            </span>
            <span>
              <strong>{{ step.title }}</strong>
              <small>{{ step.description }}</small>
            </span>
            <span class="sr-only">
              {{ step.isComplete ? $t('home.milestones.stepComplete') : $t('home.milestones.stepIncomplete') }}
            </span>
          </li>
        </ol>
        <p class="tk-meta paper-home__milestones-note">
          {{ $t('home.milestones.note') }}
        </p>
      </div>
    </section>

    <section class="paper-home__capture" :aria-label="$t('home.capture.label')">
      <form class="paper-home__capture-row" @submit.prevent="submitCapture">
        <label class="sr-only" for="paper-home-capture-input">{{ $t('home.capture.inputLabel') }}</label>
        <input
          id="paper-home-capture-input"
          ref="captureInputRef"
          v-model="captureText"
          class="paper-home__capture-input"
          type="text"
          :placeholder="$t('home.capture.placeholder')"
          autocomplete="off"
          :disabled="captureBusy"
          data-testid="paper-home-capture-input"
        />
        <span class="paper-home__capture-hint">
          <PaperKbd>⌘</PaperKbd>
          <PaperKbd>;</PaperKbd>
        </span>
      </form>
    </section>

    <!--
      Keep the modal MOUNTED and toggle `is-open` so its non-immediate
      watch(isOpen) observes the false→true transition and registers the global
      Escape handler (registerEscapeHandler). Mounting only-when-open with
      :is-open="true" skips that transition, leaving Escape unwired.
    -->
    <Teleport to="body">
      <WorkspaceSetupModal
        :is-open="showSetupModal"
        @close="closeSetupModal"
        @created="handleSetupCreated"
      />
    </Teleport>
  </div>
</template>

<style scoped>
.paper-home {
  display: flex;
  flex-direction: column;
  gap: 32px;
  padding: 32px 40px 56px;
  font-family: var(--sans);
  color: var(--ink);
  min-height: 100%;
}

.paper-home__hero {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.paper-home__eyebrow {
  text-transform: capitalize;
}

.paper-home__greeting {
  margin: 8px 0 4px;
  font-family: var(--serif);
  font-style: normal;
}

.paper-home__lede {
  margin: 0;
}

.paper-home__queue-title {
  margin: 0 0 14px;
}

.paper-home__queue-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.paper-home__card {
  padding: 0;
}

.paper-home__card-button {
  display: grid;
  grid-template-columns: auto 1fr auto;
  grid-template-rows: auto auto;
  column-gap: 12px;
  row-gap: 6px;
  width: 100%;
  padding: 18px;
  background: transparent;
  border: none;
  text-align: left;
  cursor: pointer;
  font-family: inherit;
  color: inherit;
}

.paper-home__card-button:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 2px;
}

.paper-home__card-serial {
  grid-row: 1;
  grid-column: 1;
  color: var(--faint);
  font-family: var(--mono);
}

.paper-home__card-title {
  grid-row: 1;
  grid-column: 2;
  font-family: var(--serif);
  font-size: 17px;
  line-height: 1.3;
  color: var(--ink-deep);
}

.paper-home__card-tag {
  grid-row: 1;
  grid-column: 3;
  align-self: start;
}

.paper-home__card-meta {
  grid-row: 2;
  grid-column: 2 / 4;
  font-family: var(--mono);
  font-size: 11px;
}

.paper-home__capture-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  border: 0;
  border-top: 1px solid var(--line);
}

.paper-home__capture-input {
  flex: 1;
  border: none;
  background: transparent;
  outline: none;
  font-family: var(--serif);
  font-style: italic;
  font-size: 16px;
  color: var(--ink);
}

.paper-home__capture-input::placeholder {
  font-family: var(--serif);
  font-style: italic;
  color: var(--mute);
}

.paper-home__capture-input:disabled {
  opacity: 0.6;
  cursor: progress;
}

.paper-home__capture-hint {
  display: inline-flex;
  gap: 4px;
  color: var(--mute);
}

.paper-home__empty-text,
.paper-home__state-text {
  padding: 18px;
  margin: 0;
  font-style: italic;
}

.paper-home__setup-button {
  padding: 9px 14px;
  border: 1px solid var(--ember-deep);
  border-radius: var(--r-1);
  background: var(--ember);
  /* Paper-aware on-ember text (>=4.5:1 on the ember CTA, base + hover). */
  color: var(--td-on-ember, var(--td-text-inverse));
  font-family: var(--sans);
  font-size: 13px;
  font-weight: 700;
  cursor: pointer;
  box-shadow: var(--shadow-stamp);
}

.paper-home__setup-button:hover {
  background: var(--ember-deep);
}

.paper-home__setup-button:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 3px;
}

.paper-home__milestones {
  padding: 20px;
  border: 1px solid var(--line);
  background: var(--paper-card);
  box-shadow: var(--shadow-card);
}

/*
  Collapsed (#1936): a finished loop is a one-line receipt, not a panel. Drop
  the card weight so it reads as a footnote under the live content above it.
*/
.paper-home__milestones--collapsed {
  padding: 10px 14px;
  background: transparent;
  box-shadow: none;
}

.paper-home__milestones-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 14px;
}

.paper-home__milestones--collapsed .paper-home__milestones-heading {
  align-items: center;
  margin-bottom: 0;
}

.paper-home__milestones-heading p {
  margin: 0 0 4px;
}

.paper-home__milestones-title {
  margin: 0;
  font-family: var(--serif);
  font-size: 20px;
  font-weight: 500;
  color: var(--ink-deep);
}

.paper-home__milestones--collapsed .paper-home__milestones-title {
  font-size: 15px;
  color: var(--ink-2);
}

.paper-home__milestones-controls {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.paper-home__milestones-control {
  padding: 4px 8px;
  border: 1px solid var(--line);
  border-radius: var(--r-1);
  background: transparent;
  color: var(--ink-2);
  font-family: var(--sans);
  font-size: 11px;
  cursor: pointer;
}

.paper-home__milestones-control:hover:not(:disabled) {
  border-color: var(--ember);
  color: var(--ink-deep);
}

.paper-home__milestones-control:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 2px;
}

.paper-home__milestones-control:disabled {
  opacity: 0.6;
  cursor: progress;
}

.paper-home__milestone-list {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
  padding: 0;
  margin: 0;
  list-style: none;
}

.paper-home__milestone {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--line-soft);
  color: var(--ink-2);
}

.paper-home__milestone strong,
.paper-home__milestone small {
  display: block;
}

.paper-home__milestone strong {
  color: var(--ink-deep);
  font-family: var(--serif);
  font-size: 14px;
}

.paper-home__milestone small {
  margin-top: 3px;
  color: var(--mute);
  font-size: 11px;
  line-height: 1.4;
}

.paper-home__milestone-mark {
  color: var(--faint);
  font-family: var(--mono);
}

.paper-home__milestone--complete {
  border-color: var(--applied);
  background: var(--applied-tint);
}

.paper-home__milestone--complete .paper-home__milestone-mark {
  color: var(--applied);
}

.paper-home__milestones-note {
  margin: 12px 0 0;
}

@media (max-width: 1024px) {
  .paper-home__queue-grid {
    grid-template-columns: 1fr;
  }

  .paper-home__milestone-list {
    grid-template-columns: 1fr;
  }
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
