<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useCaptureStore } from '../store/captureStore'
import { enqueueCapture } from '../utils/captureQueue'
import { useOnlineStatus } from '../composables/useOnlineStatus'
import type { CreateCaptureItemDto } from '../types/capture'

const router = useRouter()
const route = useRoute()
const captureStore = useCaptureStore()
const { isOnline } = useOnlineStatus()

const status = ref<'processing' | 'success' | 'queued' | 'error'>('processing')
const sharedTitle = ref('')
const sharedText = ref('')
const sharedUrl = ref('')

function buildCaptureText(title: string, text: string, url: string): string {
  const parts: string[] = []
  if (title) parts.push(title)
  if (text && text !== title) parts.push(text)
  if (url) parts.push(url)
  return parts.join('\n\n') || ''
}

onMounted(async () => {
  const title = (route.query.title as string) || ''
  const text = (route.query.text as string) || ''
  const url = (route.query.url as string) || ''

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

  if (isOnline.value) {
    try {
      await captureStore.createItem(dto)
      status.value = 'success'
    } catch {
      await enqueueCapture(dto)
      status.value = 'queued'
    }
  } else {
    await enqueueCapture(dto)
    status.value = 'queued'
  }
})

function goToInbox() {
  void router.push({ name: 'workspace-inbox' })
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

      <div v-else class="share-target-view__status share-target-view__status--error">
        <p>Nothing to capture</p>
        <p class="share-target-view__detail">The shared content was empty.</p>
      </div>

      <div class="share-target-view__actions">
        <button type="button" class="share-target-view__btn share-target-view__btn--primary" @click="goToInbox">
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
