import { describe, expect, it } from 'vitest'
import paperHomeSource from '../../views/paper/PaperHomeView.vue?raw'
import paperInboxSource from '../../views/paper/PaperInboxView.vue?raw'
import { shouldHandlePaperCaptureShortcut } from '../../utils/paperCaptureShortcut'

function shortcutResultFrom(
  target: Element,
  init: KeyboardEventInit = {},
): boolean {
  let result: boolean | undefined
  target.addEventListener(
    'keydown',
    (event) => {
      result = shouldHandlePaperCaptureShortcut(event as KeyboardEvent)
    },
    { once: true },
  )
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key: ';',
    ctrlKey: true,
    bubbles: true,
    cancelable: true,
    ...init,
  }))
  expect(result).toBeDefined()
  return result as boolean
}

describe('shouldHandlePaperCaptureShortcut', () => {
  it('accepts the existing Cmd/Ctrl+; chord from ordinary page content', () => {
    expect(shortcutResultFrom(document.createElement('button'))).toBe(true)
    expect(shortcutResultFrom(document.createElement('div'), { ctrlKey: false, metaKey: true })).toBe(true)
  })

  it.each(['input', 'textarea', 'select'] as const)(
    'rejects the chord when it starts in a %s',
    (tagName) => {
      expect(shortcutResultFrom(document.createElement(tagName))).toBe(false)
    },
  )

  it('rejects the chord from a descendant of a contenteditable surface', () => {
    const editor = document.createElement('div')
    editor.contentEditable = 'true'
    const child = document.createElement('span')
    editor.append(child)

    expect(shortcutResultFrom(child)).toBe(false)
  })

  it('rejects composition, the wrong key, and a missing platform modifier', () => {
    expect(shortcutResultFrom(document.createElement('button'), { isComposing: true })).toBe(false)
    expect(shortcutResultFrom(document.createElement('button'), { key: 'x' })).toBe(false)
    expect(shortcutResultFrom(document.createElement('button'), { ctrlKey: false })).toBe(false)
  })
})

describe('Paper capture shortcut wiring', () => {
  const helperImport =
    "import { shouldHandlePaperCaptureShortcut } from '../../utils/paperCaptureShortcut'"
  const handlerGuard = 'if (!shouldHandlePaperCaptureShortcut(event)) return'

  it('routes Paper Home through the text-entry-safe shortcut predicate', () => {
    expect(paperHomeSource).toContain(helperImport)
    expect(paperHomeSource).toContain(handlerGuard)
  })

  it('routes Paper Inbox through the text-entry-safe shortcut predicate', () => {
    expect(paperInboxSource).toContain(helperImport)
    expect(paperInboxSource).toContain(handlerGuard)
  })
})
