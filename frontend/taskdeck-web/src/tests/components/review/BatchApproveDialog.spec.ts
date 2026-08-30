import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import BatchApproveDialog from '../../../components/review/BatchApproveDialog.vue'

let mounted: Array<{ unmount: () => void }> = []

function mountDialog(open = true, count = 2, busy = false) {
  const wrapper = mount(BatchApproveDialog, {
    props: { open, count, busy },
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

describe('BatchApproveDialog', () => {
  it('renders only for an explicit confirmation and states Approved-not-Applied scope', () => {
    mountDialog(false)
    expect(document.body.querySelector('[data-testid="batch-approve-dialog"]')).toBeNull()

    mountDialog(true, 2)
    const dialog = document.body.querySelector('[data-testid="batch-approve-dialog"]')
    expect(dialog?.textContent).toContain('approve the whole set or none of it')
    expect(document.body.querySelector('[data-testid="batch-approve-not-applied"]')?.textContent)
      .toContain('approval only')
    expect(document.body.textContent).toContain('Nothing is applied to a board')
  })

  it('separates cancel from the one approve-only confirmation', async () => {
    const wrapper = mountDialog(true, 1)
    ;(document.body.querySelector('[data-testid="batch-approve-cancel"]') as HTMLButtonElement).click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(wrapper.emitted('confirm')).toBeUndefined()

    ;(document.body.querySelector('[data-testid="batch-approve-confirm"]') as HTMLButtonElement).click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('confirm')).toHaveLength(1)
  })

  it('locks both controls while the atomic request is in flight', () => {
    mountDialog(true, 3, true)
    expect(
      (document.body.querySelector('[data-testid="batch-approve-cancel"]') as HTMLButtonElement).disabled,
    ).toBe(true)
    expect(
      (document.body.querySelector('[data-testid="batch-approve-confirm"]') as HTMLButtonElement).disabled,
    ).toBe(true)
  })
})
