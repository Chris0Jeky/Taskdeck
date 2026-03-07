<script setup lang="ts">
const props = defineProps<{
  proposal: {
    id: string
    boardId?: string | null
    boardName?: string | null
    sourceType: string
    status: string
    riskLevel: string
    summaryTitle: string
    summarySentence: string
    operationCount: number
    affectedEntities: Array<{ type: string; label: string }>
  }
}>()

const emit = defineEmits<{
  openDiff: [proposalId: string]
  approve: [proposalId: string]
  reject: [proposalId: string]
  execute: [proposalId: string]
}>()
</script>

<template>
  <article class="td-proposal-card">
    <header class="td-proposal-card__header">
      <div>
        <h3>{{ proposal.summaryTitle }}</h3>
        <p class="td-proposal-card__summary">{{ proposal.summarySentence }}</p>
      </div>
      <div class="td-proposal-card__chips">
        <span class="td-chip">{{ proposal.sourceType }}</span>
        <span class="td-chip">{{ proposal.riskLevel }}</span>
        <span class="td-chip">{{ proposal.status }}</span>
      </div>
    </header>

    <div v-if="proposal.boardName" class="td-proposal-card__meta">
      Project: {{ proposal.boardName }}
    </div>

    <div class="td-proposal-card__meta">
      {{ proposal.operationCount }} operation<span v-if="proposal.operationCount !== 1">s</span>
    </div>

    <ul class="td-proposal-card__entities">
      <li v-for="entity in proposal.affectedEntities" :key="`${entity.type}:${entity.label}`">
        {{ entity.type }}: {{ entity.label }}
      </li>
    </ul>

    <footer class="td-proposal-card__actions">
      <button class="td-btn td-btn--secondary td-btn--sm" @click="emit('openDiff', proposal.id)">Open diff</button>
      <button class="td-btn td-btn--secondary td-btn--sm" @click="emit('reject', proposal.id)">Reject</button>
      <button class="td-btn td-btn--primary td-btn--sm" @click="emit('approve', proposal.id)">Approve</button>
      <button class="td-btn td-btn--primary td-btn--sm" @click="emit('execute', proposal.id)">Execute</button>
    </footer>
  </article>
</template>
