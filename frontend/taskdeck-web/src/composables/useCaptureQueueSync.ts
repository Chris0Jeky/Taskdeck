import { ref, onMounted, onUnmounted } from 'vue'
import { getAllPending, dequeueCapture, incrementRetry, markCaptureFailed } from '../utils/captureQueue'
import type { QueuedCapture } from '../utils/captureQueue'
import { useCaptureStore } from '../store/captureStore'
import { useSessionStore } from '../store/sessionStore'
import { useOnlineStatus } from './useOnlineStatus'
import { logError, logWarn } from '../utils/errorReporting'

const MAX_RETRIES = 5
const SYNC_TAG = 'taskdeck-capture-sync'
const SYNC_MESSAGE_TYPE = 'taskdeck:capture-sync'

let replayInProgress = false

function getErrorStatus(error: unknown): number | null {
  if (!error || typeof error !== 'object') return null
  const response = (error as { response?: unknown }).response
  if (!response || typeof response !== 'object') return null
  const status = (response as { status?: unknown }).status
  return typeof status === 'number' ? status : null
}

function describeCaptureError(error: unknown): string {
  const status = getErrorStatus(error)
  if (status !== null) return `HTTP ${status}`
  if (error instanceof Error && error.message.trim()) return error.message
  return 'Unknown capture sync error'
}

function isTransientCaptureError(error: unknown): boolean {
  const status = getErrorStatus(error)
  if (status === null) return true
  if (status === 408 || status === 429) return true
  return status >= 500 && status < 600 && status !== 501 && status !== 505
}

function isCaptureSyncMessage(event: MessageEvent<unknown>): boolean {
  const data = event.data
  return !!data && typeof data === 'object' && (data as { type?: unknown }).type === SYNC_MESSAGE_TYPE
}

export function useCaptureQueueSync() {
  const { isOnline } = useOnlineStatus()
  const pendingCount = ref(0)
  const syncing = ref(false)
  let onlineHandler: (() => void) | null = null
  let serviceWorkerMessageHandler: ((event: MessageEvent<unknown>) => void) | null = null

  async function replayQueue(): Promise<number> {
    if (replayInProgress) return 0
    replayInProgress = true
    syncing.value = true
    let replayed = 0

    try {
      const pending = await getAllPending()
      pendingCount.value = pending.length

      if (pending.length === 0) return 0

      const captureStore = useCaptureStore()
      const sessionStore = useSessionStore()
      const currentUserId = sessionStore.userId

      for (const entry of pending) {
        if (entry.retryCount >= MAX_RETRIES) {
          logWarn('Capture queue: parking entry after max retries', {
            id: entry.id,
            queuedAt: entry.queuedAt,
          })
          await markCaptureFailed(entry.id, 'Max retry count reached')
          pendingCount.value--
          continue
        }

        if (!entry.ownerUserId) {
          logWarn('Capture queue: parking entry without an owner', {
            id: entry.id,
            queuedAt: entry.queuedAt,
          })
          await markCaptureFailed(entry.id, 'Missing queue owner')
          pendingCount.value--
          continue
        }

        if (!currentUserId || entry.ownerUserId !== currentUserId) {
          logWarn('Capture queue: skipping entry for a different session', {
            id: entry.id,
            queuedAt: entry.queuedAt,
          })
          continue
        }

        try {
          await captureStore.createItem(entry.dto)
          await dequeueCapture(entry.id)
          replayed++
          pendingCount.value--
        } catch (error) {
          if (isTransientCaptureError(error)) {
            await incrementRetry(entry.id)
          } else {
            await markCaptureFailed(entry.id, describeCaptureError(error))
            pendingCount.value--
          }
        }
      }
    } catch (error) {
      logError('Capture queue replay failed:', error)
    } finally {
      replayInProgress = false
      syncing.value = false
    }

    return replayed
  }

  async function registerBackgroundSync(): Promise<boolean> {
    if (!('serviceWorker' in navigator)) return false
    try {
      const reg = await navigator.serviceWorker.ready
      if ('sync' in reg) {
        await (reg as ServiceWorkerRegistration & { sync: { register(tag: string): Promise<void> } }).sync.register(SYNC_TAG)
        return true
      }
    } catch {
      // Background Sync not supported — fall back to online event replay
    }
    return false
  }

  function registerServiceWorkerMessageReplay() {
    if (!('serviceWorker' in navigator)) return
    serviceWorkerMessageHandler = (event: MessageEvent<unknown>) => {
      if (isCaptureSyncMessage(event)) {
        void replayQueue()
      }
    }
    navigator.serviceWorker.addEventListener('message', serviceWorkerMessageHandler)
  }

  async function refreshCount(): Promise<void> {
    const pending = await getAllPending()
    pendingCount.value = pending.length
  }

  onMounted(() => {
    void refreshCount()
    void registerBackgroundSync()
    registerServiceWorkerMessageReplay()

    onlineHandler = () => {
      if (isOnline.value) {
        void replayQueue()
      }
    }
    window.addEventListener('online', onlineHandler)

    if (isOnline.value) {
      void replayQueue()
    }
  })

  onUnmounted(() => {
    if (onlineHandler) {
      window.removeEventListener('online', onlineHandler)
    }
    if (serviceWorkerMessageHandler && 'serviceWorker' in navigator) {
      navigator.serviceWorker.removeEventListener('message', serviceWorkerMessageHandler)
    }
  })

  return {
    pendingCount,
    syncing,
    replayQueue,
    registerBackgroundSync,
    refreshCount,
  }
}

export function getPendingCaptures(): Promise<QueuedCapture[]> {
  return getAllPending()
}
