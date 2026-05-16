<script setup lang="ts">
import { useShareCard } from '../../../composables/useShareCard'
import { logError } from '../../../utils/errorReporting'
import type { Card } from '../../../types/board'

const props = defineProps<{
  isFormValid: boolean
  card: Card
}>()

defineEmits<{
  (e: 'save'): void
  (e: 'close'): void
  (e: 'delete-click'): void
}>()

const { canShare, shareCard } = useShareCard()

async function handleShare() {
  try {
    await shareCard(props.card)
  } catch (error) {
    logError('Card share failed:', error)
  }
}
</script>

<template>
  <div class="mt-6 flex items-center justify-between">
    <div class="flex gap-2">
      <button
        @click="$emit('delete-click')"
        type="button"
        class="px-4 py-2 text-sm font-medium text-error hover:text-error/80 hover:bg-error/10 border border-error/40 rounded-md transition-colors"
      >
        Delete Card
      </button>
      <button
        v-if="canShare"
        @click="handleShare"
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors"
        aria-label="Share card"
      >
        Share
      </button>
    </div>
    <div class="flex gap-2">
      <button
        @click="$emit('close')"
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors"
      >
        Cancel
      </button>
      <button
        @click="$emit('save')"
        :disabled="!isFormValid"
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-primary-container bg-primary-container hover:brightness-110 disabled:opacity-40 disabled:cursor-not-allowed rounded-md transition-all"
      >
        Save Changes
      </button>
    </div>
  </div>
</template>
