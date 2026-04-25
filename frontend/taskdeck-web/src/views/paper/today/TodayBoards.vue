<script setup lang="ts">
import { computed } from 'vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import type { DossierBoardLine } from '../../../composables/useTodayDossier'

/**
 * TodayBoards — list of boards touched today, with move counts and a
 * "N PROP" pill when proposals exist.  Boards with zero moves and zero
 * proposals are dimmed.
 */
const props = defineProps<{
  boards: DossierBoardLine[]
}>()

const summary = computed(() => {
  const touched = props.boards.filter(b => b.moves > 0 || b.proposals > 0).length
  return { touched, total: props.boards.length }
})
</script>

<template>
  <div class="today-boards" data-section="boards">
    <div class="tk-eyebrow today-boards__summary">
      {{ summary.touched }} of {{ summary.total }} touched today
    </div>
    <div
      v-for="board in boards"
      :key="board.name"
      class="today-board"
      :class="{ 'today-board--dim': board.moves === 0 && board.proposals === 0 }"
    >
      <span class="today-board__name">{{ board.name }}</span>
      <span class="tk-meta today-board__moves">
        <template v-if="board.moves > 0">
          <b>{{ board.moves }}</b> moves
        </template>
        <template v-else>—</template>
      </span>
      <span class="today-board__prop">
        <PaperTagstamp v-if="board.proposals > 0" tone="ember">{{ board.proposals }} PROP</PaperTagstamp>
      </span>
    </div>
  </div>
</template>

<style scoped>
.today-boards__summary {
  margin-bottom: 8px;
}
.today-board {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 10px;
  padding: 9px 0;
  border-bottom: 1px solid var(--line-soft);
  align-items: center;
}
.today-board--dim {
  opacity: 0.55;
}
.today-board__name {
  font-family: var(--serif);
  font-size: 14px;
  font-weight: 500;
  color: var(--ink-deep);
}
.today-board--dim .today-board__name {
  color: var(--mute);
}
.today-board__moves {
  font-size: 11px;
}
.today-board__moves b {
  color: var(--ink);
}
.today-board__prop {
  min-width: 72px;
  text-align: right;
}
</style>
