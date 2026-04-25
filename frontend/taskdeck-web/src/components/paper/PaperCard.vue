<script setup lang="ts">
import { computed } from 'vue'

/**
 * PaperCard — paper-stock surface in three flavours.
 * - `flat` → `.card`   (single hairline shadow)
 * - `lift` → `.card-lift` (stronger lift; used for decision rails)
 * - `well` → `.well`   (recessed surface for column wells)
 *
 * The optional `halo` prop adds the ember halo used to mark active proposals.
 */
export type PaperCardVariant = 'flat' | 'lift' | 'well'

const props = withDefaults(
  defineProps<{
    variant?: PaperCardVariant
    halo?: boolean
    /** Render as a different element if needed (e.g. `<article>`). */
    as?: keyof HTMLElementTagNameMap
  }>(),
  {
    variant: 'flat',
    halo: false,
    as: 'div',
  },
)

const classes = computed(() => {
  const list: string[] = []
  if (props.variant === 'lift') list.push('card-lift')
  else if (props.variant === 'well') list.push('well')
  else list.push('card')
  if (props.halo) list.push('halo-ember')
  return list
})
</script>

<template>
  <component
    :is="as"
    :class="classes"
    :data-variant="variant"
    :data-halo="halo ? 'true' : null"
  >
    <slot />
  </component>
</template>
