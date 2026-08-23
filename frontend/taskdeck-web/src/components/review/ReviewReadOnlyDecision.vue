<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Proposal } from '../../types/automation'
import { normalizeProposalStatus } from '../../utils/automation'

const props = withDefaults(
  defineProps<{
    proposal: Proposal
    isExpired: boolean
    testIdPrefix?: string
  }>(),
  { testIdPrefix: 'review-read-only' },
)

const { t, locale } = useI18n()

const dateLocale = computed(() => {
  const active = locale.value
  const preferred =
    typeof navigator === 'undefined' ? [] : (navigator.languages ?? [navigator.language])
  const regional = preferred.find(
    (tag) => typeof tag === 'string' && tag.toLowerCase().split('-')[0] === active,
  )
  return regional ?? active
})

const normalizedStatus = computed(() => normalizeProposalStatus(props.proposal.status))
const decision = computed(() => {
  const status =
    props.isExpired && normalizedStatus.value === 'PendingReview'
      ? 'Expired'
      : normalizedStatus.value
  const suffix = status.charAt(0).toLowerCase() + status.slice(1)
  return t(`review.status.${suffix}`)
})
const actor = computed(
  () => props.proposal.decidedByUserId?.trim() || t('review.readOnly.notRecorded'),
)
const timestamp = computed(() => {
  const rawTimestamp =
    normalizedStatus.value === 'Applied'
      ? (props.proposal.appliedAt ?? props.proposal.decidedAt)
      : props.proposal.decidedAt
  if (!rawTimestamp) return t('review.readOnly.notRecorded')
  const parsed = new Date(rawTimestamp)
  if (Number.isNaN(parsed.getTime())) return t('review.readOnly.notRecorded')
  return parsed.toLocaleString(dateLocale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
})

function testId(suffix: string): string {
  return `${props.testIdPrefix}-${suffix}`
}
</script>

<template>
  <section
    class="review-read-only-decision"
    :aria-label="$t('review.readOnly.ariaLabel')"
    :data-testid="testId('record')"
  >
    <div class="tk-eyebrow review-read-only-decision__eyebrow">
      {{ $t('review.readOnly.eyebrow') }}
    </div>
    <h2 class="review-read-only-decision__title">{{ $t('review.readOnly.title') }}</h2>
    <p class="review-read-only-decision__body">{{ $t('review.readOnly.body') }}</p>
    <dl class="review-read-only-decision__grid">
      <div>
        <dt>{{ $t('review.readOnly.decision') }}</dt>
        <dd :data-testid="testId('decision')">{{ decision }}</dd>
      </div>
      <div>
        <dt>{{ $t('review.readOnly.actor') }}</dt>
        <dd :data-testid="testId('actor')">{{ actor }}</dd>
      </div>
      <div>
        <dt>{{ $t('review.readOnly.timestamp') }}</dt>
        <dd :data-testid="testId('timestamp')">{{ timestamp }}</dd>
      </div>
      <div>
        <dt>{{ $t('review.readOnly.operations') }}</dt>
        <dd :data-testid="testId('operations')">
          {{
            $t(
              'review.readOnly.operationsValue',
              { count: proposal.operations?.length ?? 0 },
              proposal.operations?.length ?? 0,
            )
          }}
        </dd>
      </div>
    </dl>
  </section>
</template>

<style scoped>
.review-read-only-decision {
  padding: 16px;
  border: 1px solid var(--td-border-default, var(--line-soft));
  border-left: 4px solid var(--td-color-info, var(--applied));
  background: var(--td-color-info-light, var(--applied-tint));
}
.review-read-only-decision__eyebrow {
  color: var(--td-color-info, var(--applied));
}
.review-read-only-decision__title {
  margin: 5px 0 4px;
  color: var(--td-text-primary, var(--ink));
  font-family: var(--td-font-family-heading, var(--serif));
  font-size: var(--td-font-lg, 18px);
}
.review-read-only-decision__body {
  margin: 0;
  color: var(--td-text-secondary, var(--ink-2, var(--ink)));
  font-size: var(--td-font-sm, 13px);
  line-height: 1.45;
}
.review-read-only-decision__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 20px;
  margin: 16px 0 0;
}
.review-read-only-decision__grid div {
  min-width: 0;
}
.review-read-only-decision__grid dt {
  color: var(--td-text-tertiary, var(--mute));
  font-family: var(--td-font-family-mono, var(--mono));
  font-size: 10px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}
.review-read-only-decision__grid dd {
  margin: 3px 0 0;
  overflow-wrap: anywhere;
  color: var(--td-text-primary, var(--ink));
  font-size: 12.5px;
}

@media (max-width: 640px) {
  .review-read-only-decision__grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
