import { computed, onScopeDispose, ref, watch, type Ref } from 'vue'
import { useWorkspaceStore } from '../store/workspaceStore'
import { todayApi, type CadenceApiResponse, type StreakApiResponse } from '../api/todayApi'
import { logError } from '../utils/errorReporting'
import type { TodaySummary } from '../types/workspace'

export type DossierLedgerWho = 'you' | 'haiku' | 'system'
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
  id: 'cards-moved' | 'proposals-applied' | 'captures-triaged' | 'longest-focus' | 'overdue'
  value: number | string
  numeric: boolean
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
  headlineCardsMoved: number
  lede: string
  autoSealsIn: string
  stats: DossierStatCard[]
  cadence: DossierCadence
  ledger: DossierLedgerEntry[]
  decisions: DossierDecision[]
  boards: DossierBoardLine[]
  carryOver: DossierCarryOverCard[]
  streak: DossierStreak
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
  const d = new Date(iso)
  return `${d.getUTCHours().toString().padStart(2, '0')}:${d.getUTCMinutes().toString().padStart(2, '0')}`
}

function mapCadenceResponse(response: CadenceApiResponse): DossierCadence {
  const weights = Array.from({ length: 24 }, (_, i) => {
    const bucket = response.buckets.find((b) => b.hour === i)
    return bucket?.eventCount ?? 0
  })

  const peakHourIndex = response.peakHour

  const firstTime = formatUtcTime(response.firstActionAt)
  const lastTime = formatUtcTime(response.lastActionAt)

  const peakEvents = peakHourIndex != null ? (weights[peakHourIndex] ?? 0) : 0
  const peakAction = peakHourIndex != null
    ? `${peakHourIndex.toString().padStart(2, '0')}:00-${((peakHourIndex + 1) % 24).toString().padStart(2, '0')}:00 UTC · ${peakEvents} events`
    : 'no peak'

  return {
    weights,
    peakHourIndex,
    firstAction: `${firstTime} UTC · first action`,
    peakAction,
    lastAction: `${lastTime} UTC · last action`,
  }
}

function mapStreakResponse(response: StreakApiResponse): DossierStreak {
  const cells = response.days.map((d) => d.intensityBucket)
  return {
    cells,
    todayIndex: cells.length - 1,
    totalDays: response.currentStreakLength,
    longestThisYear: response.longestStreakLength,
  }
}

