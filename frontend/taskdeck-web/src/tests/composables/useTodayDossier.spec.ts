import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'
import { todayApi } from '../../api/todayApi'
import type { CadenceApiResponse, StreakApiResponse, SealStatusApiResponse, TomorrowNoteApiResponse } from '../../api/todayApi'
import type { TodaySummary } from '../../types/workspace'

const workspaceMock = vi.hoisted(() => ({
  todaySummary: null as TodaySummary | null,
  clearTodaySummary: vi.fn(),
  fetchTodaySummary: vi.fn(),
}))

const workspaceSummaryState = ref<TodaySummary | null>(null)
Object.defineProperty(workspaceMock, 'todaySummary', {
  configurable: true,
  get: () => workspaceSummaryState.value,
  set: value => { workspaceSummaryState.value = value },
})

vi.mock('../../api/todayApi', () => ({
  todayApi: {
    getCadence: vi.fn(),
    getStreak: vi.fn(),
    getSealStatus: vi.fn(),
    getTomorrowNote: vi.fn(),
    sealDay: vi.fn(),
    saveTomorrowNote: vi.fn(),
  },
}))

vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => workspaceMock,
}))

const cadenceResponse: CadenceApiResponse = {
  buckets: [
    { hour: 9, eventCount: 3 },
    { hour: 10, eventCount: 1 },
    { hour: 14, eventCount: 5 },
  ],
  firstActionAt: '2026-01-15T09:12:00Z',
  peakHour: 14,
  lastActionAt: '2026-01-15T17:30:00Z',
}

const streakResponse: StreakApiResponse = {
  days: Array.from({ length: 90 }, (_, i) => ({
    date: `2025-10-${(i + 1).toString().padStart(2, '0')}`,
    isSealed: i < 85,
    intensityBucket: i % 5,
  })),
  currentStreakLength: 7,
  longestStreakLength: 15,
  dayCount: 90,
}

const sealStatusResponse: SealStatusApiResponse = {
  date: '2026-01-15',
  isSealed: false,
  sealedAt: null,
}

const tomorrowNoteResponse: TomorrowNoteApiResponse = {
  id: 'note-abc',
  date: '2026-01-15',
  text: 'Review the AA contrast audit',
  updatedAt: '2026-01-15T18:00:00Z',
  createdAt: '2026-01-15T17:00:00Z',
}

