<script setup lang="ts">
import { computed } from 'vue'

/**
 * PaperStatusPill — small mono uppercase status indicator with a leading
 * dot.  Wraps `.pstatus` plus the kind modifier; the `live` variant
 * already has a 0.6Hz pulse keyframe defined in `paper-tokens.css` (and a
 * `prefers-reduced-motion` override that disables the pulse).
 */
export type PaperStatusKind = 'proposed' | 'applied' | 'overdue' | 'draft' | 'live'

const props = withDefaults(
  defineProps<{
    kind?: PaperStatusKind
  }>(),
  { kind: 'draft' },
)

const classes = computed(() => ['pstatus', `pstatus--${props.kind}`, props.kind])
</script>

<template>
  <span :class="classes" :data-kind="kind">
    <slot />
  </span>
</template>
