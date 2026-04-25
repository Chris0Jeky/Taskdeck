<script setup lang="ts">
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'

/**
 * ReviewDecisionRail — sticky bar with the four decision actions
 * (Reject ⌫ · Request edit E · Defer D · Apply ⏎). Apply is rendered in
 * the ember variant. Disabled state propagates while a network call is in
 * flight.
 */
defineProps<{
  summary: string
  busy?: boolean
}>()

const emit = defineEmits<{
  (event: 'apply'): void
  (event: 'reject'): void
  (event: 'request-edit'): void
  (event: 'defer'): void
}>()
</script>

<template>
  <div
    class="card-lift halo-ember paper-review-decision"
    role="toolbar"
    aria-label="Decision actions"
  >
    <PaperTagstamp tone="ember">DECISION</PaperTagstamp>
    <span class="tk-meta paper-review-decision__summary">{{ summary }}</span>
    <span class="paper-review-decision__spacer" />

    <PaperHLBtn
      label="Reject"
      kbd="⌫"
      :disabled="busy"
      data-testid="decision-reject"
      @click="emit('reject')"
    />
    <PaperHLBtn
      label="Request edit"
      kbd="E"
      :disabled="busy"
      data-testid="decision-edit"
      @click="emit('request-edit')"
    />
    <PaperHLBtn
      label="Defer"
      kbd="D"
      :disabled="busy"
      data-testid="decision-defer"
      @click="emit('defer')"
    />
    <PaperHLBtn
      label="Apply"
      kbd="⏎"
      variant="ember"
      :disabled="busy"
      data-testid="decision-apply"
      @click="emit('apply')"
    />
  </div>
</template>

<style scoped>
.paper-review-decision {
  margin-top: 18px;
  padding: 12px 16px;
  display: flex;
  align-items: center;
  gap: 12px;
  position: sticky;
  top: 0;
  z-index: 2;
}
.paper-review-decision__summary {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.paper-review-decision__spacer {
  flex: 1;
}
</style>
