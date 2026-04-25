<script setup lang="ts">
import type { Card, CardCaptureProvenance } from '../../../types/board'
import { normalizeProposalStatus } from '../../../utils/automation'

defineProps<{
  card: Card
  loadingCaptureProvenance: boolean
  captureProvenanceError: string | null
  captureProvenance: CardCaptureProvenance | null
  loadedCaptureProvenanceCardId: string | null
  captureHrefFn: (captureItemId: string) => string
  proposalHrefFn: (proposalId: string) => string
}>()

function proposalStatusLabel(status: CardCaptureProvenance['proposalStatus']): string {
  return normalizeProposalStatus(status)
}
</script>

<template>
  <div class="pt-4 border-t border-outline-variant/30">
    <div class="text-xs text-on-surface-variant space-y-1">
      <p data-testid="timestamp">Created: {{ new Date(card.createdAt).toLocaleString() }}</p>
      <p data-testid="timestamp">Last updated: {{ new Date(card.updatedAt).toLocaleString() }}</p>
    </div>
    <div class="mt-3 space-y-2">
      <div v-if="loadingCaptureProvenance" class="text-xs text-on-surface-variant">
        Loading capture provenance...
      </div>
      <div v-else-if="captureProvenanceError" class="text-xs text-error" role="alert">
        {{ captureProvenanceError }}
      </div>
      <div v-else-if="captureProvenance" class="space-y-2">
        <div class="flex flex-wrap items-center gap-2 text-xs">
          <span class="px-2 py-1 rounded-full bg-primary/20 text-primary font-semibold uppercase tracking-wide">
            Capture Origin
          </span>
          <span class="text-on-surface-variant">Proposal status: {{ proposalStatusLabel(captureProvenance.proposalStatus) }}</span>
        </div>
        <div class="flex flex-wrap items-center gap-2 text-xs">
          <a
            class="px-2 py-1 rounded-md border border-primary/30 text-primary hover:bg-primary/10"
            :href="captureHrefFn(captureProvenance.captureItemId)"
          >
            Open Capture
          </a>
          <a
            class="px-2 py-1 rounded-md border border-primary/30 text-primary hover:bg-primary/10"
            :href="proposalHrefFn(captureProvenance.proposalId)"
          >
            Open Proposal
          </a>
        </div>
        <p v-if="captureProvenance.triageRunId" class="text-xs text-on-surface-variant">
          Triage run: {{ captureProvenance.triageRunId }}
        </p>
      </div>
      <p v-else-if="loadedCaptureProvenanceCardId === card.id" class="text-xs text-on-surface-variant italic" data-testid="provenance-empty-state">Created manually — no capture provenance.</p>
    </div>
  </div>
</template>
