export interface CardModalViewportSnapshot {
  supported: boolean
  touchCapable: boolean
  layoutHeight: number
  visualHeight: number
  visualOffsetTop: number
}

const VIEWPORT_GEOMETRY_EPSILON_PX = 1

/**
 * Selects the keyboard-constrained geometry used by the modal presentation.
 *
 * A wide touch device can cross the desktop breakpoint while its software
 * keyboard contracts or offsets the visual viewport. Fine-pointer desktops
 * keep their ordinary centred layout even if a synthetic/devtools viewport
 * reports different geometry; pinch zoom is already filtered by
 * `useVisualViewport`, so this function only consumes trusted measurements.
 */
export function shouldConstrainCardModalToVisualViewport(
  snapshot: CardModalViewportSnapshot,
): boolean {
  if (!snapshot.supported || !snapshot.touchCapable) return false

  const measurements = [
    snapshot.layoutHeight,
    snapshot.visualHeight,
    snapshot.visualOffsetTop,
  ]
  if (measurements.some(value => !Number.isFinite(value))) return false

  return snapshot.visualOffsetTop > VIEWPORT_GEOMETRY_EPSILON_PX
    || snapshot.visualHeight < snapshot.layoutHeight - VIEWPORT_GEOMETRY_EPSILON_PX
}
