<script setup lang="ts">
import { onScopeDispose, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { automationApi } from '../../api/automationApi'
import { useSessionStore } from '../../store/sessionStore'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import { normalizeProposalStatus } from '../../utils/automation'
import type { ProposalPreview } from '../../types/automation'

const props = defineProps<{ proposalId: string; boardId: string | null }>()
const session = useSessionStore()
const preview = ref<ProposalPreview | null>(null)
const loading = ref(false)
const error = ref('')
let generation = 0
let timer: ReturnType<typeof setTimeout> | undefined
function clear() {
  generation++; clearTimeout(timer); preview.value = null; loading.value = false; error.value = ''
}
watch(() => [props.proposalId, props.boardId, session.userId, session.token], clear, { flush: 'sync' })
async function load() {
  clear()
  const request = generation
  const startedAt = performance.now()
  loading.value = true
  try {
    const result = await automationApi.getProposalPreview(props.proposalId)
    if (request !== generation) return
    if (result.proposalId !== props.proposalId || result.boardId !== props.boardId)
      throw new Error('This preview does not belong to the current conversation board.')
    // Both receipt timestamps use the server clock. Subtract the full round
    // trip conservatively; the client's wall clock may be hours out of sync.
    const lifetime = Math.min(30000, Date.parse(result.expiresAt) - Date.parse(result.checkedAt))
      - (performance.now() - startedAt)
    if (!Number.isFinite(lifetime) || lifetime <= 0) throw new Error('This proposal has expired. Open Review for its history.')
    preview.value = result
    timer = setTimeout(() => { clear(); error.value = 'Refresh the preview to check the latest changes.' }, lifetime)
  } catch (cause) {
    if (request === generation) error.value = getErrorDisplay(cause, 'Preview unavailable. Open Review to inspect this proposal.').message
  } finally { if (request === generation) loading.value = false }
}
onScopeDispose(clear)
</script>

<template>
  <section class="proposal-preview" aria-label="Proposal preview">
    <button type="button" :disabled="loading" @click="load">{{ loading ? 'Checking proposal…' : preview ? 'Refresh preview' : 'Preview proposed changes' }}</button>
    <p v-if="error" role="status">{{ error }}</p>
    <template v-if="preview">
      <p>{{ normalizeProposalStatus(preview.status) }} · {{ preview.effectiveRevisionNumber === null ? 'Original proposal' : `Revision ${preview.effectiveRevisionNumber}` }} · checked {{ new Date(preview.checkedAt).toLocaleTimeString() }}</p>
      <pre>{{ preview.diff }}</pre>
      <RouterLink v-if="boardId" :to="{ path: `/workspace/boards/${boardId}`, query: { proposalId } }">Preview on board</RouterLink>
      <p>This is a checked preview, not an applied change. Open Review to inspect, approve and explicitly apply. Review checks the proposal again.</p>
    </template>
  </section>
</template>

<style scoped>
.proposal-preview { margin-block: .5rem; padding: .75rem; border: 1px solid var(--td-border-default); border-radius: .5rem; }
button { color: var(--td-text-primary); background: var(--td-surface-secondary); border: 1px solid var(--td-border-default); padding: .5rem; border-radius: .3rem; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; max-height: 25rem; overflow: auto; font-size: .8rem; }
p { font-size: .85rem; line-height: 1.5; }
</style>
