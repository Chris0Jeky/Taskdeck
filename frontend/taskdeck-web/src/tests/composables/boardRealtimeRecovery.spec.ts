import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createBoardRealtimeController } from '../../composables/useBoardRealtime'

const hub = vi.hoisted(() => ({
  lifecycle: {} as Record<string, () => Promise<void> | void>,
  events: {} as Record<string, (event: { boardId: string }) => void>,
  state: 'Disconnected',
  start: vi.fn<() => Promise<void>>(),
  stop: vi.fn<() => Promise<void>>(),
  invoke: vi.fn<(method: string, boardId: string, cardId?: string | null) => Promise<void>>(),
}))
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))
vi.mock('../../utils/errorReporting', () => ({ logWarn: vi.fn() }))
vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
  HttpTransportType: { WebSockets: 1 },
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() {
      return {
        get state() { return hub.state },
        start: hub.start,
        stop: hub.stop,
        invoke: hub.invoke,
        on: (name: string, callback: (event: { boardId: string }) => void) => { hub.events[name] = callback },
        onreconnecting: (callback: () => Promise<void> | void) => { hub.lifecycle.reconnecting = callback },
        onreconnected: (callback: () => Promise<void> | void) => { hub.lifecycle.reconnected = callback },
        onclose: (callback: () => Promise<void> | void) => { hub.lifecycle.close = callback },
      }
    }
  },
}))

function deferred() {
  let resolve!: () => void
  const promise = new Promise<void>((done) => { resolve = done })
  return { promise, resolve }
}

async function disconnect() {
  hub.state = 'Reconnecting'
  await hub.lifecycle.reconnecting!()
}

function reconnect() {
  hub.state = 'Connected'
  return hub.lifecycle.reconnected!()
}

