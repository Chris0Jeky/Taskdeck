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
 * Review landmark. `tabindex="-1"` keeps it out of the ordinary Tab order. The
 * landmark reuses the localized return-to-Review label because this fallback is
 * reached only through that action, and the name must follow live locale changes.
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
    surface.focus()
  },
  { flush: 'post' },
)
</script>

<template>
  <PaperReviewView
    v-if="paperTheme.isOn"
    ref="reviewSurfaceRef"
    role="region"
    :aria-label="t('review.empty.unavailable.return')"
    tabindex="-1"
  />
  <LegacyReviewView
    v-else
    ref="reviewSurfaceRef"
    role="region"
    :aria-label="t('review.empty.unavailable.return')"
    tabindex="-1"
  />
</template>