describe('useTodayDossier', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    workspaceMock.todaySummary = null
    workspaceMock.clearTodaySummary.mockImplementation(() => {
      workspaceMock.todaySummary = null
    })
    workspaceMock.fetchTodaySummary.mockResolvedValue(null)
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  async function importAndCreate(nowDate?: Date) {
    const { useTodayDossier } = await import('../../composables/useTodayDossier')
    const now = nowDate ?? new Date('2026-01-15T12:00:00Z')
    return useTodayDossier({ now })
  }

  it('fetches live cadence and maps to DossierCadence', async () => {
    vi.mocked(todayApi.getCadence).mockResolvedValue(cadenceResponse)
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))

    const { dossier } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getCadence).toHaveBeenCalled()
    })

    expect(dossier.value.cadence.weights).toHaveLength(24)
    expect(dossier.value.cadence.weights[9]).toBe(3)
    expect(dossier.value.cadence.weights[14]).toBe(5)
    expect(dossier.value.cadence.weights[0]).toBe(0)
    expect(dossier.value.cadence.peakHourIndex).toBe(14)
    expect(dossier.value.cadence.firstAction).toContain('09:12 UTC')
    expect(dossier.value.cadence.peakAction).toContain('14:00-15:00 UTC')
    expect(dossier.value.cadence.peakAction).toContain('5 events')
    expect(dossier.value.cadence.lastAction).toContain('17:30 UTC')
  })

  it('preserves a null cadence peak instead of highlighting midnight', async () => {
    vi.mocked(todayApi.getCadence).mockResolvedValue({
      ...cadenceResponse,
      peakHour: null,
    })
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))

    const { dossier } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getCadence).toHaveBeenCalled()
    })

    expect(dossier.value.cadence.peakHourIndex).toBeNull()
    expect(dossier.value.cadence.peakAction).toBe('no peak')
  })

  it('renders unavailable time markers for invalid cadence timestamps', async () => {
    vi.mocked(todayApi.getCadence).mockResolvedValue({
      ...cadenceResponse,
      firstActionAt: 'not-an-iso-timestamp',
      lastActionAt: '2026-99-99T25:61:00Z',
    })
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))

    const { dossier } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getCadence).toHaveBeenCalled()
    })

    expect(dossier.value.cadence.firstAction).toBe('--:-- UTC · first action')
    expect(dossier.value.cadence.lastAction).toBe('--:-- UTC · last action')
    expect(dossier.value.cadence.firstAction).not.toContain('NaN')
    expect(dossier.value.cadence.lastAction).not.toContain('NaN')
  })

  it('fetches live streak and maps to DossierStreak', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockResolvedValue(streakResponse)
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))

    const { dossier } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getStreak).toHaveBeenCalled()
    })

    expect(dossier.value.streak.cells).toHaveLength(90)
    expect(dossier.value.streak.todayIndex).toBe(89)
    expect(dossier.value.streak.totalDays).toBe(7)
    expect(dossier.value.streak.longestThisYear).toBe(15)
  })

  it('fetches seal status and reflects in sealed ref', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockResolvedValue({ ...sealStatusResponse, isSealed: true, sealedAt: '2026-01-15T18:00:00Z' })
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))

    const { sealed } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getSealStatus).toHaveBeenCalled()
    })

    expect(sealed.value).toBe(true)
  })

  it('fetches tomorrow note and maps to lineForTomorrow', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(tomorrowNoteResponse)

    const { dossier } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getTomorrowNote).toHaveBeenCalled()
    })

    expect(dossier.value.lineForTomorrow).toBe('Review the AA contrast audit')
  })

  it('uses honest unavailable states instead of fabricated dossier data when live calls fail', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('network'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('network'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('network'))
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('network'))

    const { dossier } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getCadence).toHaveBeenCalled()
    })

    expect(dossier.value.serial).toMatch(/^D-\d{4}-\d{2}-\d{2}-\d{3}$/)
    expect(dossier.value.headlineCardsMoved).toBeNull()
    expect(dossier.value.autoSealsIn).toBeNull()
    expect(dossier.value.stats).toEqual([])
    expect(dossier.value.cadenceAvailable).toBe(false)
    expect(dossier.value.cadence.peakHourIndex).toBeNull()
    expect(dossier.value.streakAvailable).toBe(false)
    expect(dossier.value.streak.cells).toEqual([])
    expect(dossier.value.ledger).toEqual([])
    expect(dossier.value.decisions).toEqual([])
    expect(dossier.value.boards).toEqual([])
    expect(dossier.value.carryOver).toEqual([])
    expect(dossier.value.lede).toContain('Activity totals are unavailable')
    expect(dossier.value.lineForTomorrow).toBe('')
  })

  it('maps the live Today summary to truthful stats and overdue carry-over cards', async () => {
    workspaceMock.todaySummary = {
      workspaceMode: 'guided',
      onboarding: {
        visibility: 'active',
        isComplete: false,
        currentStepId: null,
        dismissedAt: null,
        completedAt: null,
        steps: [],
      },
      summary: {
        capturesNeedingTriage: 2,
        proposalsPendingReview: 3,
        overdueCards: 1,
        dueTodayCards: 4,
        blockedCards: 1,
      },
      overdueCards: [{
        boardId: 'board-1',
        boardName: 'Client onboarding',
        cardId: 'card-123456789',
        title: 'Confirm engagement letter',
        dueDate: '2026-01-14',
        blockReason: null,
        updatedAt: '2026-01-15T09:00:00Z',
      }],
      dueTodayCards: [],
      blockedCards: [],
      recommendedActions: [],
    }
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(null)

    const { dossier } = await importAndCreate()

    expect(dossier.value.stats.map(stat => [stat.id, stat.value])).toEqual([
      ['captures-needing-triage', 2],
      ['proposals-pending-review', 3],
      ['overdue', 1],
      ['due-today', 4],
      ['blocked', 1],
    ])
    expect(dossier.value.carryOver).toEqual([{
      serial: 'C-card-123',
      title: 'Confirm engagement letter',
      age: 'due 2026-01-14',
      reason: 'Board: Client onboarding',
    }])
    expect(dossier.value.lede).toContain('2 captures need triage')
    expect(dossier.value.lede).toContain('3 proposals await review')
  })

  it('autosaves tomorrow note to the date captured at edit time', async () => {
    vi.useFakeTimers()
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(null)
    vi.mocked(todayApi.saveTomorrowNote).mockResolvedValue(tomorrowNoteResponse)

    const { useTodayDossier } = await import('../../composables/useTodayDossier')
    const nowRef = ref(new Date('2026-01-15T23:59:59'))
    const { saveLineForTomorrow } = useTodayDossier({ now: nowRef })

    const save = saveLineForTomorrow('Finish handoff', '2026-01-15')
    nowRef.value = new Date('2026-01-16T00:00:01')

    await vi.advanceTimersByTimeAsync(850)
    await save

    expect(todayApi.saveTomorrowNote).toHaveBeenCalledWith('2026-01-15', 'Finish handoff')
  })

  it('rejects autosave promise when the backend save fails', async () => {
    vi.useFakeTimers()
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(null)
    vi.mocked(todayApi.saveTomorrowNote).mockRejectedValue(new Error('offline'))

    const { saveLineForTomorrow } = await importAndCreate()
    const save = saveLineForTomorrow('Draft')
    const assertion = expect(save).rejects.toThrow('offline')

    await vi.advanceTimersByTimeAsync(850)

    await assertion
  })

  it('rejects superseded autosaves without writing the older text', async () => {
    vi.useFakeTimers()
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(null)
    vi.mocked(todayApi.saveTomorrowNote).mockResolvedValue(tomorrowNoteResponse)

    const { saveLineForTomorrow } = await importAndCreate()
    const first = saveLineForTomorrow('older draft', '2026-01-15')
    const firstAssertion = expect(first).rejects.toThrow('Superseded')
    const second = saveLineForTomorrow('latest draft', '2026-01-15')

    await firstAssertion
    await vi.advanceTimersByTimeAsync(850)
    await second

    expect(todayApi.saveTomorrowNote).toHaveBeenCalledTimes(1)
    expect(todayApi.saveTomorrowNote).toHaveBeenCalledWith('2026-01-15', 'latest draft')
  })

  it('does not let slow tomorrow-note hydration overwrite a newer local edit', async () => {
    vi.useFakeTimers()
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.saveTomorrowNote).mockResolvedValue(tomorrowNoteResponse)

    let resolveHydration!: (value: TomorrowNoteApiResponse) => void
    vi.mocked(todayApi.getTomorrowNote).mockImplementationOnce(
      () => new Promise((resolve) => {
        resolveHydration = resolve
      }),
    )

    const { dossier, saveLineForTomorrow } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getTomorrowNote).toHaveBeenCalled()
    })

    const save = saveLineForTomorrow('Fresh local edit', '2026-01-15')
    resolveHydration({ ...tomorrowNoteResponse, text: 'Older server note' })

    await vi.waitFor(() => {
      expect(dossier.value.lineForTomorrow).toBe('Fresh local edit')
    })

    await vi.advanceTimersByTimeAsync(850)
    await save

    expect(todayApi.saveTomorrowNote).toHaveBeenCalledWith('2026-01-15', 'Fresh local edit')
    expect(dossier.value.lineForTomorrow).toBe('Fresh local edit')
  })

  it('serializes in-flight tomorrow-note saves so older requests cannot finish after newer saves', async () => {
    vi.useFakeTimers()
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(null)

    let resolveFirst!: (value: TomorrowNoteApiResponse) => void
    let resolveSecond!: (value: TomorrowNoteApiResponse) => void
    vi.mocked(todayApi.saveTomorrowNote)
      .mockImplementationOnce(() => new Promise<TomorrowNoteApiResponse>((resolve) => {
        resolveFirst = resolve
      }))
      .mockImplementationOnce(() => new Promise<TomorrowNoteApiResponse>((resolve) => {
        resolveSecond = resolve
      }))

    const { dossier, saveLineForTomorrow } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getTomorrowNote).toHaveBeenCalled()
    })

    const first = saveLineForTomorrow('older draft', '2026-01-15')
    const firstAssertion = expect(first).rejects.toThrow('Superseded')
    await vi.advanceTimersByTimeAsync(850)

    expect(todayApi.saveTomorrowNote).toHaveBeenCalledTimes(1)
    expect(todayApi.saveTomorrowNote).toHaveBeenCalledWith('2026-01-15', 'older draft')

    const second = saveLineForTomorrow('latest draft', '2026-01-15')
    await vi.advanceTimersByTimeAsync(850)

    expect(todayApi.saveTomorrowNote).toHaveBeenCalledTimes(1)

    resolveFirst({ ...tomorrowNoteResponse, text: 'older draft' })
    await firstAssertion

    await vi.waitFor(() => {
      expect(todayApi.saveTomorrowNote).toHaveBeenCalledTimes(2)
    })
    expect(todayApi.saveTomorrowNote).toHaveBeenLastCalledWith('2026-01-15', 'latest draft')

    resolveSecond({ ...tomorrowNoteResponse, text: 'latest draft' })
    await second

    expect(dossier.value.lineForTomorrow).toBe('latest draft')
  })

  it('sealDay calls POST /today/seal and returns sealed status', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockResolvedValue(sealStatusResponse)
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.sealDay).mockResolvedValue({ sealedAt: '2026-01-15T18:00:00Z', wasAlreadySealed: false })

    const { sealDay, sealed } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getSealStatus).toHaveBeenCalled()
    })

    expect(sealed.value).toBe(false)

    const result = await sealDay()
    expect(result.sealed).toBe(true)
    expect(result.alreadySealed).toBe(false)
    expect(sealed.value).toBe(true)
    expect(todayApi.sealDay).toHaveBeenCalled()
  })

  it('sealDay returns alreadySealed when called twice', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockResolvedValue(sealStatusResponse)
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.sealDay).mockResolvedValue({ sealedAt: '2026-01-15T18:00:00Z', wasAlreadySealed: false })

    const { sealDay } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getSealStatus).toHaveBeenCalled()
    })

    await sealDay()
    const second = await sealDay()
    expect(second.alreadySealed).toBe(true)
    expect(todayApi.sealDay).toHaveBeenCalledTimes(1)
  })

  it('marks duplicate seal calls as in progress without a failure state', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockResolvedValue(sealStatusResponse)
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))
    let resolveSeal!: (value: { sealedAt: string; wasAlreadySealed: boolean }) => void
    vi.mocked(todayApi.sealDay).mockImplementationOnce(
      () => new Promise((resolve) => {
        resolveSeal = resolve
      }),
    )

    const { sealDay } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getSealStatus).toHaveBeenCalled()
    })

    const first = sealDay()
    const second = await sealDay()
    resolveSeal({ sealedAt: '2026-01-15T18:00:00Z', wasAlreadySealed: false })

    await expect(first).resolves.toEqual({ sealed: true, alreadySealed: false })
    expect(second).toEqual({ sealed: false, alreadySealed: false, inProgress: true })
    expect(todayApi.sealDay).toHaveBeenCalledTimes(1)
  })

  it('returns empty lineForTomorrow when API returns 204 (no note)', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(null)

    const { dossier } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getTomorrowNote).toHaveBeenCalled()
    })

    expect(dossier.value.lineForTomorrow).toBe('')
  })

  it('does not let stale getSealStatus overwrite a successful local seal', async () => {
    let resolveSealStatus!: (value: SealStatusApiResponse) => void
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.getSealStatus).mockImplementationOnce(
      () => new Promise((resolve) => { resolveSealStatus = resolve }),
    )
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('skip'))
    vi.mocked(todayApi.sealDay).mockResolvedValue({ sealedAt: '2026-01-15T18:00:00Z', wasAlreadySealed: false })

    const { sealDay, sealed } = await importAndCreate()

    await sealDay()
    expect(sealed.value).toBe(true)

    resolveSealStatus({ date: '2026-01-15', isSealed: false, sealedAt: null })
    await vi.waitFor(() => expect(true).toBe(true))

    expect(sealed.value).toBe(true)
  })

  it('re-fetches data when now changes', async () => {
    vi.mocked(todayApi.getCadence).mockResolvedValue(cadenceResponse)
    vi.mocked(todayApi.getStreak).mockResolvedValue(streakResponse)
    vi.mocked(todayApi.getSealStatus).mockResolvedValue(sealStatusResponse)
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(null)

    const { useTodayDossier } = await import('../../composables/useTodayDossier')
    const nowRef = ref(new Date('2026-01-15T12:00:00Z'))
    useTodayDossier({ now: nowRef })

    await vi.waitFor(() => {
      expect(todayApi.getCadence).toHaveBeenCalledTimes(1)
    })

    nowRef.value = new Date('2026-01-16T12:00:00Z')

    await vi.waitFor(() => {
      expect(todayApi.getCadence).toHaveBeenCalledTimes(2)
    })
  })

  it('invalidates and replaces the workspace summary after a local-day rollover', async () => {
    const previousSummary: TodaySummary = {
      workspaceMode: 'guided',
      onboarding: {
        visibility: 'active',
        isComplete: false,
        currentStepId: null,
        dismissedAt: null,
        completedAt: null,
        steps: [],
      },
      summary: {
        capturesNeedingTriage: 8,
        proposalsPendingReview: 5,
        overdueCards: 2,
        dueTodayCards: 1,
        blockedCards: 0,
      },
      overdueCards: [{
        boardId: 'old-board',
        boardName: 'Yesterday',
        cardId: 'old-card-123',
        title: 'Yesterday carry-over',
        dueDate: '2026-01-14',
        blockReason: null,
        updatedAt: '2026-01-15T09:00:00Z',
      }],
      dueTodayCards: [],
      blockedCards: [],
      recommendedActions: [],
    }
    const nextSummary: TodaySummary = {
      ...previousSummary,
      summary: {
        capturesNeedingTriage: 1,
        proposalsPendingReview: 0,
        overdueCards: 0,
        dueTodayCards: 6,
        blockedCards: 1,
      },
      overdueCards: [],
    }
    workspaceMock.todaySummary = previousSummary

    let resolveSummary!: (summary: TodaySummary) => void
    workspaceMock.fetchTodaySummary.mockImplementationOnce(
      () => new Promise<TodaySummary>((resolve) => {
        resolveSummary = (summary) => {
          workspaceMock.todaySummary = summary
          resolve(summary)
        }
      }),
    )
    vi.mocked(todayApi.getCadence).mockResolvedValue(cadenceResponse)
    vi.mocked(todayApi.getStreak).mockResolvedValue(streakResponse)
    vi.mocked(todayApi.getSealStatus).mockResolvedValue(sealStatusResponse)
    vi.mocked(todayApi.getTomorrowNote).mockResolvedValue(null)

    const { useTodayDossier } = await import('../../composables/useTodayDossier')
    const nowRef = ref(new Date(2026, 0, 15, 23, 59, 59))
    const { dossier } = useTodayDossier({ now: nowRef })

    expect(dossier.value.stats.find(stat => stat.id === 'captures-needing-triage')?.value).toBe(8)
    expect(dossier.value.carryOver[0]?.title).toBe('Yesterday carry-over')

    nowRef.value = new Date(2026, 0, 16, 0, 0, 1)

    await vi.waitFor(() => {
      expect(workspaceMock.clearTodaySummary).toHaveBeenCalledTimes(1)
      expect(workspaceMock.fetchTodaySummary).toHaveBeenCalledTimes(1)
    })
    expect(dossier.value.stats).toEqual([])
    expect(dossier.value.carryOver).toEqual([])

    resolveSummary(nextSummary)

    await vi.waitFor(() => {
      expect(dossier.value.stats.find(stat => stat.id === 'captures-needing-triage')?.value).toBe(1)
    })
    expect(dossier.value.stats.find(stat => stat.id === 'due-today')?.value).toBe(6)
    expect(dossier.value.carryOver).toEqual([])
    expect(dossier.value.serial).toContain('2026-01-16')
  })
})
