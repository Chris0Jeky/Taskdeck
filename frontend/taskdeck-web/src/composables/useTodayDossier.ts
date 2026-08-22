import { computed, onScopeDispose, ref, watch, type Ref } from 'vue'
import { useWorkspaceStore } from '../store/workspaceStore'
import { todayApi, type CadenceApiResponse, type StreakApiResponse } from '../api/todayApi'
import { logError } from '../utils/errorReporting'
import type { TodaySummary } from '../types/workspace'

export type DossierLedgerWho = 'you' | 'system'
export type DossierLedgerTone = 'ember' | 'applied' | 'active' | 'passive' | 'mute'

export interface DossierLedgerEntry {
  serial: string
  time: string
  who: DossierLedgerWho
  what: string
  tone: DossierLedgerTone
}

export type DossierDecisionVerdict = 'applied' | 'rejected' | 'deferred'

export interface DossierDecision {
  serial: string
  title: string
  verdict: DossierDecisionVerdict
  confidence: number
  when: string
  stale?: boolean
}

export interface DossierBoardLine {
  id: string
  name: string
  moves: number
  proposals: number
}

export interface DossierCarryOverCard {
  serial: string
  title: string
  age: string
  reason: string
}

export interface DossierStatCard {
  id: 'captures-needing-triage' | 'proposals-pending-review' | 'overdue' | 'due-today' | 'blocked'
  value: number
  numeric: true
  label: string
  sub: string
  tone: 'ink' | 'ember' | 'applied' | 'overdue'
}

export interface DossierCadence {
  weights: number[]
  peakHourIndex: number | null
  firstAction: string
  peakAction: string
  lastAction: string
}

export interface DossierStreak {
  cells: number[]
  todayIndex: number
  totalDays: number
  longestThisYear: number
}

export interface DossierData {
  serial: string
  date: Date
  headlineCardsMoved: number | null
  lede: string
  autoSealsIn: string | null
  stats: DossierStatCard[]
  cadence: DossierCadence
  cadenceAvailable: boolean
  ledger: DossierLedgerEntry[]
  decisions: DossierDecision[]
  boards: DossierBoardLine[]
  carryOver: DossierCarryOverCard[]
  streak: DossierStreak
  streakAvailable: boolean
  lineForTomorrow: string
}

const DOSSIER_NUMBER_RE = /D-\d{4}-\d{2}-\d{2}-\d{3}/

export function formatDossierSerial(date: Date, seq = 1): string {
  const { yyyy, mm, dd } = formatLocalDossierDateParts(date)
  const nnn = seq.toString().padStart(3, '0')
  const serial = `D-${yyyy}-${mm}-${dd}-${nnn}`
  if (!DOSSIER_NUMBER_RE.test(serial)) {
    throw new Error(`Invalid dossier serial: ${serial}`)
  }
  return serial
}

export function formatLocalDossierDate(date: Date): string {
  const { yyyy, mm, dd } = formatLocalDossierDateParts(date)
  return `${yyyy}-${mm}-${dd}`
}

function formatLocalDossierDateParts(date: Date): { yyyy: string; mm: string; dd: string } {
  return {
    yyyy: date.getFullYear().toString().padStart(4, '0'),
    mm: (date.getMonth() + 1).toString().padStart(2, '0'),
    dd: date.getDate().toString().padStart(2, '0'),
  }
}

function formatUtcTime(iso: string | null): string {
  if (!iso) return '--:--'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '--:--'
  return `${date.getUTCHours().toString().padStart(2, '0')}:${date.getUTCMinutes().toString().padStart(2, '0')}`
}

function formatCarryOverDueDate(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return 'overdue'
  return `due ${new Intl.DateTimeFormat('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(date)}`
}

function mapCadenceResponse(response: CadenceApiResponse): DossierCadence {
  const weights = Array.from({ length: 24 }, (_, hour) => {
    const bucket = response.buckets.find(candidate => candidate.hour === hour)
    return bucket?.eventCount ?? 0
  })
  const peakHourIndex = response.peakHour
  const peakEvents = peakHourIndex != null ? (weights[peakHourIndex] ?? 0) : 0

  return {
    weights,
    peakHourIndex,
    firstAction: `${formatUtcTime(response.firstActionAt)} UTC · first action`,
    peakAction: peakHourIndex != null
      ? `${peakHourIndex.toString().padStart(2, '0')}:00-${((peakHourIndex + 1) % 24).toString().padStart(2, '0')}:00 UTC · ${peakEvents} events`
      : 'no peak',
    lastAction: `${formatUtcTime(response.lastActionAt)} UTC · last action`,
  }
}

function mapStreakResponse(response: StreakApiResponse): DossierStreak {
  const cells = response.days.map(day => day.intensityBucket)
  return {
    cells,
    todayIndex: Math.max(0, cells.length - 1),
    totalDays: response.currentStreakLength,
    longestThisYear: response.longestStreakLength,
  }
}

