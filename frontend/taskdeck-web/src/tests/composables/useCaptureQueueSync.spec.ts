import 'fake-indexeddb/auto'
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { useCaptureQueueSync } from '../../composables/useCaptureQueueSync'
import {
  claimCaptureForReplay,
  dequeueCapture,
  enqueueCapture,
  getAllPending,
  getAllQueuedCaptures,
  incrementRetry,
} from '../../utils/captureQueue'
import type { CreateCaptureItemDto } from '../../types/capture'

const mockOnline = vi.hoisted(() => ({ value: true }))
const sessionMock = vi.hoisted(() => ({ userId: 'user-1' as string | null }))
const createItemMock = vi.hoisted(() => vi.fn())

vi.mock('../../composables/useOnlineStatus', () => ({
  useOnlineStatus: () => ({ isOnline: mockOnline }),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionMock,
}))

vi.mock('../../store/captureStore', () => ({
  useCaptureStore: () => ({ createItem: createItemMock }),
}))

function makeDto(text = 'Queued capture'): CreateCaptureItemDto {
  return { boardId: null, text, source: 'ShareTarget', titleHint: null, externalRef: null }
}

async function clearQueue() {
  const queued = await getAllQueuedCaptures()
  for (const entry of queued) {
    await dequeueCapture(entry.id)
  }
}

function mountSyncComposable() {
  let exposed: ReturnType<typeof useCaptureQueueSync> | null = null
  const Host = defineComponent({
    setup() {
      exposed = useCaptureQueueSync()
      return () => null
    },
  })
  const wrapper = mount(Host)
  if (!exposed) {
    throw new Error('Composable did not mount')
  }
  return { wrapper, sync: exposed }
}

describe('useCaptureQueueSync', () => {
  beforeEach(async () => {
    await clearQueue()
    createItemMock.mockReset()
    createItemMock.mockResolvedValue({ id: 'capture-1' })
    sessionMock.userId = 'user-1'
    mockOnline.value = false
    Reflect.deleteProperty(navigator, 'serviceWorker')
  })

  afterEach(async () => {
    await clearQueue()
  })

  it('does not replay captures queued for a different user', async () => {
    await enqueueCapture(makeDto('Other user'), 'user-2')
    const { sync } = mountSyncComposable()

    await sync.replayQueue()

    expect(createItemMock).not.toHaveBeenCalled()
    expect(await getAllPending()).toHaveLength(1)
  })

  it('reports only captures visible to the active user', async () => {
    await enqueueCapture(makeDto('Mine'), 'user-1')
    await enqueueCapture(makeDto('Other user'), 'user-2')
    await enqueueCapture(makeDto('Ownerless'))

    const { sync } = mountSyncComposable()
    await flushPromises()

    await vi.waitFor(() => {
      expect(sync.pendingCount.value).toBe(2)
    })
  })

  it('claims an ownerless login-required capture once a user session exists', async () => {
    await enqueueCapture(makeDto('Needs login'))
    const { sync } = mountSyncComposable()

    const replayed = await sync.replayQueue()

    expect(replayed).toBe(1)
    expect(createItemMock).toHaveBeenCalledWith(expect.objectContaining({ text: 'Needs login' }))
    expect(await getAllPending()).toHaveLength(0)
  })

  it('keeps ownerless captures pending until a user session exists', async () => {
    sessionMock.userId = null
    await enqueueCapture(makeDto('Needs login'))
    const { sync } = mountSyncComposable()

    const replayed = await sync.replayQueue()

    expect(replayed).toBe(0)
    expect(createItemMock).not.toHaveBeenCalled()
    const pending = await getAllPending()
    expect(pending).toHaveLength(1)
    expect(pending[0]).toMatchObject({
      ownerUserId: null,
      status: 'pending',
    })
  })

  it('parks non-transient failures without deleting the queued payload', async () => {
    createItemMock.mockRejectedValueOnce({ response: { status: 400 } })
    await enqueueCapture(makeDto('Invalid payload'), 'user-1')
    const { sync } = mountSyncComposable()

    const replayed = await sync.replayQueue()

    expect(replayed).toBe(0)
    expect(await getAllPending()).toHaveLength(0)
    const queued = await getAllQueuedCaptures()
    expect(queued).toHaveLength(1)
    expect(queued[0]).toMatchObject({
      status: 'failed',
      lastError: 'HTTP 400',
      dto: expect.objectContaining({ text: 'Invalid payload' }),
    })
  })

  it('keeps transient failures pending and increments retry count', async () => {
    createItemMock.mockRejectedValueOnce({ response: { status: 503 } })
    await enqueueCapture(makeDto('Try later'), 'user-1')
    const { sync } = mountSyncComposable()

    await sync.replayQueue()

    const pending = await getAllPending()
    expect(pending).toHaveLength(1)
    expect(pending[0].retryCount).toBe(1)
  })

  it('does not replay a row already claimed by another tab', async () => {
    const id = await enqueueCapture(makeDto('Already claimed'), 'user-1')
    await claimCaptureForReplay(id, 'user-1')
    const { sync } = mountSyncComposable()

    const replayed = await sync.replayQueue()

    expect(replayed).toBe(0)
    expect(createItemMock).not.toHaveBeenCalled()
    expect(await getAllPending()).toHaveLength(1)
  })

  it('parks entries at the retry cap instead of discarding them', async () => {
    const id = await enqueueCapture(makeDto('Retry capped'), 'user-1')
    for (let i = 0; i < 5; i++) {
      await incrementRetry(id)
    }
    const { sync } = mountSyncComposable()

    await sync.replayQueue()

    expect(createItemMock).not.toHaveBeenCalled()
    expect(await getAllPending()).toHaveLength(0)
    const queued = await getAllQueuedCaptures()
    expect(queued[0]).toMatchObject({
      id,
      status: 'failed',
      lastError: 'Max retry count reached',
    })
  })

  it('replays when the service worker forwards a client replay message', async () => {
    await enqueueCapture(makeDto('From sync event'), 'user-1')
    let messageHandler: ((event: MessageEvent<unknown>) => void) | null = null
    const serviceWorker = {
      ready: Promise.resolve({ sync: { register: vi.fn() } }),
      controller: { postMessage: vi.fn() },
      addEventListener: vi.fn((_event: string, handler: (event: MessageEvent<unknown>) => void) => {
        messageHandler = handler
      }),
      removeEventListener: vi.fn(),
    }
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: serviceWorker,
    })

    const { wrapper } = mountSyncComposable()
    await flushPromises()
    expect(serviceWorker.addEventListener).toHaveBeenCalledWith('message', expect.any(Function))
    messageHandler?.({ data: { type: 'taskdeck:capture-sync' } } as MessageEvent<unknown>)

    await vi.waitFor(() => {
      expect(createItemMock).toHaveBeenCalledWith(expect.objectContaining({ text: 'From sync event' }))
    })
    expect(await getAllPending()).toHaveLength(0)

    wrapper.unmount()
    expect(serviceWorker.removeEventListener).toHaveBeenCalledWith('message', expect.any(Function))
  })
})