describe('board realtime recovery (#3319)', () => {
  let controller: ReturnType<typeof createBoardRealtimeController>

  beforeEach(() => {
    vi.useFakeTimers()
    hub.lifecycle = {}
    hub.events = {}
    hub.state = 'Disconnected'
    hub.start.mockReset().mockImplementation(async () => { hub.state = 'Connected' })
    hub.stop.mockReset().mockImplementation(async () => { hub.state = 'Disconnected' })
    hub.invoke.mockReset().mockResolvedValue(undefined)
  })

  afterEach(async () => {
    await controller?.stop()
    vi.useRealTimers()
  })

  it('catches up after a disconnect shorter than the fallback interval', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await disconnect()
    await vi.advanceTimersByTimeAsync(1000)
    expect(fetchBoard).not.toHaveBeenCalled()
    await reconnect()
    expect(fetchBoard).toHaveBeenCalledExactlyOnceWith('board-a', {
      intent: 'background',
      afterActive: true,
    })
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenCalledTimes(1)
  })

  it('retains polling and contains a failed rejoin without blindly retrying JoinBoard', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await disconnect()
    hub.invoke.mockClear().mockImplementation(async (method) => {
      if (method === 'JoinBoard') throw new Error('synthetic join failure')
    })
    await expect(reconnect()).resolves.toBeUndefined()
    expect(fetchBoard).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenCalledExactlyOnceWith('board-a', { intent: 'background' })
    expect(hub.invoke.mock.calls.filter(([method]) => method === 'JoinBoard')).toHaveLength(1)
  })

  it('does not let optional presence restoration failure suppress catch-up', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await controller.setEditingCard('card-a')
    await disconnect()
    hub.invoke.mockImplementation(async (method) => {
      if (method === 'SetEditingCard') throw new Error('synthetic presence failure')
    })
    await expect(reconnect()).resolves.toBeUndefined()
    expect(fetchBoard).toHaveBeenCalledExactlyOnceWith('board-a', {
      intent: 'background',
      afterActive: true,
    })
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenCalledTimes(1)
  })

  it.each(['switch', 'stop'] as const)('does not catch up an abandoned board after %s during rejoin', async (change) => {
    const fetchBoard = vi.fn(async () => undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await disconnect()
    const joined = deferred()
    const started = deferred()
    hub.invoke.mockImplementation(async (method, boardId) => {
      if (method === 'JoinBoard' && boardId === 'board-a') {
        started.resolve()
        await joined.promise
      }
    })
    const recovery = reconnect()
    await started.promise
    const changed = change === 'switch' ? controller.switchBoard('board-b') : controller.stop()
    joined.resolve()
    await recovery
    await changed
    expect(fetchBoard).not.toHaveBeenCalledWith('board-a', { intent: 'background' })
    if (change === 'switch') {
      expect(fetchBoard).toHaveBeenCalledExactlyOnceWith('board-b', {
        intent: 'background',
        afterActive: true,
      })
    } else {
      expect(fetchBoard).not.toHaveBeenCalled()
    }
  })

  it.each(['mutation', 'fallback'] as const)('retains one catch-up behind an older in-flight %s refresh', async (source) => {
    const olderRead = deferred()
    const fetchBoard = vi.fn<() => Promise<void>>()
      .mockImplementationOnce(() => olderRead.promise).mockResolvedValue(undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    if (source === 'mutation') {
      hub.events.boardMutation!({ boardId: 'board-a' })
      await vi.advanceTimersByTimeAsync(300)
      await disconnect()
    } else {
      await disconnect()
      await vi.advanceTimersByTimeAsync(30000)
    }
    expect(fetchBoard).toHaveBeenCalledTimes(1)
    await reconnect()
    expect(fetchBoard).toHaveBeenCalledTimes(1)
    olderRead.resolve()
    await vi.advanceTimersByTimeAsync(0)
    expect(fetchBoard).toHaveBeenCalledTimes(2)
    expect(fetchBoard).toHaveBeenLastCalledWith('board-a', {
      intent: 'background',
      afterActive: true,
    })
  })

  it('keeps polling the latest board when its transferred rejoin fails', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await disconnect()
    const joined = deferred()
    const started = deferred()
    hub.invoke.mockImplementation(async (method, boardId) => {
      if (method !== 'JoinBoard') return
      if (boardId === 'board-a') { started.resolve(); await joined.promise }
      else throw new Error('synthetic latest-board join failure')
    })
    const recovery = reconnect()
    await started.promise
    const changed = controller.switchBoard('board-b').catch((error: unknown) => error)
    joined.resolve()
    await recovery
    expect(await changed).toBeInstanceOf(Error)
    expect(fetchBoard).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenCalledExactlyOnceWith('board-b', { intent: 'background' })
  })

  it('contains a failed catch-up read and still responds to later mutations', async () => {
    const fetchBoard = vi.fn<() => Promise<void>>()
      .mockRejectedValueOnce(new Error('synthetic read failure')).mockResolvedValue(undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await disconnect()
    await reconnect()
    await vi.advanceTimersByTimeAsync(0)
    expect(fetchBoard).toHaveBeenCalledTimes(1)
    hub.events.boardMutation!({ boardId: 'board-a' })
    await vi.advanceTimersByTimeAsync(300)
    expect(fetchBoard).toHaveBeenCalledTimes(2)
  })

  it('does not retire fallback if another disconnect happens before rejoin settles', async () => {
    const fetchBoard = vi.fn(async () => undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await disconnect()
    const joined = deferred()
    const started = deferred()
    hub.invoke.mockImplementation(async (method) => {
      if (method === 'JoinBoard') { started.resolve(); await joined.promise }
    })
    const recovery = reconnect()
    await started.promise
    await disconnect()
    joined.resolve()
    await recovery
    expect(fetchBoard).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenCalledExactlyOnceWith('board-a', { intent: 'background' })
  })

  it('does not let an older catch-up discharge a newer reconnect recovery', async () => {
    const firstCatchUp = deferred()
    const secondJoin = deferred()
    let joinCount = 0
    const fetchBoard = vi.fn<() => Promise<void>>()
      .mockImplementationOnce(() => firstCatchUp.promise)
      .mockResolvedValue(undefined)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await disconnect()

    hub.invoke.mockImplementation(async (method) => {
      if (method === 'JoinBoard') {
        joinCount += 1
        if (joinCount === 2) await secondJoin.promise
      }
    })

    await reconnect()
    expect(fetchBoard).toHaveBeenCalledExactlyOnceWith('board-a', {
      intent: 'background',
      afterActive: true,
    })

    await disconnect()
    const newerRecovery = reconnect()
    await vi.waitFor(() => expect(joinCount).toBe(2))

    firstCatchUp.resolve()
    await vi.advanceTimersByTimeAsync(0)
    expect(fetchBoard).toHaveBeenCalledTimes(1)

    secondJoin.resolve()
    await newerRecovery
    await vi.advanceTimersByTimeAsync(0)
    expect(fetchBoard).toHaveBeenCalledTimes(2)
    expect(fetchBoard).toHaveBeenLastCalledWith('board-a', {
      intent: 'background',
      afterActive: true,
    })
  })

  it('retains fallback after a handled recovery failure', async () => {
    const fetchBoard = vi.fn<() => Promise<boolean>>()
      .mockResolvedValueOnce(false)
      .mockResolvedValue(true)
    controller = createBoardRealtimeController({ fetchBoard })
    await controller.start('board-a')
    await disconnect()
    await reconnect()
    await vi.advanceTimersByTimeAsync(0)

    expect(fetchBoard).toHaveBeenCalledExactlyOnceWith('board-a', {
      intent: 'background',
      afterActive: true,
    })
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenLastCalledWith('board-a', { intent: 'background' })
    await vi.advanceTimersByTimeAsync(30000)
    expect(fetchBoard).toHaveBeenCalledTimes(2)
  })
})
