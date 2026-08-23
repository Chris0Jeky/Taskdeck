<script setup lang="ts">
import { computed } from 'vue'
import type { ProposalDeepLinkState } from '../../composables/useReviewProposals'

const props = defineProps<{
  proposalId: string
  state: ProposalDeepLinkState
  canClearScope?: boolean
}>()

const emit = defineEmits<{
  (event: 'clear-scope'): void
}>()

// `idle` lasts only until the mounted loader starts. Treat it as loading so a
// valid deep link never flashes a false not-found verdict on first render.
const effectiveState = computed(() => (props.state === 'idle' ? 'loading' : props.state))
const copyKey = computed(() => {
  if (effectiveState.value === 'outside-scope') return 'outsideScope'
  if (effectiveState.value === 'error') return 'error'
  if (effectiveState.value === 'not-found') return 'notFound'
  return 'loading'
})
</script>

<template>
  <section
    class="td-review-deep-link"
    :aria-label="$t('review.deepLink.ariaLabel')"
    data-testid="review-deep-link-state"
  >
    <div class="tk-eyebrow">{{ $t('review.deepLink.eyebrow', { id: proposalId }) }}</div>
    <h2 class="tk-h2" data-testid="paper-review-deep-link-title">
      {{ $t(`review.deepLink.${copyKey}Title`) }}
    </h2>
    <p class="tk-lede" data-testid="paper-review-deep-link-body">
      {{ $t(`review.deepLink.${copyKey}Body`) }}
    </p>
    <button
      v-if="effectiveState === 'outside-scope' && canClearScope"
      type="button"
      class="td-review-deep-link__clear"
      data-testid="paper-review-clear-scope"
      @click="emit('clear-scope')"
    >
      {{ $t('review.scope.clear') }}
    </button>
  </section>
</template>

<style scoped>
.td-review-deep-link {
  text-align: left;
}
.td-review-deep-link__clear {
  margin-top: 16px;
  border: 1px solid var(--line);
  background: var(--paper-card);
  color: var(--ink);
  cursor: pointer;
  font-family: var(--mono);
  font-size: 11px;
  padding: 7px 10px;
}
.td-review-deep-link__clear:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 2px;
}
</style>
