<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { copyToastReceipt, useToastStore, type Toast } from '../../store/toastStore'

const props = withDefaults(defineProps<{ variant?: 'legacy' | 'paper' }>(), {
  variant: 'legacy',
})

const toastStore = useToastStore()
const { t } = useI18n()
const open = ref(false)
const expanded = reactive<Record<string, boolean>>({})
const copyState = reactive<Record<string, 'copied' | 'failed' | undefined>>({})
const panelClass = computed(() => props.variant === 'paper'
  ? 'border-[var(--overdue)] bg-[var(--overdue-tint)] text-[var(--ink)]'
  : 'border-red-200 bg-red-50 text-red-800')

function detailsId(id: string): string {
  return `toast-overflow-details-${id}`
}

function toggleDetails(id: string) {
  expanded[id] = !expanded[id]
}

async function copyReceipt(toast: Toast) {
  copyState[toast.id] = (await copyToastReceipt(toast)) ? 'copied' : 'failed'
}

function dismissReceipt(id: string) {
  toastStore.dismissEvictedError(id)
  if (toastStore.evictedErrors.length === 0) open.value = false
}
</script>

<template>
  <section
    v-if="toastStore.evictedErrors.length > 0 || toastStore.evictedErrorOverflowCount > 0"
    data-toast-overflow
    :class="['pointer-events-auto mt-2 max-w-md rounded-lg border px-4 py-3 text-sm shadow-lg', panelClass]"
  >
    <button
      v-if="toastStore.evictedErrors.length > 0"
      type="button"
      data-toast-overflow-toggle
      class="font-medium underline underline-offset-2"
      :aria-expanded="open"
      :aria-controls="open ? 'toast-overflow-list' : undefined"
      @click="open = !open"
    >
      {{ open ? t('shell.toast.receipt.hideOlderErrors') : t('shell.toast.receipt.olderErrors', { count: toastStore.evictedErrors.length }, toastStore.evictedErrors.length) }}
    </button>
    <p v-if="toastStore.evictedErrorOverflowCount > 0" data-toast-overflow-count class="mt-2 text-xs font-normal">
      {{ t('shell.toast.receipt.earlierErrors', { count: toastStore.evictedErrorOverflowCount }, toastStore.evictedErrorOverflowCount) }}
    </p>

    <ul
      v-if="open && toastStore.evictedErrors.length > 0"
      id="toast-overflow-list"
      data-toast-overflow-list
      class="mt-3 max-h-[calc(100vh-6rem)] space-y-3 overflow-y-auto"
      :style="{ maxHeight: 'min(60vh, calc(100vh - 6rem))' }"
    >
      <li
        v-for="receipt in toastStore.evictedErrors"
        :key="receipt.id"
        :data-toast-receipt-id="receipt.id"
        class="border-t border-current/20 pt-2"
      >
        <p class="font-medium">{{ receipt.message }}</p>
        <div class="mt-2 flex flex-wrap gap-2 text-xs font-normal">
          <button
            v-if="receipt.details"
            type="button"
            class="underline underline-offset-2"
            :aria-expanded="expanded[receipt.id] ?? false"
            :aria-controls="expanded[receipt.id] ? detailsId(receipt.id) : undefined"
            @click="toggleDetails(receipt.id)"
          >
            {{ expanded[receipt.id] ? t('shell.toast.receipt.hideDetails') : t('shell.toast.receipt.showDetails') }}
          </button>
          <button type="button" class="underline underline-offset-2" @click="copyReceipt(receipt)">
            {{ copyState[receipt.id] === 'copied' ? t('shell.toast.receipt.copied') : copyState[receipt.id] === 'failed' ? t('shell.toast.receipt.copyFailed') : t('shell.toast.receipt.copyDetails') }}
          </button>
          <button
            type="button"
            class="underline underline-offset-2"
            :aria-label="t('shell.toast.receipt.dismissNotification')"
            @click="dismissReceipt(receipt.id)"
          >
            {{ t('shell.toast.receipt.dismissNotification') }}
          </button>
        </div>
        <pre
          v-if="receipt.details && expanded[receipt.id]"
          :id="detailsId(receipt.id)"
          class="mt-2 max-h-40 overflow-auto whitespace-pre-wrap rounded bg-black/5 p-2 text-xs font-normal"
          tabindex="0"
          role="region"
          :aria-label="t('shell.toast.receipt.errorDetails', { message: receipt.message })"
        >{{ receipt.details }}</pre>
      </li>
    </ul>
  </section>
</template>