function buildHonestDossier(now: Date, summary: TodaySummary | null): DossierData {
  const stats: DossierStatCard[] = summary
    ? [
        {
          id: 'captures-needing-triage',
          value: summary.summary.capturesNeedingTriage,
          numeric: true,
          label: 'captures to triage',
          sub: 'waiting in Inbox',
          tone: 'ink',
        },
        {
          id: 'proposals-pending-review',
          value: summary.summary.proposalsPendingReview,
          numeric: true,
          label: 'proposals to review',
          sub: 'waiting for your decision',
          tone: 'ember',
        },
        {
          id: 'overdue',
          value: summary.summary.overdueCards,
          numeric: true,
          label: 'overdue',
          sub: 'need attention',
          tone: 'overdue',
        },
        {
          id: 'due-today',
          value: summary.summary.dueTodayCards,
          numeric: true,
          label: 'due today',
          sub: 'scheduled for today',
          tone: 'applied',
        },
        {
          id: 'blocked',
          value: summary.summary.blockedCards,
          numeric: true,
          label: 'blocked',
          sub: 'currently blocked',
          tone: 'overdue',
        },
      ]
    : []

  const carryOver: DossierCarryOverCard[] = (summary?.overdueCards ?? []).map(card => ({
    serial: `C-${card.cardId.slice(0, 8)}`,
    title: card.title,
    age: card.dueDate ? formatCarryOverDueDate(card.dueDate) : 'overdue',
    reason: `Board: ${card.boardName}`,
  }))

  const lede = summary
    ? `${summary.summary.capturesNeedingTriage} captures need triage, ${summary.summary.proposalsPendingReview} proposals await review, and ${summary.summary.overdueCards} cards are overdue.`
    : 'Activity totals are unavailable. Live cadence, streak, seal status, and your note remain available below.'

  return {
    serial: formatDossierSerial(now),
    date: now,
    headlineCardsMoved: null,
    lede,
    autoSealsIn: null,
    stats,
    cadence: {
      weights: Array.from({ length: 24 }, () => 0),
      peakHourIndex: null,
      firstAction: 'No cadence data available',
      peakAction: 'No cadence data available',
      lastAction: 'No cadence data available',
    },
    cadenceAvailable: false,
    ledger: [],
    decisions: [],
    boards: [],
    carryOver,
    streak: {
      cells: [],
      todayIndex: -1,
      totalDays: 0,
      longestThisYear: 0,
    },
    streakAvailable: false,
    lineForTomorrow: '',
  }
}

export interface UseTodayDossierOptions {
  now?: Ref<Date> | Date
}

export interface SealDayResult {
  sealed: boolean
  alreadySealed: boolean
  inProgress?: boolean
}

