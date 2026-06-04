import { ref, onMounted, onUnmounted } from 'vue'
import {
  claimCaptureForReplay,
  dequeueCapture,
  getAllPending,
  getPendingCountForOwner,
  getPendingForOwner,
  incrementRetry,
  markCaptureFailed,
} from '../utils/captureQueue'
import type { QueuedCapture } from '../utils/captureQueue'
import { useCaptureStore } from '../store/captureStore'
import { useSessionStore } from '../store/sessionStore'
import { useOnlineStatus } from './useOnlineStatus'
import { logError, logWarn } from '../utils/errorReporting'

const MAX_RETRIES = 5
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

  function getCurrentUserId(): string | null {
    return useSessionStore().userId
  }

  async function refreshCount(): Promise<void> {
    pendingCount.value = await getPendingCountForOwner(getCurrentUserId())
  }

  async function replayQueue(): Promise<number> {
    if (replayInProgress) return 0
    replayInProgress = true
    syncing.value = true
    let replayed = 0

    try {
      const captureStore = useCaptureStore()
      const currentUserId = getCurrentUserId()
      const pending = await getPendingForOwner(currentUserId)
      pendingCount.value = pending.length

      if (!currentUserId || pending.length === 0) return 0

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

        const claimed = await claimCaptureForReplay(entry.id, currentUserId)
        if (!claimed) {
          logWarn('Capture queue: skipping entry claimed by another replay worker', {
            id: entry.id,
            queuedAt: entry.queuedAt,
          })
          continue
        }

        let remoteCreateSucceeded = false
        try {
          await captureStore.createItem(claimed.dto)
          remoteCreateSucceeded = true
        } catch (error) {
          if (isTransientCaptureError(error)) {
            await incrementRetry(claimed.id)
            continue
          }

          await markCaptureFailed(claimed.id, describeCaptureError(error))
          pendingCount.value--
          continue
        }

        try {
          await dequeueCapture(claimed.id)
        } catch (error) {
          logError('Capture queue dequeue failed after remote create:', error)
          if (remoteCreateSucceeded) {
            await markCaptureFailed(claimed.id, 'Remote create succeeded but local dequeue failed')
            pendingCount.value--
          }
          continue
        }

        replayed++
        pendingCount.value--
      }
    } catch (error) {
      logError('Capture queue replay failed:', error)
    } finally {
      try {
        await refreshCount()
      } catch (error) {
        logError('Capture queue count refresh failed:', error)
      }
      replayInProgress = false
      syncing.value = false
    }

    return replayed
  }

  function requestServiceWorkerQueueReplay() {
    if (!('serviceWorker' in navigator)) return
    const controller = navigator.serviceWorker.controller
    controller?.postMessage({ type: SYNC_MESSAGE_TYPE })
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

  onMounted(() => {
    refreshCount().catch((err) => logWarn('Capture queue count refresh failed on mount:', err))
    registerServiceWorkerMessageReplay()

    onlineHandler = () => {
      if (isOnline.value) {
        void replayQueue()
        requestServiceWorkerQueueReplay()
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
    refreshCount,
  }
}

export function getPendingCaptures(): Promise<QueuedCapture[]> {
  return getAllPending()
}
