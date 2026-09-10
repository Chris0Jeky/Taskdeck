<script setup lang="ts">
import { onScopeDispose, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { automationApi } from '../../api/automationApi'
import { useSessionStore } from '../../store/sessionStore'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import type { BoardProposalMarkers } from '../../composables/useBoardProposalMarker'
import { normalizeProposalStatus } from '../../utils/automation'
import type { BoardDetail, Card } from '../../types/board'
import type { Proposal, ProposalPreview } from '../../types/automation'

const props = withDefaults(defineProps<{ proposalId: string; board: BoardDetail; cards: Card[]; available?: boolean }>(), { available: true })
const emit = defineEmits<{ markers: [value: BoardProposalMarkers]; close: [] }>()
const session = useSessionStore()
const preview = ref<ProposalPreview | null>(null)
const proposal = ref<Proposal | null>(null)
const loading = ref(false)
const error = ref('')
let generation = 0
let timer: ReturnType<typeof setTimeout> | undefined
const sameId = (a: string | null, b: string | null) => a?.toLowerCase() === b?.toLowerCase()
function clear() {
  generation++; clearTimeout(timer); preview.value = null; proposal.value = null
  loading.value = false; error.value = ''; emit('markers', {})
}
function invalidate() {
  clear(); error.value = 'The board or session changed. Refresh to check this proposal again.'
}
watch([() => props.proposalId, () => props.available, () => session.userId, () => !!session.token], invalidate, { flush: 'sync' })
watch([() => props.board, () => props.cards], invalidate, { deep: true, flush: 'sync' })

async function load() {
  clear()
  if (!props.available) return
  const request = generation
  const startedAt = performance.now()
  loading.value = true
  try {
    const [receipt, detail] = await Promise.all([
      automationApi.getProposalPreview(props.proposalId), automationApi.getProposal(props.proposalId),
    ])
    if (request !== generation) return
    const status = normalizeProposalStatus(detail.status)
    const effectiveId = status === 'Approved' ? detail.approvedRevisionId : detail.latestRevisionId
    if (!sameId(receipt.proposalId, props.proposalId) || !sameId(detail.id, props.proposalId)
      || !sameId(receipt.boardId, props.board.id) || !sameId(detail.boardId, props.board.id)
      || !sameId(receipt.effectiveRevisionId, effectiveId)
      || Date.parse(receipt.proposalUpdatedAt) !== Date.parse(detail.updatedAt)
      || normalizeProposalStatus(receipt.status) !== status
      || !['PendingReview', 'Approved'].includes(status))
      throw new Error('The proposal changed while loading. Refresh to check its latest revision.')
    const lifetime = Math.min(30000, Date.parse(receipt.expiresAt) - Date.parse(receipt.checkedAt)) - (performance.now() - startedAt)
    if (!Number.isFinite(lifetime) || lifetime <= 0) throw new Error('This proposal preview has expired. Open Review for its history.')
    const existing = new Set([
      ...props.cards.filter(card => sameId(card.boardId, props.board.id)).map(card => `card:${card.id.toLowerCase()}`),
      ...props.board.columns.map(column => `column:${column.id.toLowerCase()}`),
    ])
    const markers: Record<string, string> = {}
    for (const operation of detail.operations) {
      const kind = operation.targetType.toLowerCase()
      let parameters: Record<string, unknown> = {}
      try { const parsed: unknown = JSON.parse(operation.parameters); if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) parameters = parsed as Record<string, unknown> } catch { /* The checked diff remains available if a target cannot be projected. */ }
      // Execution uses parameter IDs. A display targetId must never override them.
      const parameterId = Object.entries(parameters).find(([name]) => name.toLowerCase() === `${kind}id`)?.[1]
      const id = typeof parameterId === 'string' ? parameterId : operation.targetId
      const key = `${kind}:${id?.toLowerCase()}`
      if (operation.actionType.toLowerCase() !== 'create' && existing.has(key)) markers[key] = 'Proposed change'
      if (kind === 'card' && ['create', 'move'].includes(operation.actionType.toLowerCase()) && typeof parameters.columnId === 'string') {
        const destination = `column:${parameters.columnId.toLowerCase()}`
        if (existing.has(destination)) markers[destination] = 'Proposed change'
      }
    }
    proposal.value = detail; preview.value = receipt; emit('markers', markers)
    timer = setTimeout(() => { clear(); error.value = 'Refresh the preview to check the latest changes.' }, lifetime)
  } catch (cause) {
    if (request === generation) error.value = getErrorDisplay(cause, 'Preview unavailable. Open Review to inspect this proposal.').message
  } finally { if (request === generation) loading.value = false }
}
onScopeDispose(clear)
</script>

<template>
  <section class="board-proposal-preview" aria-label="Board proposal preview">
    <div class="board-proposal-preview__actions">
      <h2>Proposed board changes</h2>
      <button type="button" :disabled="loading || !available" @click="load">{{ loading ? 'Checking proposal…' : 'Refresh board preview' }}</button>
      <RouterLink :to="{ path: '/workspace/review', query: { boardId: board.id }, hash: `#proposal-${proposalId}` }">Open Review</RouterLink>
      <button type="button" @click="emit('close')">Close preview</button>
    </div>
    <p v-if="error" role="status">{{ error }}</p>
    <template v-if="preview && proposal">
      <p>{{ normalizeProposalStatus(preview.status) }} · {{ preview.effectiveRevisionNumber === null ? 'Original proposal' : `Revision ${preview.effectiveRevisionNumber}` }} · checked {{ new Date(preview.checkedAt).toLocaleTimeString() }}</p>
      <p>{{ proposal.presentation?.plainSummary || proposal.summary }}</p>
      <details open><summary>Changes to inspect</summary><pre>{{ preview.diff }}</pre></details>
      <p>Existing targets are marked “Proposed change”. New items, hidden cards and targets without a board object appear in the summary above. The board still shows its saved state. Approval and Apply remain in Review.</p>
    </template>
    <p v-else-if="!loading && !error">Check the proposal to reveal its diff and mark the affected board objects.</p>
  </section>
</template>

<style scoped>
.board-proposal-preview { margin: 1rem; padding: 1rem; border: 2px dashed var(--td-border-default); border-radius: .75rem; background: var(--td-surface-primary); color: var(--td-text-primary); }
.board-proposal-preview__actions { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem; }
h2 { font-size: 1rem; font-weight: 650; margin-right: auto; }
button, a { padding: .5rem; border: 1px solid var(--td-border-default); border-radius: .3rem; }
p { font-size: .85rem; margin-top: .5rem; line-height: 1.5; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; max-height: 16rem; overflow: auto; font-size: .8rem; }
</style>

<style>
[data-proposal-change] { outline: 2px dashed var(--td-text-secondary); outline-offset: -2px; }
.td-proposal-marker { display: block; padding: .3rem .6rem; font-size: .75rem; font-weight: 650; color: var(--td-text-primary); background: var(--td-surface-secondary); border-bottom: 1px dashed var(--td-border-default); }
</style>
