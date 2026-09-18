<script setup lang="ts">
import { formatEstimatedEffort } from '../../utils/estimatedEffort'
import type { EstimateTotals } from '../../types/estimateRollups'
defineProps<{ totals: EstimateTotals }>()
</script>

<template>
  <div class="space-y-1 text-sm">
    <p class="text-base font-semibold">{{ formatEstimatedEffort(totals.knownEstimateMinutes) }} known estimate</p>
    <p>{{ totals.cardCount }} cards · {{ totals.cardCount - totals.missingEstimateCount }} estimated</p>
    <p>{{ totals.missingEstimateCount }} missing estimates</p>
  </div>
</template>
