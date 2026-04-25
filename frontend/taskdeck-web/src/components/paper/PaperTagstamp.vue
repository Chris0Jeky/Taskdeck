<script setup lang="ts">
import { computed } from 'vue'

/**
 * PaperTagstamp — tiny mono uppercase tag.  Wraps `.tagstamp` and applies
 * the chosen tone via CSS custom properties.  The token CSS already gives
 * `.tagstamp` a `letter-spacing: .22em` and a 1px hairline border in the
 * current text color, so we only need to pick the colour for the tone.
 */
export type PaperTagstampTone = 'ember' | 'applied' | 'overdue' | 'mute'

const props = withDefaults(
  defineProps<{
    tone?: PaperTagstampTone
  }>(),
  { tone: 'mute' },
)

/**
 * Map tone → CSS variable in `paper-tokens.css`.  The colour is applied to
 * `color` (so the `.tagstamp` `border: 1px solid currentColor` follows).
 */
const colorVar = computed(() => {
  switch (props.tone) {
    case 'ember':
      return 'var(--ember)'
    case 'applied':
      return 'var(--applied)'
    case 'overdue':
      return 'var(--overdue)'
    default:
      return 'var(--mute)'
  }
})
</script>

<template>
  <span class="tagstamp" :data-tone="tone" :style="{ color: colorVar }">
    <slot />
  </span>
</template>
