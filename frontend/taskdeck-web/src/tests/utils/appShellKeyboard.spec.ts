import { afterEach, describe, expect, it } from 'vitest'
import {
  activeKeyboardOwningSurfaces,
  isTextEntryTarget,
  shellSurfaceOwnsAction,
} from '../../utils/appShellKeyboard'

const createdNodes: HTMLElement[] = []

function createNode<K extends keyof HTMLElementTagNameMap>(tagName: K, attributes: Record<string, string> = {}) {
  const node = document.createElement(tagName)
  for (const [name, value] of Object.entries(attributes)) node.setAttribute(name, value)
  document.body.append(node)
  createdNodes.push(node)
  return node
}

afterEach(() => {
  for (const node of createdNodes.splice(0)) node.remove()
})

describe('isTextEntryTarget', () => {
  it('recognises form controls and editable descendants', () => {
    expect(isTextEntryTarget(createNode('input'))).toBe(true)
    expect(isTextEntryTarget(createNode('textarea'))).toBe(true)
    expect(isTextEntryTarget(createNode('select'))).toBe(true)

    const editor = createNode('div', { contenteditable: 'true' })
    const child = document.createElement('span')
    editor.append(child)
    expect(isTextEntryTarget(child)).toBe(true)
  })

  it('does not treat ordinary or explicitly non-editable content as text entry', () => {
    expect(isTextEntryTarget(null)).toBe(false)
    expect(isTextEntryTarget(createNode('button'))).toBe(false)
    expect(isTextEntryTarget(createNode('div', { contenteditable: 'false' }))).toBe(false)
  })
})

describe('activeKeyboardOwningSurfaces', () => {
  it('includes visible native, alert and explicitly modal surfaces', () => {
    const nativeDialog = createNode('dialog', { open: '' })
    nativeDialog.style.display = 'block'
    const alertDialog = createNode('div', { role: 'alertdialog' })
    const ariaModal = createNode('div', { 'aria-modal': 'true' })

    const active = activeKeyboardOwningSurfaces()

    expect(active).toEqual(expect.arrayContaining([nativeDialog, alertDialog, ariaModal]))
  })

  it('excludes bare dialogs and surfaces that cannot receive input', () => {
    const bareDialog = createNode('div', { role: 'dialog' })
    const hidden = createNode('div', { 'aria-modal': 'true', hidden: '' })
    const ariaHidden = createNode('div', { 'aria-modal': 'true', 'aria-hidden': 'true' })
    const inert = createNode('div', { 'aria-modal': 'true', inert: '' })
    const displayNone = createNode('div', { 'aria-modal': 'true' })
    displayNone.style.display = 'none'
    const invisible = createNode('div', { 'aria-modal': 'true' })
    invisible.style.visibility = 'hidden'
    const disconnected = document.createElement('div')
    disconnected.setAttribute('aria-modal', 'true')

    const active = activeKeyboardOwningSurfaces()

    for (const surface of [bareDialog, hidden, ariaHidden, inert, displayNone, invisible, disconnected]) {
      expect(active).not.toContain(surface)
    }
  })
})

describe('shellSurfaceOwnsAction', () => {
  it('allows every action with no active surfaces', () => {
    expect(shellSurfaceOwnsAction([], { type: 'navigate', path: '/workspace/home' })).toBe(true)
  })

  it('allows a shell surface to handle its own action but not another surface action', () => {
    const help = createNode('div', { 'data-shell-surface': 'keyboard-help' })

    expect(shellSurfaceOwnsAction([help], { type: 'keyboard-help' })).toBe(true)
    expect(shellSurfaceOwnsAction([help], { type: 'command-palette' })).toBe(false)
  })

  it('requires every active surface to identify as shell-owned', () => {
    const help = createNode('div', { 'data-shell-surface': 'keyboard-help' })
    const palette = createNode('div', { 'data-shell-surface': 'command-palette' })
    const foreign = createNode('div')

    expect(shellSurfaceOwnsAction([help, palette], { type: 'keyboard-help' })).toBe(true)
    expect(shellSurfaceOwnsAction([help, foreign], { type: 'keyboard-help' })).toBe(false)
  })
})
