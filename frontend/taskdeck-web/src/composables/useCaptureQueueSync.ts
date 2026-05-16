import { ref, onMounted, onUnmounted } from 'vue'
import { getAllPending, dequeueCapture, incrementRetry } from '../utils/captureQueue'
import type { QueuedCapture } from '../utils/captureQueue'
import { useCaptureStore } from '../store/captureStore'
import { useOnlineStatus } from './useOnlineStatus'
import { logError } from '../utils/errorReporting'

const MAX_RETRIES = 5
const SYNC_TAG = 'taskdeck-capture-sync'

let replayInProgress = false

export function useCaptureQueueSync() {
  const { isOnline } = useOnlineStatus()
  const pendingCount = ref(0)
  const syncing = ref(false)
  let onlineHandler: (() => void) | null = null

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

      for (const entry of pending) {
        if (entry.retryCount >= MAX_RETRIES) {
          logError('Capture queue: discarding entry after max retries', {
            id: entry.id,
            queuedAt: entry.queuedAt,
          })
          await dequeueCapture(entry.id)
          pendingCount.value--
          continue
        }

        try {
          await captureStore.createItem(entry.dto)
          await dequeueCapture(entry.id)
          replayed++
          pendingCount.value--
        } catch {
          await incrementRetry(entry.id)
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

  async function refreshCount(): Promise<void> {
    const pending = await getAllPending()
    pendingCount.value = pending.length
  }

  onMounted(() => {
    void refreshCount()
    void registerBackgroundSync()

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
