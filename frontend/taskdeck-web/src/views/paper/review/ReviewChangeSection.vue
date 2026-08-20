<script setup lang="ts">
/**
 * ReviewChangeSection — § I: before/after card plus per-field diff strip.
 * Currently driven by props supplied by the orchestrator. Once the
 * backend-gap issue (per-field diff) lands, the strip will be derived
 * from the proposal payload directly.
 */
export interface ChangeBeforeCard {
  serial: string
  title: string
  body: string
  meta: string
}

export type ChangeAfterStatus = 'kept' | 'new'

export interface ChangeAfterCard {
  serial: string
  title: string
  body: string
  status: ChangeAfterStatus
}

export interface FieldDiff {
  key: string
  before: string
  after: string
  same?: boolean
}

defineProps<{
  before: ChangeBeforeCard
  after: ChangeAfterCard[]
  fields: FieldDiff[]
  subTitle: string
}>()
</script>

<template>
  <section class="paper-review-change">
    <header class="paper-review-change__header">
      <span class="tk-serial paper-review-change__serial">§ I</span>
      <h3 class="tk-h3 paper-review-change__title">{{ $t('review.change.title') }}</h3>
      <span class="tk-meta paper-review-change__sub">{{ subTitle }}</span>
    </header>
    <div class="card paper-review-change__card">
      <div class="paper-review-change__grid">
        <div class="paper-review-change__col paper-review-change__col--before">
          <div class="tk-eyebrow paper-review-change__eyebrow">
            {{ $t('review.change.beforeEyebrow') }}
          </div>
          <article class="card paper-review-change__before">
            <div class="tk-serial">{{ before.serial }}</div>
            <h4 class="paper-review-change__before-title">{{ before.title }}</h4>
            <p class="paper-review-change__before-body">{{ before.body }}</p>
            <div class="tk-meta paper-review-change__before-meta">{{ before.meta }}</div>
          </article>
        </div>
        <div class="paper-review-change__col paper-review-change__col--after">
          <div class="tk-eyebrow paper-review-change__eyebrow paper-review-change__eyebrow--after">
            {{ $t('review.change.afterEyebrow') }}
          </div>
          <div class="paper-review-change__after-stack">
            <article
              v-for="card in after"
              :key="card.serial"
              class="card paper-review-change__after-card"
              :data-status="card.status"
            >
              <div class="paper-review-change__after-head">
                <span class="tk-serial">
                  {{ card.serial }}
                  <span
                    v-if="card.status === 'new'"
                    class="paper-review-change__after-tag paper-review-change__after-tag--new"
                  > {{ $t('review.change.tag.new') }}</span>
                </span>
                <span
                  v-if="card.status === 'kept'"
                  class="tk-serial paper-review-change__after-tag paper-review-change__after-tag--kept"
                >{{ $t('review.change.tag.kept') }}</span>
              </div>
              <h5 class="paper-review-change__after-title">{{ card.title }}</h5>
              <p class="paper-review-change__after-body">{{ card.body }}</p>
            </article>
          </div>
        </div>
      </div>
      <div class="paper-review-change__fields">
        <div class="tk-eyebrow paper-review-change__fields-heading">
          {{ $t('review.change.fieldsHeading') }}
        </div>
        <div class="paper-review-change__fields-grid">
          <template v-for="field in fields" :key="field.key">
            <div class="tk-eyebrow">{{ field.key }}</div>
            <div
              class="diff-rem paper-review-change__field-cell"
              :data-same="field.same ? 'true' : null"
            >{{ field.before }}</div>
            <div
              class="diff-add paper-review-change__field-cell"
              :data-same="field.same ? 'true' : null"
            >{{ field.after }}</div>
          </template>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.paper-review-change {
  margin-top: 22px;
}
.paper-review-change__header {
  display: flex;
  align-items: baseline;
  gap: 14px;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-review-change__serial {
  color: var(--faint);
}
.paper-review-change__title {
  margin: 0;
}
.paper-review-change__sub {
  margin-left: auto;
}
.paper-review-change__card {
  padding: 0;
  overflow: hidden;
}
.paper-review-change__grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
}
.paper-review-change__col {
  padding: 22px;
}
.paper-review-change__col--before {
  border-right: 1px solid var(--line-soft);
}
.paper-review-change__col--after {
  background: linear-gradient(90deg, transparent 0%, var(--ember-bloom) 100%);
}
.paper-review-change__eyebrow {
  margin-bottom: 10px;
}
.paper-review-change__eyebrow--after {
  color: var(--ember);
}
.paper-review-change__before-title {
  margin: 4px 0;
  font-family: var(--serif);
  font-size: 16px;
  font-weight: 500;
}
.paper-review-change__before-body {
  margin: 0;
  font-size: 12.5px;
  color: var(--ink-2, var(--ink));
}
.paper-review-change__before-meta {
  font-size: 10px;
  margin-top: 10px;
}
.paper-review-change__after-stack {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.paper-review-change__after-card {
  padding: 12px;
  background: var(--paper-card);
  border-left: 2px solid var(--ember);
}
.paper-review-change__after-card[data-status='new'] {
  border-color: var(--applied);
  border-left-color: var(--applied);
}
.paper-review-change__after-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.paper-review-change__after-tag--new {
  color: var(--applied);
  margin-left: 4px;
}
.paper-review-change__after-tag--kept {
  color: var(--ember);
}
.paper-review-change__after-title {
  margin: 4px 0;
  font-family: var(--serif);
  font-size: 14.5px;
  font-weight: 500;
}
.paper-review-change__after-body {
  margin: 0;
  font-size: 12px;
  color: var(--ink-2, var(--ink));
}
.paper-review-change__fields {
  border-top: 1px solid var(--line-soft);
  padding: 14px 22px;
  background: var(--paper-2);
}
.paper-review-change__fields-heading {
  margin-bottom: 10px;
}
.paper-review-change__fields-grid {
  display: grid;
  grid-template-columns: 100px 1fr 1fr;
  gap: 8px;
}
.paper-review-change__field-cell {
  font-size: 11.5px;
}
.paper-review-change__field-cell[data-same='true'] {
  opacity: 0.35;
  text-decoration: none;
}
</style>
