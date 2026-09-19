import { describe, expect, it } from 'vitest'
import { shouldConstrainCardModalToVisualViewport } from '../../components/board/card-modal/cardModalViewportPolicy'

describe('CardModal visual viewport policy (#1746)', () => {
  it('keeps a touch-capable tablet inside a shorter, offset visual viewport', () => {
    expect(shouldConstrainCardModalToVisualViewport({
      supported: true,
      touchCapable: true,
      layoutHeight: 700,
      visualHeight: 420,
      visualOffsetTop: 120,
    })).toBe(true)
  })

  it('keeps ordinary tablet and desktop geometry on the centered desktop path', () => {
    expect(shouldConstrainCardModalToVisualViewport({
      supported: true,
      touchCapable: true,
      layoutHeight: 700,
      visualHeight: 700,
      visualOffsetTop: 0,
    })).toBe(false)

    expect(shouldConstrainCardModalToVisualViewport({
      supported: true,
      touchCapable: false,
      layoutHeight: 700,
      visualHeight: 420,
      visualOffsetTop: 120,
    })).toBe(false)
  })

  it('does not claim visual-viewport authority when the API is unavailable', () => {
    expect(shouldConstrainCardModalToVisualViewport({
      supported: false,
      touchCapable: true,
      layoutHeight: 700,
      visualHeight: 420,
      visualOffsetTop: 120,
    })).toBe(false)
  })

  it('treats an offset-only contraction as keyboard constrained', () => {
    expect(shouldConstrainCardModalToVisualViewport({
      supported: true,
      touchCapable: true,
      layoutHeight: 700,
      visualHeight: 700,
      visualOffsetTop: 80,
    })).toBe(true)
  })
})
