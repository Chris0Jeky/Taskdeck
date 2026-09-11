<script setup lang="ts">
import { computed, ref } from 'vue'
import TdDialog from '../ui/TdDialog.vue'
import CardDetachList from './CardDetachList.vue'
import { cardsApi } from '../../api/cardsApi'
import { useBoardStore } from '../../store/boardStore'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import type { Card, CardDetachPreview } from '../../types/board'

const props = defineProps<{ card: Card; disabled?: boolean }>()
const emit = defineEmits<{ changed: []; refresh: [] }>()
const boardStore = useBoardStore()
const preview = ref<CardDetachPreview | null>(null)
const busy = ref(false)
const error = ref<string | null>(null)
const allowed = computed(() => boardStore.currentBoard?.id === props.card.boardId
  && boardStore.currentBoard.canWrite === true && !boardStore.currentBoard.isArchived)

async function requestChange() {
  if (props.card.isArchived) return change()
  if (!allowed.value || props.disabled || busy.value || error.value) return
  busy.value = true
  try { preview.value = await cardsApi.previewDetach(props.card.boardId, props.card.id) }
  catch (e) { error.value = getErrorDisplay(e, 'Could not load every affected child. Refresh before archiving.').message }
  finally { busy.value = false }
}
async function change() {
  if (!allowed.value || props.disabled || busy.value || error.value) return
  busy.value = true
  try {
    await boardStore.setCardArchived(props.card.boardId, props.card.id, !props.card.isArchived, preview.value?.expectedUpdatedAt ?? props.card.updatedAt, preview.value?.expectedChildrenFingerprint)
    preview.value = null
    emit('changed')
  } catch (e) {
    error.value = getErrorDisplay(e, 'The card state could not be confirmed. Refresh before trying again.').message
  } finally { busy.value = false }
}
</script>

<template>
  <div class="my-3 space-y-2">
    <p v-if="!allowed" class="text-sm text-on-surface-variant">Archive and restore require an active board with edit access.</p>
    <p v-else-if="disabled" class="text-sm text-on-surface-variant">Save or discard your changes before archiving.</p>
    <button type="button" class="rounded border border-outline-variant/40 px-3 py-2 text-sm disabled:opacity-40"
      :disabled="!allowed || disabled || busy || !!error" @click="requestChange">
      {{ busy ? 'Saving…' : card.isArchived ? 'Restore card' : 'Archive card' }}
    </button>
    <div v-if="error" role="alert" class="text-sm text-error">
      {{ error }}
      <button type="button" class="ml-2 underline" @click="emit('refresh')">Refresh card state</button>
    </div>
    <TdDialog :open="!!preview" title="Archive card?" description="Review every child detachment before confirming." @close="preview = null">
      <CardDetachList v-if="preview" :preview="preview" />
      <template #footer>
        <button type="button" class="px-3 py-2" :disabled="busy" @click="preview = null">Cancel</button>
        <button type="button" class="px-3 py-2" :disabled="busy || !!error" @click="change">Confirm archive</button>
      </template>
    </TdDialog>
  </div>
</template>
