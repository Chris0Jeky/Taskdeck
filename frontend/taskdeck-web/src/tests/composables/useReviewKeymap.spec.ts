import { describe, expect, it, vi } from 'vitest'
import { isEditableTarget, isInteractiveTarget, useReviewKeymap } from '../../composables/useReviewKeymap'

vi.mock('vue', () => ({
  onMounted: vi.fn((cb) => cb()),
  onBeforeUnmount: vi.fn(),
}))

function makeKeyEvent(overrides: Partial<KeyboardEvent> = {}): KeyboardEvent {
  return {
    key: '',
    code: '',
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
  describe('isEditableTarget', () => {
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

    it('returns true for element with data-review-keymap="ignore"', () => {
      const container = document.createElement('div')
      container.setAttribute('data-review-keymap', 'ignore')
      const child = document.createElement('span')
      container.appendChild(child)
      document.body.appendChild(container)
      expect(isEditableTarget(child)).toBe(true)
      document.body.removeChild(container)
    })

    it('returns false for plain div', () => {
      expect(isEditableTarget(document.createElement('div'))).toBe(false)
    })

    it('returns false for null', () => {
      expect(isEditableTarget(null)).toBe(false)
    })
  })

  describe('isInteractiveTarget', () => {
    it('returns true for button', () => {
      const btn = document.createElement('button')
      document.body.appendChild(btn)
      try {
        expect(isInteractiveTarget(btn)).toBe(true)
      } finally {
        document.body.removeChild(btn)
      }
    })

    it('returns true for anchor with href', () => {
      const a = document.createElement('a')
      a.href = '#'
      document.body.appendChild(a)
      try {
        expect(isInteractiveTarget(a)).toBe(true)
      } finally {
        document.body.removeChild(a)
      }
    })

    it('returns true for role="button"', () => {
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

    it('returns false for null', () => {
      expect(isInteractiveTarget(null)).toBe(false)
    })
  })

  describe('key dispatch', () => {
    it('Enter fires onApply', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply })
      const event = makeKeyEvent({ key: 'Enter' })
      handleKeyDown(event)
      expect(onApply).toHaveBeenCalledOnce()
      expect(event.preventDefault).toHaveBeenCalled()
    })

    it('Backspace fires onReject', () => {
      const onReject = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onReject })
      handleKeyDown(makeKeyEvent({ key: 'Backspace' }))
      expect(onReject).toHaveBeenCalledOnce()
    })

    it('Space fires onPreviewDiff', () => {
      const onPreviewDiff = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onPreviewDiff })
      handleKeyDown(makeKeyEvent({ key: ' ' }))
      expect(onPreviewDiff).toHaveBeenCalledOnce()
    })

    it('Spacebar (legacy) fires onPreviewDiff', () => {
      const onPreviewDiff = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onPreviewDiff })
      handleKeyDown(makeKeyEvent({ key: 'Spacebar' }))
      expect(onPreviewDiff).toHaveBeenCalledOnce()
    })

    it('code=Space fires onPreviewDiff', () => {
      const onPreviewDiff = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onPreviewDiff })
      handleKeyDown(makeKeyEvent({ key: 'Unidentified', code: 'Space' }))
      expect(onPreviewDiff).toHaveBeenCalledOnce()
    })

    it('e fires onRequestEdit', () => {
      const onRequestEdit = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onRequestEdit })
      handleKeyDown(makeKeyEvent({ key: 'e' }))
      expect(onRequestEdit).toHaveBeenCalledOnce()
    })

    it('E (uppercase) fires onRequestEdit', () => {
      const onRequestEdit = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onRequestEdit })
      handleKeyDown(makeKeyEvent({ key: 'E' }))
      expect(onRequestEdit).toHaveBeenCalledOnce()
    })

    it('d fires onDefer', () => {
      const onDefer = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onDefer })
      handleKeyDown(makeKeyEvent({ key: 'd' }))
      expect(onDefer).toHaveBeenCalledOnce()
    })

    it('p fires onToggleProvenance', () => {
      const onToggleProvenance = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onToggleProvenance })
      handleKeyDown(makeKeyEvent({ key: 'p' }))
      expect(onToggleProvenance).toHaveBeenCalledOnce()
    })

    it('unrecognized key does nothing', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply })
      const event = makeKeyEvent({ key: 'x' })
      handleKeyDown(event)
      expect(onApply).not.toHaveBeenCalled()
      expect(event.preventDefault).not.toHaveBeenCalled()
    })
  })

  describe('guards', () => {
    it('does not fire when enabled returns false', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply }, { enabled: () => false })
      handleKeyDown(makeKeyEvent({ key: 'Enter' }))
      expect(onApply).not.toHaveBeenCalled()
    })

    it('does not fire when isComposing', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply })
      handleKeyDown(makeKeyEvent({ key: 'Enter', isComposing: true }))
      expect(onApply).not.toHaveBeenCalled()
    })

    it('does not fire when target is editable', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply })
      handleKeyDown(makeKeyEvent({ key: 'Enter', target: document.createElement('textarea') }))
      expect(onApply).not.toHaveBeenCalled()
    })

    it('does not fire when target is interactive', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply })
      const btn = document.createElement('button')
      document.body.appendChild(btn)
      handleKeyDown(makeKeyEvent({ key: 'Enter', target: btn }))
      expect(onApply).not.toHaveBeenCalled()
      document.body.removeChild(btn)
    })

    it('does not fire with meta key', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply })
      handleKeyDown(makeKeyEvent({ key: 'Enter', metaKey: true }))
      expect(onApply).not.toHaveBeenCalled()
    })

    it('does not fire with ctrl key', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply })
      handleKeyDown(makeKeyEvent({ key: 'Enter', ctrlKey: true }))
      expect(onApply).not.toHaveBeenCalled()
    })

    it('does not fire with alt key', () => {
      const onApply = vi.fn()
      const { handleKeyDown } = useReviewKeymap({ onApply })
      handleKeyDown(makeKeyEvent({ key: 'Enter', altKey: true }))
      expect(onApply).not.toHaveBeenCalled()
    })

    it('does not fire when handler is undefined', () => {
      const { handleKeyDown } = useReviewKeymap({})
      const event = makeKeyEvent({ key: 'Enter' })
      handleKeyDown(event)
      expect(event.preventDefault).not.toHaveBeenCalled()
    })
  })
})
