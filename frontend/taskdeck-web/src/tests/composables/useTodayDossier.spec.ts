import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'
import { todayApi } from '../../api/todayApi'
import type { CadenceApiResponse, StreakApiResponse, SealStatusApiResponse, TomorrowNoteApiResponse } from '../../api/todayApi'

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
  useWorkspaceStore: () => ({ todaySummary: null }),
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
  })

  afterEach(() => {
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

  it('falls back to stub data when all API calls fail', async () => {
    vi.mocked(todayApi.getCadence).mockRejectedValue(new Error('network'))
    vi.mocked(todayApi.getStreak).mockRejectedValue(new Error('network'))
    vi.mocked(todayApi.getSealStatus).mockRejectedValue(new Error('network'))
    vi.mocked(todayApi.getTomorrowNote).mockRejectedValue(new Error('network'))

    const { dossier } = await importAndCreate()
    await vi.waitFor(() => {
      expect(todayApi.getCadence).toHaveBeenCalled()
    })

    expect(dossier.value.serial).toMatch(/^D-\d{4}-\d{2}-\d{2}-\d{3}$/)
    expect(dossier.value.cadence.peakHourIndex).toBe(13)
    expect(dossier.value.streak.cells).toHaveLength(90)
    expect(dossier.value.lineForTomorrow).toContain('AA contrast audit')
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
})
