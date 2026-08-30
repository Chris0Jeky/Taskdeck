import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import BatchExecuteDialog from '../../../components/review/BatchExecuteDialog.vue'
import type { BatchExecuteReceiptRow } from '../../../composables/useBatchExecuteProposals'

let mounted: Array<{ unmount: () => void }> = []

function mountDialog(props: {
  open?: boolean
  count?: number
  busy?: boolean
  receipts?: BatchExecuteReceiptRow[]
} = {}) {
  const wrapper = mount(BatchExecuteDialog, {
    props: {
      open: props.open ?? true,
      count: props.count ?? 2,
      busy: props.busy ?? false,
      receipts: props.receipts ?? [],
    },
    attachTo: document.body,
  })
  mounted.push(wrapper)
  return wrapper
}

afterEach(() => {
  mounted.forEach((wrapper) => wrapper.unmount())
  mounted = []
  document.body.innerHTML = ''
})

describe('BatchExecuteDialog', () => {
  it('renders only for an explicit confirmation and warns that partial success is possible', () => {
    mountDialog({ open: false })
    expect(document.body.querySelector('[data-testid="batch-execute-dialog"]')).toBeNull()

    mountDialog({ open: true, count: 3 })
    expect(document.body.querySelector('[data-testid="batch-execute-dialog"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="batch-execute-partial-warning"]')?.textContent)
      .toContain('nothing is rolled back')
    expect(document.body.textContent).toContain('already-approved')
  })

  it('separates cancel from the one apply confirmation', async () => {
    const wrapper = mountDialog({ count: 1 })

    ;(document.body.querySelector('[data-testid="batch-execute-cancel"]') as HTMLButtonElement).click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
    expect(wrapper.emitted('confirm')).toBeUndefined()

    ;(document.body.querySelector('[data-testid="batch-execute-confirm"]') as HTMLButtonElement).click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('confirm')).toHaveLength(1)
  })

  it('disables confirm while busy and when there is nothing to apply', () => {
    mountDialog({ busy: true })
    expect((document.body.querySelector('[data-testid="batch-execute-confirm"]') as HTMLButtonElement).disabled)
      .toBe(true)

    document.body.innerHTML = ''
    mountDialog({ count: 0 })
    expect((document.body.querySelector('[data-testid="batch-execute-confirm"]') as HTMLButtonElement).disabled)
      .toBe(true)
  })

  it('replaces the confirmation with a per-item receipt list once results arrive', () => {
    mountDialog({
      receipts: [
        { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 2, title: 'Card one' },
        { proposalId: 'p-2', outcome: 'Failed', errorCode: 'Conflict', errorMessage: 'Board moved on', appliedOperations: null, title: 'Card two' },
        { proposalId: 'p-3', outcome: 'Skipped', errorCode: null, errorMessage: null, appliedOperations: null, title: 'Card three' },
      ],
    })

    // The confirmation is gone; there is no second chance to re-post the same batch by accident.
    expect(document.body.querySelector('[data-testid="batch-execute-dialog"]')).toBeNull()
    expect(document.body.querySelector('[data-testid="batch-execute-confirm"]')).toBeNull()

    const receipts = document.body.querySelector('[data-testid="batch-execute-receipts"]')
    expect(receipts).not.toBeNull()
    expect(document.body.querySelector('[data-testid="batch-execute-receipt-p-1"]')?.textContent)
      .toContain('Card one')
    expect(document.body.querySelector('[data-testid="batch-execute-receipt-p-1"]')?.textContent)
      .toContain('Applied')
    expect(document.body.querySelector('[data-testid="batch-execute-receipt-p-3"]')?.textContent)
      .toContain('Skipped')
    // A failed item must say WHY, not just that it failed.
    expect(document.body.querySelector('[data-testid="batch-execute-reason-p-2"]')?.textContent)
      .toContain('Board moved on')
    expect(document.body.querySelector('[data-testid="batch-execute-receipt-summary"]')?.textContent)
      .toContain('Applied 1')
  })

  it('moves focus to Done and keeps Tab contained when confirmation receives receipts', async () => {
    const wrapper = mountDialog({ count: 2 })
    const confirm = document.body.querySelector('[data-testid="batch-execute-confirm"]') as HTMLButtonElement
    const preMountedSummary = document.body.querySelector(
      '[data-testid="batch-execute-receipt-summary"]',
    ) as HTMLElement
    const backgroundSentinel = document.createElement('button')
    backgroundSentinel.type = 'button'
    backgroundSentinel.dataset.testid = 'background-sentinel'
    document.body.append(backgroundSentinel)

    expect(preMountedSummary.textContent).toBe('')
    expect(preMountedSummary.getAttribute('role')).toBe('status')
    expect(preMountedSummary.getAttribute('aria-live')).toBe('polite')
    expect(preMountedSummary.getAttribute('aria-atomic')).toBe('true')
    expect(preMountedSummary.classList).toContain('batch-execute-receipt-summary--empty')
    expect(document.body.querySelectorAll('[role="status"][aria-live="polite"]').length).toBe(1)

    confirm.focus()
    confirm.click()
    await wrapper.setProps({
      receipts: [
        { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1, title: 'Card one' },
      ],
    })
    await wrapper.vm.$nextTick()

    const done = document.body.querySelector('[data-testid="batch-execute-done"]') as HTMLButtonElement
    const summary = document.body.querySelector('[data-testid="batch-execute-receipt-summary"]') as HTMLElement
    expect(document.activeElement).toBe(done)
    expect(summary).toBe(preMountedSummary)
    expect(summary.textContent).toContain('Applied 1')
    expect(summary.classList).not.toContain('batch-execute-receipt-summary--empty')
    expect(document.body.querySelectorAll('[role="status"][aria-live="polite"]').length).toBe(1)

    done.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }))
    expect(document.activeElement).toBe(done)
    expect(document.activeElement).not.toBe(backgroundSentinel)

    done.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }))
    expect(document.activeElement).toBe(done)
    expect(document.activeElement).not.toBe(backgroundSentinel)

    const summaryText = summary.textContent
    await wrapper.setProps({ busy: true, count: 99 })
    const stableSummary = document.body.querySelector(
      '[data-testid="batch-execute-receipt-summary"]',
    ) as HTMLElement
    expect(stableSummary).toBe(preMountedSummary)
    expect(stableSummary.textContent).toBe(summaryText)
    expect(document.body.querySelectorAll('[role="status"][aria-live="polite"]').length).toBe(1)
  })

  it('closes from the receipt view through its own done action', async () => {
    const wrapper = mountDialog({
      receipts: [
        { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1, title: 'Card one' },
      ],
    })

    ;(document.body.querySelector('[data-testid="batch-execute-done"]') as HTMLButtonElement).click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('falls back to a stated reason when the server reported none', () => {
    mountDialog({
      receipts: [
        { proposalId: 'p-1', outcome: 'Failed', errorCode: null, errorMessage: null, appliedOperations: null, title: 'Card one' },
      ],
    })
    expect(document.body.querySelector('[data-testid="batch-execute-reason-p-1"]')?.textContent)
      .toContain('No reason reported')
  })
})
