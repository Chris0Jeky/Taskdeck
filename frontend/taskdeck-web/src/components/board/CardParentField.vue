<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { cardsApi } from '../../api/cardsApi'
import { useBoardStore } from '../../store/boardStore'
import type { Card } from '../../types/board'
const props = defineProps<{ card: Card; disabled?: boolean }>()
const model = defineModel<string | null>({ required: true })
const boardStore = useBoardStore()
const cards = ref<Card[]>([])
const loading = ref(false)
const error = ref(false)
const allowed = computed(() => boardStore.currentBoard?.id === props.card.boardId && boardStore.currentBoard.canWrite === true && !boardStore.currentBoard.isArchived)
let generation = 0
watch(() => props.card.id, async () => {
  const request = ++generation
  cards.value = []
  loading.value = true
  error.value = false
  try {
    const result = await cardsApi.getCards(props.card.boardId)
    if (request === generation) cards.value = result
  } catch { if (request === generation) error.value = true }
  finally { if (request === generation) loading.value = false }
}, { immediate: true })
const candidates = computed(() => cards.value.filter(card => card.id !== props.card.id))
</script>
<template>
  <div class="my-3 space-y-1">
    <label for="card-parent" class="block text-sm font-medium">Parent card</label>
    <select id="card-parent" v-model="model" class="w-full rounded border border-outline-variant/40 bg-surface px-3 py-2"
      :disabled="disabled || !allowed || loading || error">
      <option :value="null">No parent</option>
      <option v-if="model && !candidates.some(card => card.id === model)" :value="model">Current parent ({{ model }})</option>
      <option v-for="candidate in candidates" :key="candidate.id" :value="candidate.id">{{ candidate.title }} ({{ candidate.workItemType ?? 'Task' }})</option>
    </select>
    <p v-if="loading" role="status" class="text-sm">Loading parent choices...</p>
    <p v-else-if="error" role="alert" class="text-sm">Parent choices could not be loaded. Close and reopen this card to retry.</p>
    <p v-else class="text-sm text-on-surface-variant">Optional, on this board. Up to four levels; all card types can be parents. Save changes to apply.</p>
  </div>
</template>
