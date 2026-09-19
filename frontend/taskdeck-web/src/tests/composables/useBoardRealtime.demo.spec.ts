import { describe, expect, it, vi } from 'vitest'

const builder = vi.hoisted(() => ({
  withUrl: vi.fn(),
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(function () { return builder }),
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
  HttpTransportType: { WebSockets: 1 },
  LogLevel: { Warning: 3, Information: 1 },
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: true }
})

import { createBoardRealtimeController } from '../../composables/useBoardRealtime'

describe('createBoardRealtimeController in demo mode', () => {
  it('does not open a SignalR connection', async () => {
    const controller = createBoardRealtimeController({ fetchBoard: vi.fn() })
    await controller.start('demo-board-1')
    await controller.switchBoard('demo-board-2')
    expect(builder.withUrl).not.toHaveBeenCalled()
  })
})
