<script setup lang="ts">
import { computed } from 'vue'

/**
 * PaperEmptyState — generic "quiet room" empty surface used everywhere a list,
 * a board, the inbox, or a search returns nothing.  Mirrors `Empty` in
 * `design_handoff_taskdeck_paper/paper/surface-misc.jsx`.
 *
 * Two tones:
 *   - `neutral` (default) — paper-card background, used for read-only voids.
 *   - `ember`             — ember-tinted background for actionable empties
 *                           (e.g. "No boards yet" with a CTA).
 *
 * The component renders a serif italic mark, the title, an optional body, and
 * an optional CTA slot.  No illustrations.  Keep copy short; this is paper, not
 * a marketing page.
 */
export type PaperEmptyStateTone = 'neutral' | 'ember'

const props = withDefaults(
  defineProps<{
    /** Optional serif-italic mark glyph (e.g. "✎", "◇", "○").  Defaults to "·". */
    mark?: string
    /** Tone-tinted background.  Use `ember` only when a CTA is rendered. */
    tone?: PaperEmptyStateTone
  }>(),
  {
    mark: '·',
    tone: 'neutral',
  },
)

const classes = computed(() => ['paper-empty-state', `paper-empty-state--${props.tone}`])
</script>

<template>
  <div :class="classes" :data-tone="tone">
    <span class="paper-empty-state__mark" aria-hidden="true">{{ mark }}</span>
    <div class="paper-empty-state__body">
      <h3 class="paper-empty-state__title">
        <slot name="title" />
      </h3>
      <p v-if="$slots.default" class="paper-empty-state__copy">
        <slot />
      </p>
      <div v-if="$slots.cta" class="paper-empty-state__cta">
        <slot name="cta" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.paper-empty-state {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  gap: 18px;
  padding: 22px;
  min-height: 200px;
  border: 1px solid var(--line);
  border-radius: 3px;
  background: var(--paper-card);
  font-family: var(--sans);
  text-align: left;
}

.paper-empty-state--ember {
  background: var(--ember-tint);
  border-color: var(--ember);
}

.paper-empty-state__mark {
  font-family: var(--serif);
  font-style: italic;
  font-size: 36px;
  line-height: 1;
  opacity: 0.6;
  color: var(--ember);
}

.paper-empty-state__title {
  margin: 0 0 4px;
  font-family: var(--serif);
  font-style: italic;
  font-size: 17px;
  font-weight: 500;
  color: var(--ink-deep);
}

.paper-empty-state__copy {
  margin: 0;
  font-size: 12.5px;
  color: var(--ink-2);
  line-height: 1.5;
}

.paper-empty-state__cta {
  margin-top: 12px;
}
</style>
