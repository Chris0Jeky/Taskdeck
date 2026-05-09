import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { todayApi } from '../../api/todayApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('todayApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('getCadence', () => {
    it('sends GET to /today/cadence with date query param', async () => {
      const response = {
        buckets: [{ hour: 9, eventCount: 3 }],
        firstActionAt: '2026-01-15T09:00:00Z',
        peakHour: 9,
        lastActionAt: '2026-01-15T17:00:00Z',
      }
      vi.mocked(http.get).mockResolvedValue({ data: response })

      const result = await todayApi.getCadence('2026-01-15')

      expect(http.get).toHaveBeenCalledWith('/today/cadence?date=2026-01-15')
      expect(result).toEqual(response)
    })
  })

  describe('getStreak', () => {
    it('sends GET to /today/streak with default days=90', async () => {
      const response = {
        days: [{ date: '2026-01-15', isSealed: true, intensityBucket: 3 }],
        currentStreakLength: 5,
        longestStreakLength: 12,
        dayCount: 90,
      }
      vi.mocked(http.get).mockResolvedValue({ data: response })

      const result = await todayApi.getStreak()

      expect(http.get).toHaveBeenCalledWith('/today/streak?days=90')
      expect(result).toEqual(response)
    })

    it('sends GET to /today/streak with custom days', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: { days: [], currentStreakLength: 0, longestStreakLength: 0, dayCount: 0 } })

      await todayApi.getStreak(30)

      expect(http.get).toHaveBeenCalledWith('/today/streak?days=30')
    })
  })

  describe('sealDay', () => {
    it('sends POST to /today/seal with date body', async () => {
      const response = { sealedAt: '2026-01-15T18:00:00Z', wasAlreadySealed: false }
      vi.mocked(http.post).mockResolvedValue({ data: response })

      const result = await todayApi.sealDay('2026-01-15')

      expect(http.post).toHaveBeenCalledWith('/today/seal', { date: '2026-01-15' })
      expect(result).toEqual(response)
    })
  })

  describe('getSealStatus', () => {
    it('sends GET to /today/seal with date query param', async () => {
      const response = { date: '2026-01-15', isSealed: true, sealedAt: '2026-01-15T18:00:00Z' }
      vi.mocked(http.get).mockResolvedValue({ data: response })

      const result = await todayApi.getSealStatus('2026-01-15')

      expect(http.get).toHaveBeenCalledWith('/today/seal?date=2026-01-15')
      expect(result).toEqual(response)
    })
  })

  describe('getTomorrowNote', () => {
    it('returns note data on 200', async () => {
      const response = {
        id: 'note-1',
        date: '2026-01-15',
        text: 'Pick up AA contrast audit',
        updatedAt: '2026-01-15T18:00:00Z',
        createdAt: '2026-01-15T17:00:00Z',
      }
      vi.mocked(http.get).mockResolvedValue({ status: 200, data: response })

      const result = await todayApi.getTomorrowNote('2026-01-15')

      expect(http.get).toHaveBeenCalledWith(
        '/today/tomorrow-note?date=2026-01-15',
        { validateStatus: expect.any(Function) },
      )
      expect(result).toEqual(response)
    })

    it('returns null on 204 (no note)', async () => {
      vi.mocked(http.get).mockResolvedValue({ status: 204, data: '' })

      const result = await todayApi.getTomorrowNote('2026-01-15')

      expect(result).toBeNull()
    })
  })

  describe('saveTomorrowNote', () => {
    it('sends PUT to /today/tomorrow-note with date and text', async () => {
      const response = {
        id: 'note-1',
        date: '2026-01-15',
        text: 'Do the thing',
        updatedAt: '2026-01-15T18:05:00Z',
        createdAt: '2026-01-15T17:00:00Z',
      }
      vi.mocked(http.put).mockResolvedValue({ data: response })

      const result = await todayApi.saveTomorrowNote('2026-01-15', 'Do the thing')

      expect(http.put).toHaveBeenCalledWith('/today/tomorrow-note', { date: '2026-01-15', text: 'Do the thing' })
      expect(result).toEqual(response)
    })
  })
})
