import type { CreateCaptureItemDto } from '../types/capture'

const DB_NAME = 'taskdeck-capture-queue'
const DB_VERSION = 1
const STORE_NAME = 'pending-captures'

export type QueuedCaptureStatus = 'pending' | 'failed'

export interface QueuedCapture {
  id: string
  dto: CreateCaptureItemDto
  queuedAt: string
  retryCount: number
  ownerUserId: string | null
  status: QueuedCaptureStatus
  failedAt?: string
  lastError?: string
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION)
    request.onupgradeneeded = () => {
      const db = request.result
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME, { keyPath: 'id' })
      }
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
    request.onblocked = () => reject(new Error('IndexedDB blocked by another connection'))
  })
}

export async function enqueueCapture(dto: CreateCaptureItemDto, ownerUserId: string | null = null): Promise<string> {
  const db = await openDb()
  const id = crypto.randomUUID()
  const entry: QueuedCapture = {
    id,
    dto,
    queuedAt: new Date().toISOString(),
    retryCount: 0,
    ownerUserId,
    status: 'pending',
  }

  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readwrite')
    tx.objectStore(STORE_NAME).put(entry)
    tx.oncomplete = () => {
      db.close()
      resolve(id)
    }
    tx.onerror = () => {
      db.close()
      reject(tx.error)
    }
  })
}

export async function dequeueCapture(id: string): Promise<void> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readwrite')
    tx.objectStore(STORE_NAME).delete(id)
    tx.oncomplete = () => {
      db.close()
      resolve()
    }
    tx.onerror = () => {
      db.close()
      reject(tx.error)
    }
  })
}

export async function getAllPending(): Promise<QueuedCapture[]> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readonly')
    const request = tx.objectStore(STORE_NAME).getAll()
    request.onsuccess = () => {
      db.close()
      const pending = (request.result as QueuedCapture[])
        .filter((entry) => (entry.status ?? 'pending') === 'pending')
        .map((entry) => ({
          ...entry,
          ownerUserId: entry.ownerUserId ?? null,
          status: entry.status ?? 'pending',
        }))
      resolve(pending.sort((a, b) => a.queuedAt.localeCompare(b.queuedAt)))
    }
    request.onerror = () => {
      db.close()
      reject(request.error)
    }
  })
}

export async function getAllQueuedCaptures(): Promise<QueuedCapture[]> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readonly')
    const request = tx.objectStore(STORE_NAME).getAll()
    request.onsuccess = () => {
      db.close()
      const queued = (request.result as QueuedCapture[]).map((entry) => ({
        ...entry,
        ownerUserId: entry.ownerUserId ?? null,
        status: entry.status ?? 'pending',
      }))
      resolve(queued.sort((a, b) => a.queuedAt.localeCompare(b.queuedAt)))
    }
    request.onerror = () => {
      db.close()
      reject(request.error)
    }
  })
}

export async function incrementRetry(id: string): Promise<void> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readwrite')
    const store = tx.objectStore(STORE_NAME)
    const getReq = store.get(id)
    getReq.onsuccess = () => {
      const entry = getReq.result as QueuedCapture | undefined
      if (entry) {
        entry.retryCount += 1
        entry.status = entry.status ?? 'pending'
        entry.ownerUserId = entry.ownerUserId ?? null
        store.put(entry)
      }
    }
    tx.oncomplete = () => {
      db.close()
      resolve()
    }
    tx.onerror = () => {
      db.close()
      reject(tx.error)
    }
  })
}

export async function markCaptureFailed(id: string, lastError: string): Promise<void> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readwrite')
    const store = tx.objectStore(STORE_NAME)
    const getReq = store.get(id)
    getReq.onsuccess = () => {
      const entry = getReq.result as QueuedCapture | undefined
      if (entry) {
        entry.status = 'failed'
        entry.failedAt = new Date().toISOString()
        entry.lastError = lastError
        entry.ownerUserId = entry.ownerUserId ?? null
        store.put(entry)
      }
    }
    tx.oncomplete = () => {
      db.close()
      resolve()
    }
    tx.onerror = () => {
      db.close()
      reject(tx.error)
    }
  })
}

export async function assignCaptureOwner(id: string, ownerUserId: string): Promise<void> {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readwrite')
    const store = tx.objectStore(STORE_NAME)
    const getReq = store.get(id)
    getReq.onsuccess = () => {
      const entry = getReq.result as QueuedCapture | undefined
      if (entry && (entry.status ?? 'pending') === 'pending' && !entry.ownerUserId) {
        entry.ownerUserId = ownerUserId
        entry.status = 'pending'
        store.put(entry)
      }
    }
    tx.oncomplete = () => {
      db.close()
      resolve()
    }
    tx.onerror = () => {
      db.close()
      reject(tx.error)
    }
  })
}

export async function getPendingCount(): Promise<number> {
  const pending = await getAllPending()
  return pending.length
}
