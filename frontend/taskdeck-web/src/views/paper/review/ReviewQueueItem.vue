<script setup lang="ts">
import { computed } from 'vue'

/**
 * ReviewQueueItem — single row in the queue rail. Active items get a 2 px
 * ember left border + ember-bloom gradient; stale items dim to 0.7 opacity
 * with a whisper border. Stays a presentational component — selection is
 * driven by the parent rail.
 */
const props = withDefaults(
  defineProps<{
    serial: string
    title: string
    who: string
    confidence: number | null
    age: string
    reach: string
    active?: boolean
    stale?: boolean
  }>(),
  { active: false, stale: false },
)

const emit = defineEmits<{ (event: 'select'): void }>()

const classes = computed(() => {
  const list = ['paper-review-q']
  if (props.active) list.push('paper-review-q--active')
  if (props.stale) list.push('paper-review-q--stale')
  return list
})

const formattedConfidence = computed(() =>
  props.confidence == null ? null : props.confidence.toFixed(2),
)
</script>

<template>
  <a
    :class="classes"
    href="#"
    role="button"
    :aria-pressed="active"
    :data-serial="serial"
    @click.prevent="emit('select')"
  >
    <div class="paper-review-q__row">
      <span class="tk-serial paper-review-q__serial">{{ serial }}</span>
      <span class="tk-meta paper-review-q__age">{{ age }}</span>
    </div>
    <div class="paper-review-q__title">{{ title }}</div>
    <div class="tk-meta paper-review-q__meta">
      <span>{{ who }}</span>
      <template v-if="formattedConfidence !== null">
        <span aria-hidden="true"> · </span>
        <span>conf {{ formattedConfidence }}</span>
      </template>
      <span aria-hidden="true"> · </span>
      <span>{{ reach }}</span>
    </div>
  </a>
</template>

<style scoped>
.paper-review-q {
  display: block;
  padding: 12px 18px;
  text-decoration: none;
  color: inherit;
  border-left: 2px solid transparent;
  background: transparent;
  transition: background 200ms ease, opacity 200ms ease;
}
.paper-review-q:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: -2px;
}
.paper-review-q--active {
  border-left-color: var(--ember);
  background: linear-gradient(90deg, var(--ember-bloom) 0%, transparent 70%);
}
.paper-review-q--stale {
  border-left-color: var(--whisper);
  opacity: 0.7;
}
.paper-review-q__row {
  display: flex;
  justify-content: space-between;
  margin-bottom: 4px;
}
.paper-review-q__serial {
  color: var(--faint);
}
.paper-review-q--active .paper-review-q__serial {
  color: var(--ember);
}
.paper-review-q__age {
  font-size: 9.5px;
}
.paper-review-q__title {
  font-family: var(--serif);
  font-size: 13.5px;
  font-weight: 500;
  color: var(--ink);
  line-height: 1.3;
  margin-bottom: 4px;
}
.paper-review-q--active .paper-review-q__title {
  color: var(--ink-deep);
}
.paper-review-q__meta {
  font-size: 10px;
}
</style>
