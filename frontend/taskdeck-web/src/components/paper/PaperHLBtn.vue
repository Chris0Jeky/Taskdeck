<script setup lang="ts">
import { computed } from 'vue'
import { formatShortcut } from '../../utils/keyboardShortcuts'
import PaperKbd from './PaperKbd.vue'

/**
 * PaperHLBtn — hairline button with optional leading icon and trailing
 * keyboard hint.  Wraps `.pbtn` plus an optional variant class.  The kbd
 * hint is rendered via `<PaperKbd>` and separated from the label with a
 * 1px vertical divider so wide keys (`space`, platform modifiers) don't crowd
 * the text.
 *
 * The label may be passed as the `label` prop or via the default slot.
 * `:active { transform: translateY(1px) }` is supplied by the token CSS.
 */
export type PaperHLBtnVariant = 'default' | 'primary' | 'ember' | 'ghost'

const props = withDefaults(
  defineProps<{
    label?: string
    kbd?: string
    variant?: PaperHLBtnVariant
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
  }>(),
  {
    variant: 'default',
    type: 'button',
    disabled: false,
  },
)

const emit = defineEmits<{
  (event: 'click', e: MouseEvent): void
}>()

const classes = computed(() => {
  const base = ['pbtn']
  if (props.variant === 'primary') base.push('pbtn-primary')
  else if (props.variant === 'ember') base.push('pbtn-ember')
  else if (props.variant === 'ghost') base.push('pbtn-ghost')
  return base
})

const displayedKbd = computed(() => props.kbd ? formatShortcut(props.kbd) : '')

function onClick(e: MouseEvent) {
  if (props.disabled) return
  emit('click', e)
}
</script>

<template>
  <button
    :class="classes"
    :type="type"
    :disabled="disabled"
    :data-variant="variant"
    @click="onClick"
  >
    <span v-if="$slots.icon" class="phlbtn-icon"><slot name="icon" /></span>
    <span class="phlbtn-label">
      <slot>{{ label }}</slot>
    </span>
    <template v-if="kbd">
      <span class="phlbtn-divider" aria-hidden="true" />
      <PaperKbd>{{ displayedKbd }}</PaperKbd>
    </template>
  </button>
</template>

<style scoped>
.phlbtn-icon {
  display: inline-flex;
  align-items: center;
}
.phlbtn-label {
  /* keep label on a single line so the kbd divider stays vertical */
  display: inline-flex;
  align-items: center;
}
.phlbtn-divider {
  display: inline-block;
  width: 1px;
  align-self: stretch;
  margin: 2px 4px 2px 2px;
  background: currentColor;
  opacity: 0.18;
}
</style>
