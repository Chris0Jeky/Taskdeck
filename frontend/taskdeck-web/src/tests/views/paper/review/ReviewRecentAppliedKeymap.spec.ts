import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { useReviewKeymap, type ReviewKeymapHandlers } from '../../../../composables/useReviewKeymap'
import ReviewRecentApplied, {
  type RecentlyAppliedRow,
} from '../../../../views/paper/review/ReviewRecentApplied.vue'

const rows: RecentlyAppliedRow[] = [
  { id: 'applied-1', serial: '#A001', title: 'First applied proposal', age: '5m' },
  { id: 'applied-2', serial: '#A002', title: 'Second applied proposal', age: '2m' },
]

function mountHost(
  handlers: ReviewKeymapHandlers,
  initialActiveId: string | null = 'applied-1',
) {
  const Host = defineComponent({
    name: 'ReviewRecentAppliedKeymapHost',
    setup() {
      const activeId = ref<string | null>(initialActiveId)
      useReviewKeymap(handlers)

      return () =>
        h(ReviewRecentApplied, {
          rows,
          activeId: activeId.value,
          onSelect: (id: string) => {
            activeId.value = id
          },
        })
    },
  })

  return mount(Host, { attachTo: document.body })
}

function dispatchKey(
  key: string,
  target: EventTarget,
  init: Pick<KeyboardEventInit, 'altKey' | 'ctrlKey' | 'metaKey' | 'shiftKey'> = {},
) {
  const event = new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
    ...init,
  })
  target.dispatchEvent(event)
  return event
}

describe('ReviewRecentApplied keymap boundary', () => {
  afterEach(() => {
    document.body.replaceChildren()
    vi.restoreAllMocks()
  })

  it('files the applied record after keyboard selection leaves focus on its active row', async () => {
    const onReject = vi.fn()
    const wrapper = mountHost({ onReject })
    const secondRow = wrapper.get('[data-proposal-id="applied-2"]')
    const button = secondRow.element as HTMLButtonElement

    button.focus()
    await secondRow.trigger('click')
    await nextTick()

    expect(document.activeElement).toBe(button)
    expect(secondRow.attributes('aria-pressed')).toBe('true')

    const event = dispatchKey('Backspace', button)

    expect(onReject).toHaveBeenCalledOnce()
    expect(event.defaultPrevented).toBe(true)
    wrapper.unmount()
  })

  it('keeps every non-filing review shortcut blocked on the active applied-row button', () => {
    const handlers = {
      onApply: vi.fn(),
      onRequestEdit: vi.fn(),
      onDefer: vi.fn(),
      onToggleProvenance: vi.fn(),
      onPreviewDiff: vi.fn(),
    }
    const wrapper = mountHost(handlers)
    const button = wrapper.get('[data-proposal-id="applied-1"]').element as HTMLButtonElement

    for (const key of ['Enter', 'e', 'd', 'p', ' ']) {
      dispatchKey(key, button)
    }

    expect(handlers.onApply).not.toHaveBeenCalled()
    expect(handlers.onRequestEdit).not.toHaveBeenCalled()
    expect(handlers.onDefer).not.toHaveBeenCalled()
    expect(handlers.onToggleProvenance).not.toHaveBeenCalled()
    expect(handlers.onPreviewDiff).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('does not file from an inactive applied-row button', () => {
    const onReject = vi.fn()
    const wrapper = mountHost({ onReject })
    const button = wrapper.get('[data-proposal-id="applied-2"]').element as HTMLButtonElement

    const event = dispatchKey('Backspace', button)

    expect(onReject).not.toHaveBeenCalled()
    expect(event.defaultPrevented).toBe(false)
    wrapper.unmount()
  })

  it('does not reinterpret modified Backspace from the active applied row', () => {
    const onReject = vi.fn()
    const wrapper = mountHost({ onReject })
    const button = wrapper.get('[data-proposal-id="applied-1"]').element as HTMLButtonElement

    const event = dispatchKey('Backspace', button, { ctrlKey: true })

    expect(onReject).not.toHaveBeenCalled()
    expect(event.defaultPrevented).toBe(false)
    wrapper.unmount()
  })
})
