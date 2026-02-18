import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

type RegisterEscapeHandler = (onEscape: () => void) => () => void

let registerEscapeHandler: RegisterEscapeHandler
let cleanup: Array<() => void> = []

async function loadEscapeStack() {
  vi.resetModules()
  const module = await import('../../composables/useEscapeStack')
  registerEscapeHandler = module.registerEscapeHandler
}

function pressEscape() {
  const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
  window.dispatchEvent(event)
}

describe('useEscapeStack', () => {
  beforeEach(async () => {
    cleanup = []
    await loadEscapeStack()
  })

  afterEach(() => {
    for (const unregister of cleanup.reverse()) {
      unregister()
    }
    cleanup = []
  })

  it('supports unregistering from the middle of the stack', () => {
    const first = vi.fn()
    const middle = vi.fn()
    const top = vi.fn()

    const unregisterFirst = registerEscapeHandler(first)
    const unregisterMiddle = registerEscapeHandler(middle)
    const unregisterTop = registerEscapeHandler(top)
    cleanup.push(unregisterFirst, unregisterMiddle, unregisterTop)

    unregisterMiddle()

    pressEscape()
    expect(top).toHaveBeenCalledTimes(1)
    expect(middle).not.toHaveBeenCalled()
    expect(first).not.toHaveBeenCalled()

    unregisterTop()

    pressEscape()
    expect(first).toHaveBeenCalledTimes(1)
    expect(middle).not.toHaveBeenCalled()
  })

  it('handles rapid registrations and unregistrations without leaking order', () => {
    const callbacks = Array.from({ length: 20 }, () => vi.fn())
    const unregisters = callbacks.map((callback) => registerEscapeHandler(callback))
    cleanup.push(...unregisters)

    // Remove every even index; last remaining should be index 19.
    unregisters.forEach((unregister, index) => {
      if (index % 2 === 0) {
        unregister()
      }
    })

    pressEscape()
    expect(callbacks[19]).toHaveBeenCalledTimes(1)
    expect(callbacks[18]).not.toHaveBeenCalled()

    // Now remove the current top and verify next live handler becomes active.
    unregisters[19]?.()
    pressEscape()
    expect(callbacks[17]).toHaveBeenCalledTimes(1)
  })

  it('treats duplicate registrations of the same function as distinct stack entries', () => {
    const sharedCallback = vi.fn()

    const unregisterFirst = registerEscapeHandler(sharedCallback)
    const unregisterSecond = registerEscapeHandler(sharedCallback)
    cleanup.push(unregisterFirst, unregisterSecond)

    pressEscape()
    expect(sharedCallback).toHaveBeenCalledTimes(1)

    unregisterSecond()
    pressEscape()
    expect(sharedCallback).toHaveBeenCalledTimes(2)
  })
})
