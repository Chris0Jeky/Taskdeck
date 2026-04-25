<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

/**
 * PaperStamp — round embossed stamp used on cards and proposals.
 *
 * The rotation is computed once on mount (random −7° to −9° unless the caller
 * pins it explicitly) so re-renders don't make the stamp twitch.  When the
 * stamp transitions from `applied` → `proposed` we crossfade to the proposed
 * state over 240ms to convey an undo; reduced-motion users get an instant
 * swap with no animation.
 */
export type PaperStampKind =
  | 'applied'
  | 'proposed'
  | 'captured'
  | 'overdue'
  | 'draft'

const props = withDefaults(
  defineProps<{
    kind?: PaperStampKind
    date?: string
    time?: string
    num?: string
    /** Force a specific rotation (deg).  When omitted a stable −7° to −9° is
     *  picked once at mount. */
    rotate?: number
  }>(),
  {
    kind: 'applied',
    date: '',
    time: '',
    num: '',
  },
)

interface KindMeta {
  label: string
  /** Modifier class applied to `.stamp` (token CSS already styles colour). */
  modifier: '' | 'ember' | 'applied' | 'overdue'
  embossed: boolean
}

const KIND_META: Record<PaperStampKind, KindMeta> = {
  applied: { label: 'Reviewed', modifier: 'applied', embossed: true },
  proposed: { label: 'Proposed', modifier: 'ember', embossed: false },
  captured: { label: 'Captured', modifier: '', embossed: false },
  overdue: { label: 'Overdue', modifier: 'overdue', embossed: false },
  draft: { label: 'Draft', modifier: '', embossed: false },
}

// Pick a stable rotation once for the lifetime of the component.
const fallbackRotate = -7 - Math.random() * 2 // −7° to −9°
const rotation = computed(() =>
  typeof props.rotate === 'number' ? props.rotate : fallbackRotate,
)

const meta = computed(() => KIND_META[props.kind])

const classes = computed(() => {
  const list = ['stamp']
  if (meta.value.modifier) list.push(meta.value.modifier)
  if (meta.value.embossed) list.push('stamp--embossed')
  return list
})

// Undo crossfade: when kind changes from `applied` to `proposed` (or any
// transition really) trigger a brief opacity blink so the swap reads as a
// physical re-stamp rather than an instant text change.
const fading = ref(false)
let fadeTimer: ReturnType<typeof setTimeout> | null = null

const prefersReducedMotion = (): boolean => {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return false
  }
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

watch(
  () => props.kind,
  (next, prev) => {
    if (prev === undefined || next === prev) return
    if (prefersReducedMotion()) return
    fading.value = true
    if (fadeTimer) clearTimeout(fadeTimer)
    fadeTimer = setTimeout(() => {
      fading.value = false
      fadeTimer = null
    }, 240)
  },
)

onBeforeUnmount(() => {
  if (fadeTimer) {
    clearTimeout(fadeTimer)
    fadeTimer = null
  }
})
</script>

<template>
  <span
    :class="classes"
    :data-kind="kind"
    :data-fading="fading ? 'true' : null"
    :style="{ transform: `rotate(${rotation}deg)` }"
  >
    <span class="stamp__label">{{ meta.label }}</span>
    <b v-if="date">{{ date }}</b>
    <span v-if="time || num" class="stamp-num">
      <template v-if="time">{{ time }}</template>
      <template v-if="time && num"> · </template>
      <template v-if="num">#{{ num }}</template>
    </span>
  </span>
</template>

<style scoped>
.stamp {
  transition: opacity 240ms cubic-bezier(0.2, 0.65, 0.25, 1);
}
.stamp[data-fading='true'] {
  opacity: 0.55;
}
.stamp--embossed {
  /* paper-token shadow-press already defined inset; layer with a hairline. */
  box-shadow: var(--shadow-press), 0 0 0 1px currentColor inset;
}
@media (prefers-reduced-motion: reduce) {
  .stamp {
    transition: none;
  }
}
</style>
