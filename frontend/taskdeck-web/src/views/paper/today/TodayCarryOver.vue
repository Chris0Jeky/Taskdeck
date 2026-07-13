<script setup lang="ts">
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import type { DossierCarryOverCard } from '../../../composables/useTodayDossier'

/**
 * TodayCarryOver — live overdue cards rendered inside a rust-bordered card.
 * No pin action is offered until a real mutation endpoint exists.
 */
defineProps<{
  cards: DossierCarryOverCard[]
}>()

</script>

<template>
  <div class="today-carry-over" data-section="carry-over">
    <div
      v-for="card in cards"
      :key="card.serial"
      class="today-carry-over__row"
    >
      <div class="today-carry-over__head">
        <span class="tk-serial">{{ card.serial }}</span>
        <PaperTagstamp tone="overdue">{{ card.age.toUpperCase() }}</PaperTagstamp>
      </div>
      <div class="today-carry-over__title">{{ card.title }}</div>
      <div class="tk-meta today-carry-over__meta">{{ card.reason }}</div>
    </div>
  </div>
</template>

<style scoped>
.today-carry-over {
  display: flex;
  flex-direction: column;
  gap: 0;
}
.today-carry-over__row {
  padding: 10px 0;
  border-bottom: 1px solid var(--line-soft);
}
.today-carry-over__head {
  display: flex;
  justify-content: space-between;
  margin-bottom: 2px;
}
.today-carry-over__title {
  font-family: var(--serif);
  font-size: 14px;
  font-weight: 500;
  color: var(--ink-deep);
  line-height: 1.3;
}
.today-carry-over__meta {
  font-size: 10.5px;
  margin-top: 2px;
}
</style>
