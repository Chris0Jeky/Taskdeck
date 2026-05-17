import { describe, expect, it, vi } from 'vitest'
import {
  isEditableTarget,
  isInteractiveTarget,
  useReviewKeymap,
  type ReviewKeymapHandlers,
} from '../../composables/useReviewKeymap'

vi.mock('vue', () => ({
  onMounted: (fn: () => void) => fn(),
  onBeforeUnmount: vi.fn(),
}))

function makeKeyEvent(
  key: string,
  overrides: Partial<KeyboardEvent> = {},
): KeyboardEvent {
  return {
    key,
    code: key === ' ' ? 'Space' : `Key${key.toUpperCase()}`,
    isComposing: false,
    metaKey: false,
    ctrlKey: false,
    altKey: false,
    target: document.createElement('div'),
    preventDefault: vi.fn(),
    stopPropagation: vi.fn(),
    ...overrides,
  } as unknown as KeyboardEvent
}

describe('useReviewKeymap', () => {
  describe('key bindings', () => {
    it('Enter triggers onApply', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('Enter'))
      expect(handlers.onApply).toHaveBeenCalledOnce()
    })

    it('Backspace triggers onReject', () => {
      const handlers: ReviewKeymapHandlers = { onReject: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('Backspace'))
      expect(handlers.onReject).toHaveBeenCalledOnce()
    })

    it('Space triggers onPreviewDiff', () => {
      const handlers: ReviewKeymapHandlers = { onPreviewDiff: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent(' '))
      expect(handlers.onPreviewDiff).toHaveBeenCalledOnce()
    })

    it('Spacebar legacy key value triggers onPreviewDiff', () => {
      const handlers: ReviewKeymapHandlers = { onPreviewDiff: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('Spacebar'))
      expect(handlers.onPreviewDiff).toHaveBeenCalledOnce()
    })

    it('event.code Space fallback triggers onPreviewDiff', () => {
      const handlers: ReviewKeymapHandlers = { onPreviewDiff: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('Unidentified', { code: 'Space' }))
      expect(handlers.onPreviewDiff).toHaveBeenCalledOnce()
    })

    it('E triggers onRequestEdit', () => {
      const handlers: ReviewKeymapHandlers = { onRequestEdit: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('e'))
      expect(handlers.onRequestEdit).toHaveBeenCalledOnce()
    })

    it('uppercase E triggers onRequestEdit', () => {
      const handlers: ReviewKeymapHandlers = { onRequestEdit: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('E'))
      expect(handlers.onRequestEdit).toHaveBeenCalledOnce()
    })

    it('D triggers onDefer', () => {
      const handlers: ReviewKeymapHandlers = { onDefer: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('d'))
      expect(handlers.onDefer).toHaveBeenCalledOnce()
    })

    it('P triggers onToggleProvenance', () => {
      const handlers: ReviewKeymapHandlers = { onToggleProvenance: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('p'))
      expect(handlers.onToggleProvenance).toHaveBeenCalledOnce()
    })

    it('unbound keys are ignored', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      const event = makeKeyEvent('x')
      handleKeyDown(event)
      expect(handlers.onApply).not.toHaveBeenCalled()
      expect(event.preventDefault).not.toHaveBeenCalled()
    })
  })

  describe('guards', () => {
    it('does not fire when enabled returns false', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers, { enabled: () => false })
      handleKeyDown(makeKeyEvent('Enter'))
      expect(handlers.onApply).not.toHaveBeenCalled()
    })

    it('does not fire when isComposing is true', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('Enter', { isComposing: true }))
      expect(handlers.onApply).not.toHaveBeenCalled()
    })

    it('does not fire when target is a text input', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      const input = document.createElement('input')
      input.type = 'text'
      handleKeyDown(makeKeyEvent('Enter', { target: input } as unknown as Partial<KeyboardEvent>))
      expect(handlers.onApply).not.toHaveBeenCalled()
    })

    it('does not fire when target is a textarea', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      handleKeyDown(makeKeyEvent('Enter', { target: document.createElement('textarea') } as unknown as Partial<KeyboardEvent>))
      expect(handlers.onApply).not.toHaveBeenCalled()
    })

    it('does not fire when target is interactive', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      const button = document.createElement('button')
      document.body.appendChild(button)
      try {
        handleKeyDown(makeKeyEvent('Enter', { target: button } as unknown as Partial<KeyboardEvent>))
        expect(handlers.onApply).not.toHaveBeenCalled()
      } finally {
        document.body.removeChild(button)
      }
    })

    it('does not fire with modifier keys', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)

      handleKeyDown(makeKeyEvent('Enter', { metaKey: true }))
      handleKeyDown(makeKeyEvent('Enter', { ctrlKey: true }))
      handleKeyDown(makeKeyEvent('Enter', { altKey: true }))

      expect(handlers.onApply).not.toHaveBeenCalled()
    })

    it('does not fire when handler is not provided for the key', () => {
      const handlers: ReviewKeymapHandlers = {}
      const { handleKeyDown } = useReviewKeymap(handlers)
      const event = makeKeyEvent('Enter')
      handleKeyDown(event)
      expect(event.preventDefault).not.toHaveBeenCalled()
    })
  })

  describe('preventDefault', () => {
    it('calls preventDefault and stopPropagation when handler fires', () => {
      const handlers: ReviewKeymapHandlers = { onApply: vi.fn() }
      const { handleKeyDown } = useReviewKeymap(handlers)
      const event = makeKeyEvent('Enter')
      handleKeyDown(event)
      expect(event.preventDefault).toHaveBeenCalledOnce()
      expect(event.stopPropagation).toHaveBeenCalledOnce()
    })
  })
})

