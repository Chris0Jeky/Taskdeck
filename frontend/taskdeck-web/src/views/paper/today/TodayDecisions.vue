<script setup lang="ts">
import { computed } from 'vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import type {
  DossierDecision,
  DossierDecisionVerdict,
} from '../../../composables/useTodayDossier'

/**
 * TodayDecisions — 4-up grid of today's proposals with verdict tagstamps.
 *   APPLIED  → applied tone (green)
 *   REJECTED → overdue tone (rust)
 *   DEFERRED → ember tone (ember)
 */
const props = defineProps<{
  decisions: DossierDecision[]
}>()

const VERDICT_LABEL: Record<DossierDecisionVerdict, string> = {
  applied: 'APPLIED',
  rejected: 'REJECTED',
  deferred: 'DEFERRED',
}

const VERDICT_TONE: Record<DossierDecisionVerdict, 'applied' | 'overdue' | 'ember'> = {
  applied: 'applied',
  rejected: 'overdue',
  deferred: 'ember',
}

const summary = computed(() => {
  const total = props.decisions.length
  const applied = props.decisions.filter(d => d.verdict === 'applied')
  const avgConfidence =
    applied.length > 0
      ? applied.reduce((sum, d) => sum + d.confidence, 0) / applied.length
      : 0
  const applyRate = total > 0 ? applied.length / total : 0
  return {
    avgConfidence: avgConfidence.toFixed(2),
    applyRatePct: Math.round(applyRate * 100),
  }
})
</script>

<template>
  <div class="today-decisions" data-section="decisions">
    <div class="today-decisions__grid">
      <article
        v-for="decision in decisions"
        :key="decision.serial"
        class="card today-decision"
        :class="{ 'today-decision--stale': decision.stale }"
        :data-verdict="decision.verdict"
      >
        <div class="today-decision__head">
          <span class="tk-serial">{{ decision.serial }}</span>
          <PaperTagstamp :tone="VERDICT_TONE[decision.verdict]">
            {{ VERDICT_LABEL[decision.verdict] }}
          </PaperTagstamp>
        </div>
        <div class="today-decision__title">{{ decision.title }}</div>
        <div class="tk-meta today-decision__meta">conf {{ decision.confidence.toFixed(2) }} · {{ decision.when }}</div>
      </article>
    </div>
    <div v-if="decisions.length > 0" class="today-decisions__note">
      <span class="tk-eyebrow today-decisions__note-tag">NOTE</span>
      You applied at <b>{{ summary.avgConfidence }}</b> avg confidence today. Apply rate
      <b>{{ summary.applyRatePct }}%</b>.
    </div>
  </div>
</template>

<style scoped>
.today-decisions__grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-top: 10px;
}
.today-decision {
  padding: 12px;
}
.today-decision--stale {
  opacity: 0.7;
}
.today-decision__head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}
.today-decision__title {
  font-family: var(--serif);
  font-size: 13.5px;
  font-weight: 500;
  color: var(--ink-deep);
  margin: 4px 0;
  line-height: 1.3;
}
.today-decision__meta {
  font-size: 10px;
}
.today-decisions__note {
  margin-top: 14px;
  padding: 12px;
  background: var(--paper-2);
  border-radius: 2px;
  font-size: 12px;
  color: var(--ink-2);
  border-left: 2px solid var(--applied);
}
.today-decisions__note-tag {
  color: var(--applied);
  margin-right: 8px;
}
.today-decisions__note b {
  color: var(--ink);
}

@media (max-width: 700px) {
  .today-decisions__grid {
    grid-template-columns: 1fr;
  }
}
</style>
