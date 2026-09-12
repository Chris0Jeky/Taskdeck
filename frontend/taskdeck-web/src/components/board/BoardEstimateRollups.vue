<script setup lang="ts">
import { computed, nextTick, ref, toRef } from 'vue'
import { useBoardStore } from '../../store/boardStore'
import { useBoardEstimateRollups } from '../../composables/useBoardEstimateRollups'
import EstimateTotalsSummary from './EstimateTotalsSummary.vue'

const props = defineProps<{ boardId: string }>()
const boardStore = useBoardStore()
const revision = computed(() => JSON.stringify([
  boardStore.currentBoard,
  boardStore.currentBoardCards,
  boardStore.error,
]))
const { open, loading, stale, error, rollup, refresh, toggle } = useBoardEstimateRollups(toRef(props, 'boardId'), revision)
const panelId = computed(() => `board-estimates-${props.boardId}`)
const trigger = ref<HTMLButtonElement | null>(null)
async function closeFromKeyboard() {
  toggle()
  await nextTick()
  trigger.value?.focus()
}
</script>

<template>
  <section class="border-b border-outline-variant/30 bg-surface-container px-4 py-2" aria-label="Board estimates">
    <button ref="trigger" type="button" class="rounded border border-outline-variant/40 px-3 py-2 text-sm focus-visible:outline focus-visible:outline-2"
      :aria-expanded="open" :aria-controls="panelId" @click="toggle">Estimates</button>
    <div v-if="open" :id="panelId" role="region" :aria-labelledby="panelId + '-title'"
      class="mt-3 space-y-4">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 :id="panelId + '-title'" class="font-semibold">Active card estimates</h2>
        <button type="button" class="rounded border border-outline-variant/40 px-3 py-2 text-sm"
          :disabled="loading" @click="refresh" @keydown.esc.stop.prevent="closeFromKeyboard">Refresh estimates</button>
      </div>
      <p class="text-sm">Estimates are optional. Known zero is included; missing estimates are counted separately. Parent and child estimates stay independent.</p>
      <p class="text-sm">Current assignments and estimates only; these totals do not measure time worked or capacity.</p>
      <p v-if="stale" role="status">Board state changed. Refresh estimates to see the latest totals.</p>
      <p v-if="loading" role="status">Loading estimates…</p>
      <p v-else-if="error" role="alert">{{ error }}</p>
      <div v-else-if="rollup" class="space-y-4" :class="{ 'opacity-70': stale }">
        <p class="text-xs">Snapshot from <time :datetime="rollup.generatedAt">{{ new Date(rollup.generatedAt).toLocaleString() }}</time>. Refresh after changes.</p>
        <div class="grid gap-4 sm:grid-cols-2">
          <section class="rounded border border-outline-variant/30 p-3" aria-label="Board total">
            <h3 class="mb-2 font-semibold">Board total</h3>
            <EstimateTotalsSummary :totals="rollup.board" />
            <p v-if="rollup.board.cardCount === 0" class="mt-2 text-sm">No active cards on this board.</p>
          </section>
          <section class="rounded border border-outline-variant/30 p-3" aria-label="Unassigned total">
            <h3 class="mb-2 font-semibold">Unassigned</h3>
            <EstimateTotalsSummary :totals="rollup.unassigned" />
          </section>
        </div>
        <section aria-label="Estimates by column">
          <h3 class="mb-2 font-semibold">By column</h3>
          <ul class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <li v-for="column in rollup.columns" :key="column.columnId" class="min-w-0 rounded border border-outline-variant/30 p-3">
              <h4 class="mb-2 break-words font-medium">{{ column.name }}</h4>
              <EstimateTotalsSummary :totals="column.totals" />
            </li>
          </ul>
        </section>
        <section aria-label="Estimates by participant">
          <h3 class="mb-2 font-semibold">By participant</h3>
          <p class="mb-3 text-sm">Participant totals overlap: a card assigned to several people contributes its full estimate to each person. Do not add these totals to calculate the board total.</p>
          <ul class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <li v-for="participant in rollup.participants" :key="participant.userId" class="min-w-0 rounded border border-outline-variant/30 p-3">
              <h4 class="mb-2 break-words font-medium">{{ participant.username }}</h4>
              <EstimateTotalsSummary :totals="participant.totals" />
            </li>
          </ul>
          <p v-if="rollup.participants.length === 0" class="text-sm">No current participants.</p>
        </section>
      </div>
    </div>
  </section>
</template>