describe('isEditableTarget', () => {
  it('returns false for null', () => {
    expect(isEditableTarget(null)).toBe(false)
  })

  it('returns true for textarea', () => {
    expect(isEditableTarget(document.createElement('textarea'))).toBe(true)
  })

  it('returns true for select', () => {
    expect(isEditableTarget(document.createElement('select'))).toBe(true)
  })

  it('returns true for text input', () => {
    const input = document.createElement('input')
    input.type = 'text'
    expect(isEditableTarget(input)).toBe(true)
  })

  it('returns true for email input', () => {
    const input = document.createElement('input')
    input.type = 'email'
    expect(isEditableTarget(input)).toBe(true)
  })

  it('returns false for checkbox input', () => {
    const input = document.createElement('input')
    input.type = 'checkbox'
    expect(isEditableTarget(input)).toBe(false)
  })

  it('returns true for contenteditable element', () => {
    const div = document.createElement('div')
    div.contentEditable = 'true'
    expect(isEditableTarget(div)).toBe(true)
  })

  it('returns true for element with data-review-keymap ignore', () => {
    const wrapper = document.createElement('div')
    wrapper.setAttribute('data-review-keymap', 'ignore')
    const child = document.createElement('span')
    wrapper.appendChild(child)
    document.body.appendChild(wrapper)
    try {
      expect(isEditableTarget(child)).toBe(true)
    } finally {
      document.body.removeChild(wrapper)
    }
  })

  it('returns false for plain div', () => {
    expect(isEditableTarget(document.createElement('div'))).toBe(false)
  })
})

describe('isInteractiveTarget', () => {
  it('returns false for null', () => {
    expect(isInteractiveTarget(null)).toBe(false)
  })

  it('returns true for button', () => {
    const button = document.createElement('button')
    document.body.appendChild(button)
    try {
      expect(isInteractiveTarget(button)).toBe(true)
    } finally {
      document.body.removeChild(button)
    }
  })

  it('returns true for anchor with href', () => {
    const anchor = document.createElement('a')
    anchor.href = '#'
    document.body.appendChild(anchor)
    try {
      expect(isInteractiveTarget(anchor)).toBe(true)
    } finally {
      document.body.removeChild(anchor)
    }
  })

  it('returns true for role button', () => {
    const div = document.createElement('div')
    div.setAttribute('role', 'button')
    document.body.appendChild(div)
    try {
      expect(isInteractiveTarget(div)).toBe(true)
    } finally {
      document.body.removeChild(div)
    }
  })

  it('returns false for plain div', () => {
    const div = document.createElement('div')
    document.body.appendChild(div)
    try {
      expect(isInteractiveTarget(div)).toBe(false)
    } finally {
      document.body.removeChild(div)
    }
  })
})
