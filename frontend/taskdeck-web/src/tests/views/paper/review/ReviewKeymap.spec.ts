import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import {
  isEditableTarget,
  useReviewKeymap,
  type ReviewKeymapHandlers,
} from '../../../../composables/useReviewKeymap'

function makeHost(handlers: ReviewKeymapHandlers, enabled = () => true) {
  return defineComponent({
    name: 'KeymapHost',
    setup() {
      const showInput = ref(true)
      useReviewKeymap(handlers, { enabled })
      return { showInput }
    },
    render() {
      return h('div', [
        h('input', { 'data-testid': 'reason-input', type: 'text' }),
        h('textarea', { 'data-testid': 'edit-composer' }),
        h('div', { 'data-testid': 'editable', contenteditable: 'true' }),
      ])
    },
  })
}

function dispatchKey(key: string, target?: EventTarget) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
  ;(target ?? window).dispatchEvent(event)
  return event
}

describe('useReviewKeymap', () => {
  let onApply: ReturnType<typeof vi.fn>
  let onReject: ReturnType<typeof vi.fn>
  let onRequestEdit: ReturnType<typeof vi.fn>
  let onDefer: ReturnType<typeof vi.fn>
  let onToggleProvenance: ReturnType<typeof vi.fn>
  let onPreviewDiff: ReturnType<typeof vi.fn>

  beforeEach(() => {
    onApply = vi.fn()
    onReject = vi.fn()
    onRequestEdit = vi.fn()
    onDefer = vi.fn()
    onToggleProvenance = vi.fn()
    onPreviewDiff = vi.fn()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('fires apply when ⏎ pressed outside an input', () => {
    const Host = makeHost({ onApply, onReject, onRequestEdit, onDefer, onToggleProvenance, onPreviewDiff })
    const wrapper = mount(Host, { attachTo: document.body })
    dispatchKey('Enter')
    expect(onApply).toHaveBeenCalledOnce()
    wrapper.unmount()
  })

  it('does NOT fire apply when ⏎ pressed inside a text input', async () => {
    const Host = makeHost({ onApply, onReject, onRequestEdit, onDefer, onToggleProvenance, onPreviewDiff })
    const wrapper = mount(Host, { attachTo: document.body })

    const input = wrapper.get('[data-testid="reason-input"]').element as HTMLInputElement
    input.focus()
    dispatchKey('Enter', input)

    expect(onApply).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('does NOT fire when ⏎ pressed inside a textarea (edit composer)', () => {
    const Host = makeHost({ onApply, onReject, onRequestEdit, onDefer, onToggleProvenance, onPreviewDiff })
    const wrapper = mount(Host, { attachTo: document.body })

    const ta = wrapper.get('[data-testid="edit-composer"]').element as HTMLTextAreaElement
    ta.focus()
    dispatchKey('Enter', ta)

    expect(onApply).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('does NOT fire when key pressed inside contenteditable', () => {
    const Host = makeHost({ onApply, onReject, onRequestEdit, onDefer, onToggleProvenance, onPreviewDiff })
    const wrapper = mount(Host, { attachTo: document.body })

    const ce = wrapper.get('[data-testid="editable"]').element as HTMLElement
    dispatchKey('Enter', ce)
    expect(onApply).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('maps ⌫ → reject, E → request-edit, D → defer, P → provenance, Space → preview', () => {
    const Host = makeHost({ onApply, onReject, onRequestEdit, onDefer, onToggleProvenance, onPreviewDiff })
    const wrapper = mount(Host, { attachTo: document.body })

    dispatchKey('Backspace')
    dispatchKey('e')
    dispatchKey('d')
    dispatchKey('p')
    dispatchKey(' ')

    expect(onReject).toHaveBeenCalledOnce()
    expect(onRequestEdit).toHaveBeenCalledOnce()
    expect(onDefer).toHaveBeenCalledOnce()
    expect(onToggleProvenance).toHaveBeenCalledOnce()
    expect(onPreviewDiff).toHaveBeenCalledOnce()
    wrapper.unmount()
  })

  it('skips dispatch when modifier keys are held', () => {
    const Host = makeHost({ onApply, onReject, onRequestEdit, onDefer, onToggleProvenance, onPreviewDiff })
    const wrapper = mount(Host, { attachTo: document.body })

    const event = new KeyboardEvent('keydown', {
      key: 'Enter',
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    })
    window.dispatchEvent(event)
    expect(onApply).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('skips dispatch when enabled() returns false', () => {
    const Host = makeHost(
      { onApply, onReject, onRequestEdit, onDefer, onToggleProvenance, onPreviewDiff },
      () => false,
    )
    const wrapper = mount(Host, { attachTo: document.body })
    dispatchKey('Enter')
    expect(onApply).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('isEditableTarget detects inputs, textareas, contenteditable, and ignore-marked containers', () => {
    const input = document.createElement('input')
    input.type = 'text'
    expect(isEditableTarget(input)).toBe(true)

    const submit = document.createElement('input')
    submit.type = 'submit'
    expect(isEditableTarget(submit)).toBe(false)

    const ta = document.createElement('textarea')
    expect(isEditableTarget(ta)).toBe(true)

    const div = document.createElement('div')
    div.setAttribute('data-review-keymap', 'ignore')
    document.body.appendChild(div)
    const inner = document.createElement('span')
    div.appendChild(inner)
    expect(isEditableTarget(inner)).toBe(true)
    document.body.removeChild(div)

    expect(isEditableTarget(null)).toBe(false)
  })
})
