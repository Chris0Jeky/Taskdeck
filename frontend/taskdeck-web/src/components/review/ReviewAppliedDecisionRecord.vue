<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Proposal, ProposalOperation } from '../../types/automation'

const props = defineProps<{
  proposal: Proposal
}>()

const { t, locale } = useI18n()

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const dateLocale = computed(() => {
  const active = locale.value
  const preferred =
    typeof navigator === 'undefined' ? [] : (navigator.languages ?? [navigator.language])
  return (
    preferred.find(
      (tag) => typeof tag === 'string' && tag.toLowerCase().split('-')[0] === active,
    ) ?? active
  )
})

const decisionActor = computed(() => {
  const actor = props.proposal.decidedByUserId?.trim() ?? ''
  return uuidPattern.test(actor) ? actor : t('review.appliedRecord.value.notRecorded')
})

function formatTimestamp(value: string | null): string {
  if (!value) return t('review.appliedRecord.value.notRecorded')
  const parsed = new Date(value)
  if (!Number.isFinite(parsed.getTime())) return t('review.appliedRecord.value.notRecorded')
  try {
    return new Intl.DateTimeFormat(dateLocale.value, {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(parsed)
  } catch {
    return t('review.appliedRecord.value.notRecorded')
  }
}

function humanize(value: string): string {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
    .toLowerCase()
}

function fallbackOperation(operation: ProposalOperation | undefined): string {
  if (!operation) return t('review.appliedRecord.value.notRecorded')
  const action = humanize(operation.actionType ?? '')
  const target = humanize(operation.targetType ?? '')
  if (!action && !target) return t('review.appliedRecord.value.notRecorded')
  return [action, target].filter(Boolean).join(' · ')
}

const operationDescriptions = computed(() => {
  const operations = [...(props.proposal.operations ?? [])]
    .map((operation, index) => ({ operation, index }))
    .sort((left, right) => {
      const leftSequence = Number.isFinite(left.operation.sequence)
        ? left.operation.sequence
        : Number.MAX_SAFE_INTEGER
      const rightSequence = Number.isFinite(right.operation.sequence)
        ? right.operation.sequence
        : Number.MAX_SAFE_INTEGER
      return leftSequence - rightSequence || left.index - right.index
    })
    .map(({ operation }) => operation)

  const headlines = (props.proposal.presentation?.operationHeadlines ?? []).map((headline) =>
    headline?.trim() ?? '',
  )
  const count = Math.max(operations.length, headlines.length)
  if (count === 0) return []

  return Array.from({ length: count }, (_, index) => {
    return headlines[index] || fallbackOperation(operations[index])
  })
})
</script>

<template>
  <section
    class="review-applied-record"
    :aria-label="$t('review.appliedRecord.ariaLabel')"
    data-testid="review-applied-decision-record"
  >
    <header class="review-applied-record__header">
      <p class="review-applied-record__eyebrow">{{ $t('review.appliedRecord.eyebrow') }}</p>
      <h2 class="review-applied-record__title">{{ $t('review.appliedRecord.heading') }}</h2>
      <p class="review-applied-record__lede">{{ $t('review.appliedRecord.lede') }}</p>
    </header>

    <dl class="review-applied-record__facts">
      <div>
        <dt>{{ $t('review.appliedRecord.field.outcome') }}</dt>
        <dd data-testid="applied-record-outcome">{{ $t('review.appliedRecord.value.applied') }}</dd>
      </div>
      <div>
        <dt>{{ $t('review.appliedRecord.field.decision') }}</dt>
        <dd data-testid="applied-record-decision">{{ $t('review.appliedRecord.value.approved') }}</dd>
      </div>
      <div>
        <dt>{{ $t('review.appliedRecord.field.decisionActor') }}</dt>
        <dd data-testid="applied-record-decision-actor">{{ decisionActor }}</dd>
      </div>
      <div>
        <dt>{{ $t('review.appliedRecord.field.decisionTime') }}</dt>
        <dd data-testid="applied-record-decision-time">{{ formatTimestamp(proposal.decidedAt) }}</dd>
      </div>
      <div>
        <dt>{{ $t('review.appliedRecord.field.appliedTime') }}</dt>
        <dd data-testid="applied-record-applied-time">{{ formatTimestamp(proposal.appliedAt) }}</dd>
      </div>
    </dl>

    <div class="review-applied-record__operations">
      <h3>{{ $t('review.appliedRecord.operations.heading') }}</h3>
      <ol v-if="operationDescriptions.length > 0" data-testid="applied-record-operations">
        <li v-for="(description, index) in operationDescriptions" :key="index">
          {{ description }}
        </li>
      </ol>
      <p v-else data-testid="applied-record-operations-empty">
        {{ $t('review.appliedRecord.value.notRecorded') }}
      </p>
    </div>
  </section>
</template>

<style scoped>
.review-applied-record {
  margin-top: 18px;
  padding: 18px;
  border: 1px solid var(--line, var(--td-border-default));
  border-left: 4px solid var(--ember, var(--td-color-info));
  background: var(--paper-card, var(--td-surface-container-low));
  color: var(--ink, var(--td-text-primary));
}

.review-applied-record__header {
  display: grid;
  gap: 4px;
}

.review-applied-record__eyebrow,
.review-applied-record__lede,
.review-applied-record__operations p {
  margin: 0;
  color: var(--mute, var(--td-text-secondary));
  font-size: 0.8125rem;
  line-height: 1.5;
}

.review-applied-record__eyebrow {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: 0.6875rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.review-applied-record__title {
  margin: 0;
  font-size: 1rem;
}

.review-applied-record__facts {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 12px;
  margin: 16px 0 0;
}

.review-applied-record__facts div {
  min-width: 0;
}

.review-applied-record__facts dt {
  color: var(--mute, var(--td-text-secondary));
  font-size: 0.6875rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.review-applied-record__facts dd {
  margin: 4px 0 0;
  overflow-wrap: anywhere;
  font-size: 0.8125rem;
}

.review-applied-record__operations {
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px solid var(--line-soft, var(--td-border-ghost));
}

.review-applied-record__operations h3 {
  margin: 0 0 8px;
  font-size: 0.8125rem;
}

.review-applied-record__operations ol {
  display: grid;
  gap: 6px;
  margin: 0;
  padding-left: 1.4rem;
  font-size: 0.8125rem;
  line-height: 1.45;
}
</style>
