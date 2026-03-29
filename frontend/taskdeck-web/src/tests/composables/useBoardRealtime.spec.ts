import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createBoardRealtimeController } from '../../composables/useBoardRealtime'
import { HttpTransportType } from '@microsoft/signalr'

const callbacks: {
  boardMutation?: (event: { boardId: string }) => Promise<void> | void
  boardPresence?: (event: { boardId: string; members: Array<{ userId: string }> }) => void
  reconnecting?: () => Promise<void> | void
  reconnected?: () => Promise<void> | void
  close?: () => Promise<void> | void
} = {}

const mockConnection = {
  state: 'Disconnected',
  start: vi.fn(async () => {
    mockConnection.state = 'Connected'
  }),
  stop: vi.fn(async () => {
    mockConnection.state = 'Disconnected'
  }),
  invoke: vi.fn(async () => undefined),
  on: vi.fn((eventName: string, handler: (event: { boardId: string }) => Promise<void> | void) => {
    if (eventName === 'boardMutation') {
      callbacks.boardMutation = handler
      return
    }

    if (eventName === 'boardPresence') {
      callbacks.boardPresence = handler as (event: { boardId: string; members: Array<{ userId: string }> }) => void
    }
  }),
  onreconnecting: vi.fn((handler: () => Promise<void> | void) => {
    callbacks.reconnecting = handler
  }),
  onreconnected: vi.fn((handler: () => Promise<void> | void) => {
    callbacks.reconnected = handler
  }),
  onclose: vi.fn((handler: () => Promise<void> | void) => {
    callbacks.close = handler
  }),
}

const mockBuilder = {
  withUrl: vi.fn(() => mockBuilder),
  withAutomaticReconnect: vi.fn(() => mockBuilder),
  configureLogging: vi.fn(() => mockBuilder),
  build: vi.fn(() => mockConnection),
}

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(() => mockBuilder),
  HubConnectionState: {
    Connected: 'Connected',
    Disconnected: 'Disconnected',
  },
  HttpTransportType: {
    WebSockets: 1,
  },
  LogLevel: {
    Warning: 3,
  },
}))

describe('createBoardRealtimeController', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.removeItem('taskdeck_token')
    callbacks.boardMutation = undefined
    callbacks.boardPresence = undefined
    callbacks.reconnecting = undefined
    callbacks.reconnected = undefined
    callbacks.close = undefined
    mockConnection.state = 'Disconnected'
    mockConnection.start.mockImplementation(async () => {
      mockConnection.state = 'Connected'
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('joins board stream when started', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')

    expect(mockConnection.start).toHaveBeenCalledOnce()
    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinBoard', 'board-1')
  })

  it('configures SignalR with websocket transport and negotiation enabled', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    // Use a structurally valid JWT (three base64url segments) so tokenStorage.getToken() accepts it
    const fakeJwt = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1c2VyLTEifQ.fakesig'
    localStorage.setItem('taskdeck_token', fakeJwt)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')

    const withUrlCall = mockBuilder.withUrl.mock.calls.at(0)
    expect(withUrlCall).toBeDefined()
    const [, options] = withUrlCall as [string, { accessTokenFactory: () => string; transport: number; skipNegotiation?: boolean }]
    expect(options.transport).toBe(HttpTransportType.WebSockets)
    expect(options.skipNegotiation).toBeUndefined()
    expect(options.accessTokenFactory()).toBe(fakeJwt)
  })

  it('uses an empty access token when no session token is present', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')

    const withUrlCall = mockBuilder.withUrl.mock.calls.at(0)
    expect(withUrlCall).toBeDefined()
    const [, options] = withUrlCall as [string, { accessTokenFactory: () => string }]
    expect(options.accessTokenFactory()).toBe('')
  })

  it('refreshes board when matching board mutation event arrives', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')
    await callbacks.boardMutation?.({ boardId: 'board-1' })

    expect(fetchBoard).toHaveBeenCalledWith('board-1')
  })

  it('ignores mutation events for other boards', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')
    await callbacks.boardMutation?.({ boardId: 'board-2' })

    expect(fetchBoard).not.toHaveBeenCalled()
  })

  it('emits presence snapshots for the currently subscribed board', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    const onPresenceChanged = vi.fn()
    const controller = createBoardRealtimeController({ fetchBoard, onPresenceChanged })

    await controller.start('board-1')
    callbacks.boardPresence?.({ boardId: 'board-1', members: [{ userId: 'user-1' }] })

    expect(onPresenceChanged).toHaveBeenCalledWith({
      boardId: 'board-1',
      members: [{ userId: 'user-1' }],
    })
  })

  it('ignores presence snapshots for other boards', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    const onPresenceChanged = vi.fn()
    const controller = createBoardRealtimeController({ fetchBoard, onPresenceChanged })

    await controller.start('board-1')
    callbacks.boardPresence?.({ boardId: 'board-2', members: [{ userId: 'user-1' }] })

    expect(onPresenceChanged).not.toHaveBeenCalled()
  })

  it('leaves previous board and joins next board when switched', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')
    await controller.switchBoard('board-2')

    expect(mockConnection.invoke).toHaveBeenCalledWith('LeaveBoard', 'board-1')
    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinBoard', 'board-2')
  })

  it('falls back to polling when websocket connection cannot start', async () => {
    vi.useFakeTimers()
    const fetchBoard = vi.fn(async () => undefined)
    mockConnection.start.mockRejectedValueOnce(new Error('websocket unavailable'))

    const controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-1')

    await vi.advanceTimersByTimeAsync(15000)
    expect(fetchBoard).toHaveBeenCalledWith('board-1')

    await controller.stop()
  })

  it('sends editing-card status when connected', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')
    await controller.setEditingCard('card-1')

    expect(mockConnection.invoke).toHaveBeenCalledWith('SetEditingCard', 'board-1', 'card-1')
  })

  it('starts polling on reconnecting and re-joins board when reconnected', async () => {
    vi.useFakeTimers()
    const fetchBoard = vi.fn(async () => undefined)
    const controller = createBoardRealtimeController({ fetchBoard })

    await controller.start('board-1')
    await controller.setEditingCard('card-1')
    await callbacks.reconnecting?.()
    await vi.advanceTimersByTimeAsync(15000)
    expect(fetchBoard).toHaveBeenCalledWith('board-1')

    fetchBoard.mockClear()
    await callbacks.reconnected?.()
    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinBoard', 'board-1')
    expect(mockConnection.invoke).toHaveBeenCalledWith('SetEditingCard', 'board-1', 'card-1')

    await vi.advanceTimersByTimeAsync(15000)
    expect(fetchBoard).not.toHaveBeenCalled()

    await controller.stop()
  })
})
