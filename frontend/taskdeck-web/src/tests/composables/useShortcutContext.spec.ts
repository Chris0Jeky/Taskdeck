import { mount, type VueWrapper } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

type ShortcutContextModule = typeof import('../../composables/useShortcutContext')

let shortcutContextModule: ShortcutContextModule
let wrappers: VueWrapper[] = []

async function loadShortcutContextModule() {
  vi.resetModules()
  shortcutContextModule = await import('../../composables/useShortcutContext')
}

function pressKey(key: string) {
  document.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
}

function mountContext(
  context: ShortcutContextModule['ShortcutContext'],
  shortcuts: Array<{
    key: string
    handler: () => void
    description: string
    ctrl?: boolean
    shift?: boolean
    alt?: boolean
  }>
) {
  const TestComponent = defineComponent({
    setup() {
      shortcutContextModule.useContextualShortcuts(context, shortcuts)
      return {}
    },
    template: '<div></div>',
  })

  const wrapper = mount(TestComponent, { attachTo: document.body })
  wrappers.push(wrapper)
  return wrapper
}

describe('useShortcutContext', () => {
  beforeEach(async () => {
    wrappers = []
    await loadShortcutContextModule()
  })

  afterEach(() => {
    for (const wrapper of wrappers.reverse()) {
      wrapper.unmount()
    }
    wrappers = []
  })

  it('makes a pushed context the active context', () => {
    const context = shortcutContextModule.useShortcutContext()

    context.pushContext('board-canvas')

    expect(context.activeContext()).toBe('board-canvas')
  })

  it('popping a context restores the previous one', () => {
    const context = shortcutContextModule.useShortcutContext()

    context.pushContext('board-canvas')
    context.pushContext('modal/drawer')

    context.popContext('modal/drawer')

    expect(context.activeContext()).toBe('board-canvas')
  })

  it('fires shortcuts registered in the active context', () => {
    const activeHandler = vi.fn()

    mountContext('board-canvas', [
      { key: 'j', description: 'Next card', handler: activeHandler },
    ])

    pressKey('j')

    expect(activeHandler).toHaveBeenCalledTimes(1)
  })

  it('does not fire shortcuts in a non-active stacked context', () => {
    const boardHandler = vi.fn()
    const modalHandler = vi.fn()

    mountContext('board-canvas', [
      { key: 'j', description: 'Board shortcut', handler: boardHandler },
    ])
    mountContext('modal/drawer', [
      { key: 'j', description: 'Modal shortcut', handler: modalHandler },
    ])

    pressKey('j')

    expect(modalHandler).toHaveBeenCalledTimes(1)
    expect(boardHandler).not.toHaveBeenCalled()
  })

  it('restores the previous context shortcuts after popping the top context', () => {
    const boardHandler = vi.fn()
    const modalHandler = vi.fn()

    mountContext('board-canvas', [
      { key: 'j', description: 'Board shortcut', handler: boardHandler },
    ])
    const modalWrapper = mountContext('modal/drawer', [
      { key: 'j', description: 'Modal shortcut', handler: modalHandler },
    ])

    pressKey('j')
    expect(modalHandler).toHaveBeenCalledTimes(1)
    expect(boardHandler).not.toHaveBeenCalled()

    modalWrapper.unmount()
    wrappers = wrappers.filter((wrapper) => wrapper !== modalWrapper)

    pressKey('j')

    expect(boardHandler).toHaveBeenCalledTimes(1)
  })

  it('supports stacking and unwinding multiple contexts in order', () => {
    const context = shortcutContextModule.useShortcutContext()

    context.pushContext('board-canvas')
    context.pushContext('card-editor')
    context.pushContext('modal/drawer')

    expect(context.activeContext()).toBe('modal/drawer')

    context.popContext('modal/drawer')
    expect(context.activeContext()).toBe('card-editor')

    context.popContext('card-editor')
    expect(context.activeContext()).toBe('board-canvas')

    context.popContext('board-canvas')
    expect(context.activeContext()).toBe('global-shell')
  })

  it('allows popping an empty stack without throwing', () => {
    const context = shortcutContextModule.useShortcutContext()

    context.popContext('global-shell')

    expect(context.activeContext()).toBe('global-shell')
    expect(() => context.popContext('global-shell')).not.toThrow()
    expect(() => context.popContext('modal/drawer')).not.toThrow()
    expect(context.activeContext()).toBe('global-shell')
  })
})