function buildStubDossier(now: Date, summary: TodaySummary | null): DossierData {
  const overdueCount = summary?.summary.overdueCards ?? 2
  const capturesTriaged = 11
  const cardsMoved = 9
  const proposalsApplied = 3

  const stats: DossierStatCard[] = [
    {
      id: 'cards-moved',
      value: cardsMoved,
      numeric: true,
      label: 'cards moved',
      sub: '+2 vs your wk avg',
      tone: 'ink',
    },
    {
      id: 'proposals-applied',
      value: proposalsApplied,
      numeric: true,
      label: 'proposals applied',
      sub: 'of 4 reviewed · 75%',
      tone: 'ember',
    },
    {
      id: 'captures-triaged',
      value: capturesTriaged,
      numeric: true,
      label: 'captures triaged',
      sub: '0 left in inbox',
      tone: 'ink',
    },
    {
      id: 'longest-focus',
      value: '2h 14m',
      numeric: false,
      label: 'focus time · longest',
      sub: '13:02 — 15:16 · uninterrupted',
      tone: 'applied',
    },
    {
      id: 'overdue',
      value: overdueCount,
      numeric: true,
      label: 'overdue',
      sub: 'C-072, C-061',
      tone: 'overdue',
    },
  ]

  const cadenceWeights = [
    0, 0, 0, 0, 0, 0, 0, 0,
    1, 3, 2, 1, 3, 4, 2, 3, 4, 2,
    0, 0, 0, 0, 0, 0,
  ]

  const cadence: DossierCadence = {
    weights: cadenceWeights,
    peakHourIndex: 13,
    firstAction: '08:42 · capture',
    peakAction: '13:00 — 14:00 · 7 events',
    lastAction: '17:18 · seal',
  }

  const ledger: DossierLedgerEntry[] = [
    { serial: 'L-018', time: '17:18', who: 'you', what: "Sealed proposal #011 · Set up CI → Done", tone: 'applied' },
    { serial: 'L-017', time: '16:04', who: 'haiku', what: 'Proposed split · #014 · ready for review', tone: 'ember' },
    { serial: 'L-016', time: '15:42', who: 'you', what: 'Triaged 7 captures into Product Backlog', tone: 'active' },
    { serial: 'L-015', time: '15:16', who: 'you', what: 'End of focus block (2h 14m)', tone: 'passive' },
    { serial: 'L-014', time: '13:55', who: 'haiku', what: 'Proposed merge of duplicates · #008 · rejected', tone: 'mute' },
    { serial: 'L-013', time: '13:02', who: 'you', what: 'Started focus block · DnD off · 2h 14m', tone: 'active' },
    { serial: 'L-012', time: '12:48', who: 'you', what: "Renamed board · 'Sprint 12 · QA'", tone: 'active' },
    { serial: 'L-011', time: '11:42', who: 'you', what: 'Applied #014 · 3 cards land · undo 6h', tone: 'applied' },
    { serial: 'L-010', time: '11:38', who: 'haiku', what: 'Proposed split · #014 · 0.84 confidence', tone: 'ember' },
    { serial: 'L-009', time: '10:14', who: 'you', what: "Deferred #012 · 'Add Blocked column'", tone: 'passive' },
    { serial: 'L-008', time: '09:18', who: 'you', what: "Applied #011 · 'Set up CI' → Done", tone: 'applied' },
    { serial: 'L-007', time: '09:00', who: 'system', what: 'Day opened · 5 cards on Today board', tone: 'passive' },
  ]

  const decisions: DossierDecision[] = [
    { serial: '#014', title: 'Split: Implement dark mode', verdict: 'applied', confidence: 0.84, when: '11:42' },
    { serial: '#012', title: "Add 'Blocked' column", verdict: 'deferred', confidence: 0.71, when: '10:14' },
    { serial: '#011', title: "Move 'Set up CI' → Done", verdict: 'applied', confidence: 0.91, when: '09:18' },
    { serial: '#008', title: 'Merge duplicates', verdict: 'rejected', confidence: 0.62, when: 'yest', stale: true },
  ]

  const boards: DossierBoardLine[] = [
    { id: 'product-backlog', name: 'Product Backlog', moves: 6, proposals: 2 },
    { id: 'sprint-12', name: 'Sprint 12', moves: 3, proposals: 1 },
    { id: 'personal', name: 'Personal', moves: 1, proposals: 0 },
    { id: 'side-projects', name: 'Side projects', moves: 0, proposals: 0 },
    { id: 'notes-references', name: 'Notes & references', moves: 0, proposals: 0 },
  ]

  const carryOver: DossierCarryOverCard[] = [
    { serial: 'C-072', title: 'Audit AA contrast on toasts', age: '3d overdue', reason: 'rolled over twice' },
    { serial: 'C-061', title: 'Reply: design system intro', age: '1d overdue', reason: 'snoozed yesterday' },
  ]

  const cells = Array.from({ length: 90 }, (_, i) => {
    if (i === 89) return 4
    if (i === 73) return 0
    return ((i * 31) % 5)
  })

  const streak: DossierStreak = {
    cells,
    todayIndex: 89,
    totalDays: 17,
    longestThisYear: 23,
  }

  return {
    serial: formatDossierSerial(now),
    date: now,
    headlineCardsMoved: cardsMoved,
    lede:
      "A quiet Saturday. You triaged the morning inbox, applied two of haiku's proposals, and closed the dark-mode hand-off. Two cards drifted past their due — bring them up tomorrow.",
    autoSealsIn: '2h 18m',
    stats,
    cadence,
    ledger,
    decisions,
    boards,
    carryOver,
    streak,
    lineForTomorrow:
      "Pick up the AA contrast audit first — it's been carried twice. Aim to seal Sprint 12 by Wednesday.",
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
  const liveCadence = ref<DossierCadence | null>(null)
  const liveStreak = ref<DossierStreak | null>(null)
  const liveLineForTomorrow = ref('')

  const stubDossier = computed<DossierData>(() => buildStubDossier(now.value, workspace.todaySummary))

  const dossier = computed<DossierData>(() => {
    const base = stubDossier.value
    return {
      ...base,
      cadence: liveCadence.value ?? base.cadence,
      streak: liveStreak.value ?? base.streak,
      lineForTomorrow: liveLineForTomorrow.value,
    }
  })

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
    } catch (err) {
      if (pending.saveGeneration === tomorrowNoteSaveGeneration) {
        logError('Tomorrow note autosave failed', { message: (err as Error)?.message })
        pending.reject(err)
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
    const gen = ++fetchGeneration
    const dateStr = formatLocalDossierDate(now.value)
    const tomorrowNoteMutationGenerationAtFetch = tomorrowNoteMutationGeneration

    const results = await Promise.allSettled([
      todayApi.getCadence(dateStr),
      todayApi.getStreak(90),
      todayApi.getSealStatus(dateStr),
      todayApi.getTomorrowNote(dateStr),
    ])

    if (gen !== fetchGeneration) return

    if (results[0].status === 'fulfilled') {
      liveCadence.value = mapCadenceResponse(results[0].value)
    }
    if (results[1].status === 'fulfilled') {
      liveStreak.value = mapStreakResponse(results[1].value)
    }
    if (results[2].status === 'fulfilled') {
      sealed.value = results[2].value.isSealed
    }
    if (tomorrowNoteMutationGenerationAtFetch === tomorrowNoteMutationGeneration) {
      if (results[3].status === 'fulfilled') {
        liveLineForTomorrow.value = results[3].value?.text ?? ''
      } else {
        liveLineForTomorrow.value = ''
      }
    }
  }

  watch(now, () => {
    liveCadence.value = null
    liveStreak.value = null
    liveLineForTomorrow.value = ''
    tomorrowNoteMutationGeneration += 1
    sealed.value = false
    fetchLiveData()
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
      const dateStr = formatLocalDossierDate(now.value)
      const response = await todayApi.sealDay(dateStr)
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
    sealed,
    sealDay,
    saveLineForTomorrow,
    resetSealForTesting,
  }
}