export function useTodayDossier(options: UseTodayDossierOptions = {}) {
  const workspace = useWorkspaceStore()
  const fixedNow = options.now instanceof Date ? options.now : null
  const liveNow = ref(new Date())
  let dayTimer: ReturnType<typeof setTimeout> | null = null

  function scheduleNextDayTick() {
    liveNow.value = new Date()
    const nextDay = new Date(liveNow.value)
    nextDay.setHours(24, 0, 0, 0)
    const delay = Math.max(0, nextDay.getTime() - liveNow.value.getTime())
    dayTimer = setTimeout(scheduleNextDayTick, delay)
  }

  if (!fixedNow && !options.now) {
    scheduleNextDayTick()
  }

  onScopeDispose(() => {
    if (dayTimer) {
      clearTimeout(dayTimer)
      dayTimer = null
    }
  })

  const now = computed<Date>(() => {
    if (fixedNow) return fixedNow
    if (options.now && 'value' in options.now) return options.now.value
    return liveNow.value
  })

  const sealed = ref(false)
  let sealMutationGeneration = 0
  const liveCadence = ref<DossierCadence | null>(null)
  const liveStreak = ref<DossierStreak | null>(null)
  const liveLineForTomorrow = ref('')
  // `cadenceAvailable` / `streakAvailable` are false both before the request
  // resolves and after it fails, so on their own they cannot tell a caller
  // which of the two is happening — and the panels rendered the failure copy
  // for the whole in-flight window (GH-1983). This flag is the missing half:
  // true from the moment a live fetch starts until the newest one settles.
  // One flag covers both panels because both come out of the same
  // `Promise.allSettled` batch in `fetchLiveData` and therefore settle together.
  const liveDataLoading = ref(true)

  const honestDossier = computed<DossierData>(() => buildHonestDossier(now.value, workspace.todaySummary))

  const dossier = computed<DossierData>(() => ({
    ...honestDossier.value,
    cadence: liveCadence.value ?? honestDossier.value.cadence,
    cadenceAvailable: liveCadence.value !== null,
    streak: liveStreak.value ?? honestDossier.value.streak,
    streakAvailable: liveStreak.value !== null,
    lineForTomorrow: liveLineForTomorrow.value,
  }))

  let autosaveTimer: ReturnType<typeof setTimeout> | null = null
  let tomorrowNoteMutationGeneration = 0
  let tomorrowNoteSaveGeneration = 0
  type TomorrowNoteAutosave = {
    text: string
    dateStr: string
    saveGeneration: number
    resolve: () => void
    reject: (error: unknown) => void
  }
  let pendingAutosave: TomorrowNoteAutosave | null = null
  let inflightAutosave: TomorrowNoteAutosave | null = null
  const AUTOSAVE_DEBOUNCE_MS = 800
  const SUPERSEDED_AUTOSAVE_ERROR = 'Superseded by newer tomorrow note autosave'

  async function flushAutosave() {
    if (inflightAutosave) return
    const pending = pendingAutosave
    if (!pending) return

    pendingAutosave = null
    inflightAutosave = pending
    try {
      await todayApi.saveTomorrowNote(pending.dateStr, pending.text)
      if (pending.saveGeneration === tomorrowNoteSaveGeneration) {
        pending.resolve()
      } else {
        pending.reject(new Error(SUPERSEDED_AUTOSAVE_ERROR))
      }
    } catch (error) {
      if (pending.saveGeneration === tomorrowNoteSaveGeneration) {
        logError('Tomorrow note autosave failed', { message: (error as Error)?.message })
        pending.reject(error)
      } else {
        pending.reject(new Error(SUPERSEDED_AUTOSAVE_ERROR))
      }
    } finally {
      inflightAutosave = null
      if (pendingAutosave && !autosaveTimer) {
        void flushAutosave()
      }
    }
  }

  function saveLineForTomorrow(text: string, dateStr = formatLocalDossierDate(now.value)): Promise<void> {
    tomorrowNoteMutationGeneration += 1
    const saveGeneration = ++tomorrowNoteSaveGeneration
    liveLineForTomorrow.value = text
    if (pendingAutosave) {
      pendingAutosave.reject(new Error(SUPERSEDED_AUTOSAVE_ERROR))
    }
    if (autosaveTimer) clearTimeout(autosaveTimer)
    return new Promise((resolve, reject) => {
      pendingAutosave = { text, dateStr, saveGeneration, resolve, reject }
      autosaveTimer = setTimeout(() => {
        autosaveTimer = null
        void flushAutosave()
      }, AUTOSAVE_DEBOUNCE_MS)
    })
  }

  onScopeDispose(() => {
    if (autosaveTimer) {
      clearTimeout(autosaveTimer)
      autosaveTimer = null
      void flushAutosave()
    }
  })

  let fetchGeneration = 0

  async function fetchLiveData() {
    const generation = ++fetchGeneration
    // Set synchronously, before the first await: the `immediate: true` watch
    // below runs during setup, so the very first paint must already read as
    // loading rather than as failed.
    liveDataLoading.value = true
    const dateStr = formatLocalDossierDate(now.value)
    const noteMutationAtFetch = tomorrowNoteMutationGeneration
    const sealMutationAtFetch = sealMutationGeneration
    const results = await Promise.allSettled([
      todayApi.getCadence(dateStr),
      todayApi.getStreak(90),
      todayApi.getSealStatus(dateStr),
      todayApi.getTomorrowNote(dateStr),
    ])

    if (generation !== fetchGeneration) return

    if (results[0].status === 'fulfilled') {
      liveCadence.value = mapCadenceResponse(results[0].value)
    }
    if (results[1].status === 'fulfilled') {
      liveStreak.value = mapStreakResponse(results[1].value)
    }
    if (sealMutationAtFetch === sealMutationGeneration && results[2].status === 'fulfilled') {
      sealed.value = results[2].value.isSealed
    }
    if (noteMutationAtFetch === tomorrowNoteMutationGeneration) {
      liveLineForTomorrow.value = results[3].status === 'fulfilled'
        ? (results[3].value?.text ?? '')
        : ''
    }
    // Only the newest fetch clears the flag; a superseded one leaves it set
    // because its replacement is still in flight.
    liveDataLoading.value = false
  }

  watch(now, (currentNow, previousNow) => {
    const didCrossLocalDay = previousNow !== undefined
      && formatLocalDossierDate(currentNow) !== formatLocalDossierDate(previousNow)

    liveCadence.value = null
    liveStreak.value = null
    liveLineForTomorrow.value = ''
    tomorrowNoteMutationGeneration += 1
    sealMutationGeneration += 1
    sealed.value = false

    if (didCrossLocalDay) {
      workspace.clearTodaySummary()
      void workspace.fetchTodaySummary().catch(() => {
        // The workspace store retains the error for PaperToday's visible retry state.
      })
    }

    void fetchLiveData()
  }, { immediate: true })

  let sealingInProgress = false

  async function sealDay(): Promise<SealDayResult> {
    if (sealed.value) {
      return { sealed: true, alreadySealed: true }
    }
    if (sealingInProgress) {
      return { sealed: false, alreadySealed: false, inProgress: true }
    }

    sealingInProgress = true
    try {
      const response = await todayApi.sealDay(formatLocalDossierDate(now.value))
      sealMutationGeneration += 1
      sealed.value = true
      return { sealed: true, alreadySealed: response.wasAlreadySealed }
    } catch {
      return { sealed: false, alreadySealed: false }
    } finally {
      sealingInProgress = false
    }
  }

  function resetSealForTesting() {
    sealed.value = false
  }

  return {
    dossier,
    liveDataLoading,
    sealed,
    sealDay,
    saveLineForTomorrow,
    resetSealForTesting,
  }
}
