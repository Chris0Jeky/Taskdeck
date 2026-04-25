import { onBeforeUnmount, readonly, ref, type Ref } from 'vue'

export type ViewportMode = 'desktop' | 'tablet' | 'phone'

const PHONE_QUERY = '(max-width: 480px)'
const TABLET_QUERY = '(max-width: 1024px)'

function detect(): ViewportMode {
  if (typeof window === 'undefined' || !window.matchMedia) return 'desktop'
  if (window.matchMedia(PHONE_QUERY).matches) return 'phone'
  if (window.matchMedia(TABLET_QUERY).matches) return 'tablet'
  return 'desktop'
}

/**
 * Reactive viewport mode for Paper narrow companions.
 * Listens to matchMedia changes and tears down on unmount.
 */
export function useViewportMode(): { mode: Readonly<Ref<ViewportMode>> } {
  const mode = ref<ViewportMode>(detect())

  if (typeof window === 'undefined' || !window.matchMedia) {
    return { mode: readonly(mode) }
  }

  const phoneMq = window.matchMedia(PHONE_QUERY)
  const tabletMq = window.matchMedia(TABLET_QUERY)

  const update = () => {
    mode.value = detect()
  }

  phoneMq.addEventListener?.('change', update)
  tabletMq.addEventListener?.('change', update)

  onBeforeUnmount(() => {
    phoneMq.removeEventListener?.('change', update)
    tabletMq.removeEventListener?.('change', update)
  })

  return { mode: readonly(mode) }
}
