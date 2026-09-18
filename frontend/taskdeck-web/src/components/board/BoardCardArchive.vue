<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { cardsApi } from '../../api/cardsApi'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import { isDemoMode } from '../../utils/demoMode'
import CardArchiveAction from './CardArchiveAction.vue'
import type { Card } from '../../types/board'

const props = defineProps<{ boardId: string }>()
const cards = ref<Card[]>([])
const open = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)
const revision = ref(0)
let generation = 0
async function load() {
  if (isDemoMode) return
  const current = ++generation
  loading.value = true
  error.value = null
  cards.value = []
  try {
    const result = await cardsApi.getArchivedCards(props.boardId)
    if (current === generation) { cards.value = result; revision.value++ }
  } catch (e) {
    if (current === generation) error.value = getErrorDisplay(e, 'Could not load archived cards.').message
  } finally { if (current === generation) loading.value = false }
}
watch(() => props.boardId, () => { generation++; cards.value = []; open.value = false; error.value = null; loading.value = false })
function toggle() { open.value = !open.value; if (open.value) void load() }
</script>

<template>
  <section class="border-b border-outline-variant/30 bg-surface-container px-4 py-3" aria-label="Card archive">
    <p v-if="isDemoMode" class="text-sm">Archived card history is not available in this demo.</p>
    <button v-else type="button" class="rounded border border-outline-variant/40 px-3 py-2 text-sm"
      :aria-expanded="open" aria-controls="board-card-archive-history" @click="toggle">Archived cards</button>
    <div v-if="!isDemoMode && open" id="board-card-archive-history" class="mt-3 space-y-3">
      <p class="text-sm">Archived cards keep their original column, labels, thinking and history. Restore returns them to active work.</p>
      <button type="button" class="text-sm underline" :disabled="loading" @click="load">Refresh archived cards</button>
      <p v-if="loading" role="status">Loading archived cards…</p>
      <p v-else-if="error" role="alert">{{ error }}</p>
      <p v-else-if="cards.length === 0">No archived cards on this board.</p>
      <ul v-else class="space-y-3">
        <li v-for="card in cards" :key="`${revision}:${card.id}`" class="rounded border border-outline-variant/30 p-3">
          <h3 class="font-semibold">{{ card.title }}</h3>
          <p class="text-sm whitespace-pre-wrap">{{ card.description }}</p>
          <span v-for="label in card.labels" :key="label.id" class="mr-2 text-sm">{{ label.name }}</span>
          <RouterLink :to="`/workspace/boards/${boardId}/cards/${card.id}/thinking`" class="block text-sm underline">Open thinking and history</RouterLink>
          <CardArchiveAction :card="card" @changed="load" @refresh="load" />
        </li>
      </ul>
    </div>
  </section>
</template>
