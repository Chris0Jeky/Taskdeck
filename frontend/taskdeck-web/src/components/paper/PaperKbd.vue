<script setup lang="ts">
import { computed } from 'vue'

/**
 * PaperKbd — keyboard glyph chip.  Wraps `.pkbd` (default) or `.pkbd-light`
 * (when `light` is true).  The element is a real `<kbd>` for assistive tech.
 */
const props = withDefaults(
  defineProps<{
    light?: boolean
  }>(),
  { light: false },
)

const classes = computed(() => ['pkbd', props.light ? 'pkbd-light' : null].filter(Boolean))
</script>

<template>
  <kbd :class="classes" data-paper-kbd>
    <slot />
  </kbd>
</template>
