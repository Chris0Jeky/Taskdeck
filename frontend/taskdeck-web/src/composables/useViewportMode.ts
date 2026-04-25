import { onScopeDispose, readonly, ref, type Ref } from 'vue'

export type ViewportMode = 'desktop' | 'tablet' | 'phone'

const PHONE_QUERY = '(max-width: 480px)'
const TABLET_QUERY = '(max-width: 1024px)'

function classify(phoneMatches: boolean, tabletMatches: boolean): ViewportMode {
  if (phoneMatches) return 'phone'
  if (tabletMatches) return 'tablet'
  return 'desktop'
}

/**
 * Reactive viewport mode for Paper narrow companions.
 * Listens to matchMedia changes and tears down via onScopeDispose so the
 * composable can be used inside components, Pinia stores, or any other
 * reactive effect scope.
 */
export function useViewportMode(): { mode: Readonly<Ref<ViewportMode>> } {
  const mode = ref<ViewportMode>('desktop')

  if (typeof window === 'undefined' || !window.matchMedia) {
    return { mode: readonly(mode) }
  }

  const phoneMq = window.matchMedia(PHONE_QUERY)
  const tabletMq = window.matchMedia(TABLET_QUERY)

  const update = () => {
    mode.value = classify(phoneMq.matches, tabletMq.matches)
  }

  // Initialise from the cached MQL state — no extra matchMedia() calls.
  update()

  phoneMq.addEventListener?.('change', update)
  tabletMq.addEventListener?.('change', update)

  onScopeDispose(() => {
    phoneMq.removeEventListener?.('change', update)
    tabletMq.removeEventListener?.('change', update)
  })

  return { mode: readonly(mode) }
}
