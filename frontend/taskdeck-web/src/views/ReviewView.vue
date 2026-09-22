<script setup lang="ts">
import { nextTick, ref, watch, type ComponentPublicInstance } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import PaperReviewView from './paper/PaperReviewView.vue'
import LegacyReviewView from './LegacyReviewView.vue'
import { usePaperThemeStore } from '../store/paperThemeStore'

const paperTheme = usePaperThemeStore()
const route = useRoute()
const { t } = useI18n()
const reviewSurfaceRef = ref<ComponentPublicInstance | null>(null)

function focusTransiently(surface: HTMLElement) {
  surface.setAttribute('tabindex', '-1')
  let released = false
  const release = () => {
    if (released) return
    released = true
    surface.removeEventListener('blur', release)
    surface.removeEventListener('pointerdown', release, true)
    surface.removeEventListener('click', release)
    surface.removeAttribute('tabindex')
  }

  surface.addEventListener('blur', release, { once: true })
  surface.addEventListener('pointerdown', release, { once: true, capture: true })
  surface.addEventListener('click', release, { once: true })
  surface.focus()
  if (document.activeElement !== surface) release()
}

/**
 * Both Review skins own the primary unavailable-pin handoff (#2599): Paper
 * prefers its first queue row and Legacy its queue section, then each falls
 * back to its empty state. Two transient layouts can remove both targets while
 * the return control removes itself: Paper can retain a decision receipt behind
 * a filtered-empty rail, and Legacy can be covered by an explicit-load skeleton.
 *
 * Keep this route wrapper as the last resort rather than teaching either skin
 * about the other's layout. The two ticks let their own queue/empty handoff win;
 * only a focus loss all the way to the document receives the stable, named
 * Review landmark. The landmark uses a dedicated localized surface name rather
 * than the return action's label, and that name follows live locale changes.
 *
 * `tabindex` is applied for this handoff only. It is removed on blur, the first
 * pointer or synthesized click interaction, or immediately when focus does not
 * take. A PERMANENT `tabindex="-1"` would make every click on inert review
 * content focus this root (the HTML focusing steps walk up to the nearest
 * focusable ancestor), and both skins' own handoffs read "activeElement is not
 * the document" as "the reviewer moved focus deliberately" - PaperReviewView's
 * unavailable-return handoff and ReviewMain's decision-receipt handoff would
 * then stop firing for the rest of the visit.
 */
watch(
  () => route.hash,
  async (hash, previousHash) => {
    if (!previousHash.startsWith('#proposal-') || hash.startsWith('#proposal-')) return

    await nextTick()
    await nextTick()

    const active = document.activeElement
    if (active !== document.body && active !== document.documentElement) return

    const surface = reviewSurfaceRef.value?.$el as HTMLElement | undefined
    if (!surface?.isConnected) return
    focusTransiently(surface)
  },
  { flush: 'post' },
)
</script>

<template>
  <PaperReviewView
    v-if="paperTheme.isOn"
    ref="reviewSurfaceRef"
    role="region"
    :aria-label="t('review.surfaceLabel')"
  />
  <LegacyReviewView
    v-else
    ref="reviewSurfaceRef"
    role="region"
    :aria-label="t('review.surfaceLabel')"
  />
</template>
