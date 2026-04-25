<script setup lang="ts">
import { computed } from 'vue'
import { PAPER_ICON_SHAPES, type PaperIconName } from './paperIconPaths'

/**
 * PaperIcon — single SFC that renders any named hairline icon from the Paper
 * & Graphite set.  Strokes use `currentColor`; sizing is controlled by the
 * `.hl-icon` token classes (14px default, `md` 16px, `lg` 20px).
 */
const props = withDefaults(
  defineProps<{
    name: PaperIconName
    size?: 14 | 16 | 20
    /** Optional accessible label.  When omitted the SVG is `aria-hidden`. */
    label?: string
  }>(),
  { size: 14 },
)

const sizeClass = computed(() =>
  props.size === 16 ? 'hl-icon-md' : props.size === 20 ? 'hl-icon-lg' : '',
)

const shapes = computed(() => PAPER_ICON_SHAPES[props.name] ?? [])

const a11y = computed(() =>
  props.label
    ? { role: 'img', 'aria-label': props.label }
    : { 'aria-hidden': 'true' as const },
)
</script>

<template>
  <svg
    :class="['hl-icon', sizeClass]"
    viewBox="0 0 16 16"
    :data-icon="name"
    v-bind="a11y"
  >
    <template v-for="(shape, i) in shapes" :key="i">
      <path v-if="shape.kind === 'path'" :d="shape.d" />
      <circle
        v-else-if="shape.kind === 'circle'"
        :cx="shape.cx"
        :cy="shape.cy"
        :r="shape.r"
      />
      <rect
        v-else-if="shape.kind === 'rect'"
        :x="shape.x"
        :y="shape.y"
        :width="shape.width"
        :height="shape.height"
      />
    </template>
  </svg>
</template>
