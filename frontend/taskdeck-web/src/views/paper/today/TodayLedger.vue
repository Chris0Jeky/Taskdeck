<script setup lang="ts">
import type { DossierLedgerEntry, DossierLedgerTone } from '../../../composables/useTodayDossier'

/**
 * TodayLedger — full event log for the day with `.rule-ledger` background.
 * Each row is grid: serial · time · who-pill · what · tone-dot.
 */
defineProps<{
  entries: DossierLedgerEntry[]
}>()

function toneColor(tone: DossierLedgerTone): string {
  switch (tone) {
    case 'ember':
      return 'var(--ember)'
    case 'applied':
      return 'var(--applied)'
    case 'active':
      return 'var(--ink-deep)'
    case 'passive':
      return 'var(--ink-2)'
    case 'mute':
    default:
      return 'var(--faint)'
  }
}
</script>

<template>
  <div class="today-ledger rule-ledger" data-section="ledger">
    <div
      v-for="entry in entries"
      :key="entry.serial"
      class="today-ledger__row"
      :data-tone="entry.tone"
    >
      <span class="tk-serial today-ledger__sn">{{ entry.serial }}</span>
      <span class="tk-serial today-ledger__time">{{ entry.time }}</span>
      <span class="tagstamp today-ledger__who" :style="{ color: toneColor(entry.tone) }">{{ entry.who.toUpperCase() }}</span>
      <span class="today-ledger__what">{{ entry.what }}</span>
      <span class="today-ledger__dot-cell" aria-hidden="true">
        <span class="today-ledger__dot" :style="{ background: toneColor(entry.tone) }" />
      </span>
    </div>
  </div>
</template>

<style scoped>
.today-ledger {
  /* `.rule-ledger` from paper-tokens supplies the horizontal rule pattern. */
}
.today-ledger__row {
  display: grid;
  grid-template-columns: 60px 60px 60px 1fr 60px;
  gap: 12px;
  padding: 8px 22px;
  border-bottom: 1px solid var(--line-soft);
  align-items: center;
  font-size: 12px;
}
.today-ledger__sn {
  color: var(--faint);
}
.today-ledger__time {
  color: var(--ink-2);
}
.today-ledger__who {
  font-size: 9px;
}
.today-ledger__what {
  color: var(--ink-2);
  line-height: 1.45;
}
.today-ledger__dot-cell {
  text-align: right;
}
.today-ledger__dot {
  width: 6px;
  height: 6px;
  display: inline-block;
  border-radius: 50%;
}
</style>
