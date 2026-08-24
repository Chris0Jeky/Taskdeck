<template>
  <div class="fixed top-4 right-4 z-50 flex flex-col gap-2 pointer-events-none">
    <div
      class="sr-only"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      data-toast-polite-announcer
    >{{ politeAnnouncement }}</div>
    <TransitionGroup name="toast">
      <div
        v-for="toast in toastStore.toasts"
        :key="toast.id"
        :data-toast-id="toast.id"
        :class="[
          'pointer-events-auto',
          'min-w-80 max-w-md',
          'px-4 py-3 rounded-lg shadow-lg',
          'flex items-start gap-3',
          'transition-all duration-300',
          toastClass(toast.type),
        ]"
        :role="toast.type === 'error' ? 'alert' : undefined"
        :aria-live="toast.type === 'error' ? 'assertive' : undefined"
        :aria-atomic="toast.type === 'error' ? 'true' : undefined"
      >
        <!-- Icon -->
        <div class="flex-shrink-0" aria-hidden="true">
          <svg
            v-if="toast.type === 'success'"
            class="w-5 h-5"
            fill="currentColor"
            viewBox="0 0 20 20"
          >
            <path
              fill-rule="evenodd"
              d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z"
              clip-rule="evenodd"
            />
          </svg>
          <svg
            v-else-if="toast.type === 'error'"
            class="w-5 h-5"
            fill="currentColor"
            viewBox="0 0 20 20"
          >
            <path
              fill-rule="evenodd"
              d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
              clip-rule="evenodd"
            />
          </svg>
          <svg
            v-else-if="toast.type === 'warning'"
            class="w-5 h-5"
            fill="currentColor"
            viewBox="0 0 20 20"
          >
            <path
              fill-rule="evenodd"
              d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z"
              clip-rule="evenodd"
            />
          </svg>
          <svg
            v-else
            class="w-5 h-5"
            fill="currentColor"
            viewBox="0 0 20 20"
          >
            <path
              fill-rule="evenodd"
              d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z"
              clip-rule="evenodd"
            />
          </svg>
        </div>

        <!-- Message -->
        <div class="flex-1 text-sm font-medium min-w-0">
          <div>{{ toast.message }}</div>
          <div v-if="toast.type === 'error'" class="mt-2 flex flex-wrap gap-2 text-xs font-normal">
            <button
              v-if="toast.details"
              type="button"
              class="underline underline-offset-2 hover:opacity-70"
              :aria-expanded="expanded[toast.id] ?? false"
              :aria-controls="expanded[toast.id] ? detailsId(toast.id) : undefined"
              @click="toggleDetails(toast.id)"
            >
              {{ expanded[toast.id] ? t('shell.toast.receipt.hideDetails') : t('shell.toast.receipt.showDetails') }}
            </button>
            <button
              type="button"
              class="underline underline-offset-2 hover:opacity-70"
              @click="copyReceipt(toast)"
            >
              {{ copyState[toast.id] === 'copied' ? t('shell.toast.receipt.copied') : copyState[toast.id] === 'failed' ? t('shell.toast.receipt.copyFailed') : t('shell.toast.receipt.copyDetails') }}
            </button>
          </div>
          <pre
            v-if="toast.type === 'error' && toast.details && expanded[toast.id]"
            :id="detailsId(toast.id)"
            class="mt-2 max-h-40 overflow-auto whitespace-pre-wrap rounded bg-black/5 p-2 text-xs font-normal"
            tabindex="0"
            role="region"
            :aria-label="t('shell.toast.receipt.errorDetails', { message: toast.message })"
          >{{ toast.details }}</pre>
        </div>

        <!-- Close button -->
        <button
          @click="toastStore.remove(toast.id)"
          class="flex-shrink-0 hover:opacity-70 transition-opacity"
          :aria-label="t('shell.toast.receipt.dismissNotification')"
        >
          <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20" aria-hidden="true">
            <path
              fill-rule="evenodd"
              d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
              clip-rule="evenodd"
            />
          </svg>
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

<script setup lang="ts">
import { nextTick, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { copyToastReceipt, useToastStore, type Toast } from '../../store/toastStore'

const toastStore = useToastStore()
const { t } = useI18n()
const expanded = reactive<Record<string, boolean>>({})
const copyState = reactive<Record<string, 'copied' | 'failed' | undefined>>({})
const politeAnnouncement = ref('')
let initialToastIds: Set<string> | null = null

watch(
  () => toastStore.toasts.map(({ id, message, type }) => ({ id, message, type })),
  async (current, previous) => {
    if (initialToastIds === null) {
      initialToastIds = new Set(current.map(({ id }) => id))
      return
    }

    const previousIds = new Set((previous ?? []).map(({ id }) => id))
    const added = current.filter(
      ({ id, type }) =>
        type !== 'error' && !previousIds.has(id) && !initialToastIds!.has(id),
    )

    if (added.length === 0) {
      if (!current.some(({ type }) => type !== 'error')) politeAnnouncement.value = ''
      return
    }

    politeAnnouncement.value = ''
    await nextTick()
    politeAnnouncement.value = added.map(({ message }) => message).join(' ')
  },
  { flush: 'post', immediate: true },
)

function detailsId(id: string): string {
  return `toast-details-${id}`
}

function toggleDetails(id: string) {
  expanded[id] = !expanded[id]
}

async function copyReceipt(toast: Toast) {
  copyState[toast.id] = (await copyToastReceipt(toast)) ? 'copied' : 'failed'
}

function toastClass(type: string): string {
  switch (type) {
    case 'success':
      return 'bg-green-50 text-green-800 border border-green-200'
    case 'error':
      return 'bg-red-50 text-red-800 border border-red-200'
    case 'warning':
      return 'bg-yellow-50 text-yellow-800 border border-yellow-200'
    case 'info':
    default:
      return 'bg-blue-50 text-blue-800 border border-blue-200'
  }
}
</script>

<style scoped>
.toast-enter-active,
.toast-leave-active {
  transition: all 0.3s ease;
}

.toast-enter-from {
  opacity: 0;
  transform: translateX(100%);
}

.toast-leave-to {
  opacity: 0;
  transform: translateX(100%);
}

.toast-move {
  transition: transform 0.3s ease;
}
</style>
