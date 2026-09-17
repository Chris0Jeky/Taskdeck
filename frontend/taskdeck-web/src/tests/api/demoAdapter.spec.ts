import { afterEach, describe, expect, it, vi } from 'vitest'
import type { InternalAxiosRequestConfig } from 'axios'
import { demoHttpAdapter, resetDemoHttpFixtures } from '../../api/demoAdapter'
import { DEMO_PROPOSAL_ID, DEMO_CHAT_SESSION_ID } from '../../utils/demoData'
import { DEMO_USER } from '../../utils/demoIdentity'
import { LOCAL_DEV_API_BASE_URL } from '../../utils/apiBaseUrl'

function config(method: string, url: string, data?: unknown): InternalAxiosRequestConfig {
  return {
    method,
    url,
    baseURL: LOCAL_DEV_API_BASE_URL,
    data,
    headers: { 'Content-Type': 'application/json' },
  } as InternalAxiosRequestConfig
}

describe('demoHttpAdapter', () => {
  afterEach(() => {
    resetDemoHttpFixtures()
    vi.restoreAllMocks()
  })

  it('does not open XHR even when the request is aimed at localhost:5000', async () => {
    const open = vi.spyOn(XMLHttpRequest.prototype, 'open')
    const send = vi.spyOn(XMLHttpRequest.prototype, 'send')

    const response = await demoHttpAdapter(config('get', '/automation/proposals'))

    expect(open).not.toHaveBeenCalled()
    expect(send).not.toHaveBeenCalled()
    expect(response.status).toBe(200)
    expect(Array.isArray(response.data)).toBe(true)
    expect(response.data).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: DEMO_PROPOSAL_ID, status: 'PendingReview', boardId: 'demo-board-1' }),
    ]))
  })

  it('loads the same pending proposal Home advertises', async () => {
    const list = await demoHttpAdapter(config('get', '/automation/proposals?limit=200'))
    expect(list.data).toHaveLength(1)
    expect(list.data[0].id).toBe(DEMO_PROPOSAL_ID)

    const one = await demoHttpAdapter(config('get', `/automation/proposals/${DEMO_PROPOSAL_ID}`))
    expect(one.data.id).toBe(DEMO_PROPOSAL_ID)
    expect(one.data.summary).toContain('dark mode')
  })

  it('loads chat sessions and health without a live provider', async () => {
    const health = await demoHttpAdapter(config('get', '/llm/chat/health'))
    expect(health.data).toMatchObject({ isAvailable: true, isMock: true, providerName: 'Mock' })

    const sessions = await demoHttpAdapter(config('get', '/llm/chat/sessions'))
    expect(sessions.data).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: DEMO_CHAT_SESSION_ID, boardId: 'demo-board-1' }),
    ]))
  })

  it('loads card parent candidates and assignees', async () => {
    const cards = await demoHttpAdapter(config('get', '/boards/demo-board-1/cards'))
    expect(Array.isArray(cards.data)).toBe(true)
    expect(cards.data.length).toBeGreaterThan(1)
    expect(cards.data.some((card: { id: string }) => card.id === 'demo-board-1-card-2')).toBe(true)

    const people = await demoHttpAdapter(config('get', '/boards/demo-board-1/participants'))
    expect(people.data).toEqual(expect.arrayContaining([
      expect.objectContaining({ userId: DEMO_USER.id }),
    ]))
  })
})
