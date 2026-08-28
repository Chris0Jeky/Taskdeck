<script setup lang="ts">
import TdDialog from '../ui/TdDialog.vue'

defineProps<{
  open: boolean
  count: number
  busy?: boolean
}>()

const emit = defineEmits<{
  (event: 'confirm'): void
  (event: 'cancel'): void
}>()
</script>

<template>
  <TdDialog
    :open="open"
    :title="$t('review.batchApprove.dialog.title')"
    :description="$t('review.batchApprove.dialog.description', { count }, count)"
    :close-on-backdrop="false"
    @close="emit('cancel')"
  >
    <div data-testid="batch-approve-dialog">
      <p>{{ $t('review.batchApprove.dialog.body', { count }, count) }}</p>
      <p class="tk-meta" data-testid="batch-approve-not-applied">
        {{ $t('review.batchApprove.dialog.notApplied') }}
      </p>
    </div>

    <template #footer>
      <button
        type="button"
        class="td-btn td-btn--secondary td-btn--sm"
        data-testid="batch-approve-cancel"
        :disabled="busy"
        @click="emit('cancel')"
      >
        {{ $t('review.batchApprove.dialog.cancel') }}
      </button>
      <button
        type="button"
        class="td-btn td-btn--primary td-btn--sm"
        data-testid="batch-approve-confirm"
        :disabled="busy || count === 0"
        @click="emit('confirm')"
      >
        {{ $t('review.batchApprove.dialog.confirm', { count }, count) }}
      </button>
    </template>
  </TdDialog>
</template>
