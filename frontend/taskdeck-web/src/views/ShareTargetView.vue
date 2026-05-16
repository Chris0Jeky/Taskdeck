<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useCaptureStore } from '../store/captureStore'
import { enqueueCapture } from '../utils/captureQueue'
import { useOnlineStatus } from '../composables/useOnlineStatus'
import * as tokenStorage from '../utils/tokenStorage'
import { isTokenExpired } from '../utils/jwt'
import type { CreateCaptureItemDto } from '../types/capture'

const router = useRouter()
const route = useRoute()
const captureStore = useCaptureStore()
const { isOnline } = useOnlineStatus()

const status = ref<'processing' | 'success' | 'queued' | 'login-required' | 'error'>('processing')
const sharedTitle = ref('')
const sharedText = ref('')
const sharedUrl = ref('')
const SHARE_CACHE_NAME = 'taskdeck-share-target'
const SHARE_CACHE_REQUEST = '/capture/share-data'

interface SharedContent {
  title: string
  text: string
  url: string
}

function buildCaptureText(title: string, text: string, url: string): string {
  const parts: string[] = []
  if (title) parts.push(title)
  if (text && text !== title) parts.push(text)
  if (url) parts.push(url)
  return parts.join('\n\n') || ''
}

function getCurrentQueueOwnerUserId(): string | null {
  return tokenStorage.getSession()?.userId ?? null
}

function hasValidSession(): boolean {
  const token = tokenStorage.getToken()
  if (!token) return false
  return !isTokenExpired(token)
}

async function safeEnqueue(dto: CreateCaptureItemDto): Promise<boolean> {
  try {
    await enqueueCapture(dto, getCurrentQueueOwnerUserId())
    return true
  } catch {
    return false
  }
}

function queryValue(value: unknown): string {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}

async function consumePostedShareTarget(): Promise<SharedContent | null> {
  if (typeof caches === 'undefined') return null

  try {
    const cache = await caches.open(SHARE_CACHE_NAME)
    const response = await cache.match(SHARE_CACHE_REQUEST)
    if (!response) return null

    await cache.delete(SHARE_CACHE_REQUEST)
    const payload = await response.json() as Partial<SharedContent>
    return {
      title: typeof payload.title === 'string' ? payload.title : '',
      text: typeof payload.text === 'string' ? payload.text : '',
      url: typeof payload.url === 'string' ? payload.url : '',
    }
  } catch {
    return null
  }
}

async function resolveSharedContent(): Promise<SharedContent> {
  const title = queryValue(route.query.title)
  const text = queryValue(route.query.text)
  const url = queryValue(route.query.url)

  if (title || text || url) {
    void router.replace({ name: 'capture-share-target', query: {} })
    return { title, text, url }
  }

  const posted = await consumePostedShareTarget()
  return posted ?? { title: '', text: '', url: '' }
}

onMounted(async () => {
  const { title, text, url } = await resolveSharedContent()

  sharedTitle.value = title
  sharedText.value = text
  sharedUrl.value = url

  const captureText = buildCaptureText(title, text, url)

  if (!captureText) {
    status.value = 'error'
    return
  }

  const dto: CreateCaptureItemDto = {
    boardId: null,
    text: captureText,
    source: 'ShareTarget',
    titleHint: title || null,
    externalRef: url || null,
  }

  if (!hasValidSession()) {
    const queued = await safeEnqueue(dto)
    status.value = queued ? 'login-required' : 'error'
    return
  }

  if (isOnline.value) {
    try {
      await captureStore.createItem(dto)
      status.value = 'success'
    } catch {
      const queued = await safeEnqueue(dto)
      status.value = queued ? 'queued' : 'error'
    }
  } else {
    const queued = await safeEnqueue(dto)
    status.value = queued ? 'queued' : 'error'
  }
})

function goToInbox() {
  void router.push({ name: 'workspace-inbox' })
}

function goToLogin() {
  void router.push({ name: 'login' })
}

function close() {
  window.close()
}
</script>

<template>
  <div class="share-target-view">
    <div class="share-target-view__card">
      <h1 class="share-target-view__title">Captured</h1>

      <div v-if="status === 'processing'" class="share-target-view__status">
        <p>Processing shared content…</p>
      </div>

      <div v-else-if="status === 'success'" class="share-target-view__status share-target-view__status--success">
        <p>Sent to Inbox</p>
        <p v-if="sharedTitle" class="share-target-view__preview">{{ sharedTitle }}</p>
      </div>

      <div v-else-if="status === 'queued'" class="share-target-view__status share-target-view__status--queued">
        <p>Queued for sync</p>
        <p class="share-target-view__detail">Will be sent when you're back online.</p>
      </div>

      <div v-else-if="status === 'login-required'" class="share-target-view__status share-target-view__status--queued">
        <p>Login required</p>
        <p class="share-target-view__detail">Content saved locally. Log in to sync it to your inbox.</p>
      </div>

      <div v-else class="share-target-view__status share-target-view__status--error">
        <p>Nothing to capture</p>
        <p class="share-target-view__detail">The shared content was empty or could not be saved.</p>
      </div>

      <div class="share-target-view__actions">
        <button
          v-if="status === 'login-required'"
          type="button"
          class="share-target-view__btn share-target-view__btn--primary"
          @click="goToLogin"
        >
          Log In
        </button>
        <button
          v-else
          type="button"
          class="share-target-view__btn share-target-view__btn--primary"
          @click="goToInbox"
        >
          Open Inbox
        </button>
        <button type="button" class="share-target-view__btn" @click="close">
          Close
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.share-target-view {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background: var(--paper, #faf9f6);
  color: var(--ink, #1a1a1a);
}

.share-target-view__card {
  width: 100%;
  max-width: 360px;
  padding: 32px 24px;
  background: var(--paper-card, #fff);
  border: 1px solid var(--line, #e0ddd8);
  border-radius: 8px;
  text-align: center;
}

.share-target-view__title {
  margin: 0 0 16px;
  font-family: var(--serif, Georgia, serif);
  font-size: 20px;
  font-weight: 500;
}

.share-target-view__status {
  margin-bottom: 24px;
}

.share-target-view__status p {
  margin: 4px 0;
}

.share-target-view__preview {
  font-family: var(--mono, monospace);
  font-size: 12px;
  color: var(--mute, #888);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.share-target-view__detail {
  font-size: 13px;
  color: var(--mute, #888);
}

.share-target-view__actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.share-target-view__btn {
  width: 100%;
  padding: 10px 16px;
  border: 1px solid var(--line, #e0ddd8);
  border-radius: 6px;
  background: transparent;
  font-family: var(--sans, system-ui);
  font-size: 14px;
  cursor: pointer;
  transition: background 0.15s;
}

.share-target-view__btn:hover {
  background: var(--paper-2, #f5f4f1);
}

.share-target-view__btn--primary {
  background: var(--ink, #1a1a1a);
  color: var(--paper, #faf9f6);
  border-color: var(--ink, #1a1a1a);
}

.share-target-view__btn--primary:hover {
  opacity: 0.9;
}
</style>
