import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import RejectProposalDialog from '../../../components/review/RejectProposalDialog.vue'
import type { Proposal } from '../../../types/automation'

/**
 * GH-1969 — the rejection reason moved from `window.prompt` into this dialog.
 *
 * The behaviour that must hold across the move is the OPTIONAL/REQUIRED split:
 * a Low/Medium proposal can be rejected with an empty box (and an all-whitespace
 * reason is stored as no reason), while High/Critical cannot be rejected without
 * one. The old prompt collected the empty string and then refused it with an
 * error toast; this refuses before the accept is offered.
 */

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  return {
    id: 'p-1',
    sourceType: 'Queue',
    sourceReferenceId: null,
    boardId: 'b-1',
    requestedByUserId: 'u-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Raw summary from the backend',
    diffPreview: null,
    validationIssues: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'corr-1',
    operations: [],
    approvedRevisionId: null,
    latestRevisionId: null,
    ...overrides,
  } as Proposal
}

// TdDialog teleports to <body>, so a wrapper left mounted would leak its DOM
// into the next test's body queries. Track and unmount every mount.
let mounted: Array<{ unmount: () => void }> = []

function mountDialog(proposal: Proposal | null, options: { busy?: boolean; requiresReason?: boolean } = {}) {
  const wrapper = mount(RejectProposalDialog, {
    props: { proposal, busy: options.busy ?? false, requiresReason: options.requiresReason ?? false },
    attachTo: document.body,
  })
  mounted.push(wrapper)
  return wrapper
}

function reasonField(): HTMLTextAreaElement {
  const field = document.body.querySelector(
    '[data-testid="reject-dialog-reason"]',
  ) as HTMLTextAreaElement | null
  expect(field).not.toBeNull()
  return field!
}

function acceptButton(): HTMLButtonElement {
  const accept = document.body.querySelector(
    '[data-testid="reject-dialog-accept"]',
  ) as HTMLButtonElement | null
  expect(accept).not.toBeNull()
  return accept!
}

async function type(wrapper: { vm: { $nextTick: () => Promise<unknown> } }, value: string) {
  const field = reasonField()
  field.value = value
  field.dispatchEvent(new Event('input'))
  await wrapper.vm.$nextTick()
}

afterEach(() => {
  mounted.forEach((wrapper) => wrapper.unmount())
  mounted = []
  document.body.innerHTML = ''
})

describe('RejectProposalDialog', () => {
  it('renders nothing when there is no proposal awaiting a reason', () => {
    mountDialog(null)
    expect(document.body.querySelector('[data-testid="reject-dialog"]')).toBeNull()
  })

  it('is an in-app dialog, not a browser one', async () => {
    const promptSpy = vi.spyOn(window, 'prompt')
    const wrapper = mountDialog(makeProposal())
    await type(wrapper, 'Already tracked elsewhere')
    acceptButton().click()

    // The whole point of GH-1969: this is `.td-dialog` markup the product can
    // style, translate and test, and nothing native is consulted.
    expect(document.body.querySelector('.td-dialog')).not.toBeNull()
    expect(promptSpy).not.toHaveBeenCalled()
    promptSpy.mockRestore()
  })

  it('carries the proposal summary so the reviewer rejects what they are looking at', () => {
    mountDialog(
      makeProposal({
        presentation: {
          plainSummary: 'Split “dark mode” into 3 cards',
          impactSummary: '',
          riskCue: '',
          sourceCue: '',
          operationHeadlines: [],
          affectedEntities: [],
        },
      }),
    )
    expect(
      document.body.querySelector('[data-testid="reject-dialog-summary"]')?.textContent,
    ).toContain('Split “dark mode” into 3 cards')
  })

  it('emits the typed reason, trimmed', async () => {
    const wrapper = mountDialog(makeProposal())
    await type(wrapper, '  already tracked on the On-Call rota  ')
    acceptButton().click()

    expect(wrapper.emitted('confirm')).toEqual([['already tracked on the On-Call rota']])
  })

  it('still rejects with an empty reason when the reason is optional', async () => {
    const wrapper = mountDialog(makeProposal())
    expect(acceptButton().disabled).toBe(false)
    acceptButton().click()

    expect(wrapper.emitted('confirm')).toEqual([['']])
  })

  it('reduces an all-whitespace reason to no reason', async () => {
    const wrapper = mountDialog(makeProposal())
    await type(wrapper, '    ')
    acceptButton().click()

    expect(wrapper.emitted('confirm')).toEqual([['']])
  })

  it('withholds the accept until a required reason is typed', async () => {
    const wrapper = mountDialog(makeProposal({ riskLevel: 'High' }), { requiresReason: true })
    expect(document.body.querySelector('[data-testid="reject-dialog-required-note"]')).not.toBeNull()
    expect(acceptButton().disabled).toBe(true)

    // Whitespace is not a reason.
    await type(wrapper, '   ')
    expect(acceptButton().disabled).toBe(true)
    acceptButton().click()
    expect(wrapper.emitted('confirm')).toBeUndefined()

    await type(wrapper, 'Duplicates the incident ticket')
    expect(acceptButton().disabled).toBe(false)
    acceptButton().click()
    expect(wrapper.emitted('confirm')).toEqual([['Duplicates the incident ticket']])
  })

  it('labels the field by whether the reason is required', async () => {
    const wrapper = mountDialog(makeProposal())
    expect(document.body.textContent).toContain('Reason (optional)')

    await wrapper.setProps({ requiresReason: true })
    expect(document.body.textContent).toContain('Reason (required)')
  })

  it('cancelling emits cancel and never confirms', async () => {
    const wrapper = mountDialog(makeProposal())
    await type(wrapper, 'typed but abandoned')
    ;(
      document.body.querySelector('[data-testid="reject-dialog-cancel"]') as HTMLButtonElement
    ).click()

    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(wrapper.emitted('confirm')).toBeUndefined()
  })

  it('Escape cancels through the shared escape stack', async () => {
    const wrapper = mountDialog(makeProposal())
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(wrapper.emitted('confirm')).toBeUndefined()
  })

  it('does not close on a backdrop click, so a typed reason cannot be lost to a stray click', async () => {
    const wrapper = mountDialog(makeProposal())
    await type(wrapper, 'carefully worded')
    ;(document.body.querySelector('.td-dialog-backdrop') as HTMLElement).click()
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('cancel')).toBeUndefined()
    expect(reasonField().value).toBe('carefully worded')
  })

  it('traps focus and restores it to the trigger on close', async () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()
    expect(document.activeElement).toBe(trigger)

    const wrapper = mountDialog(null)
    await wrapper.setProps({ proposal: makeProposal() })
    await wrapper.vm.$nextTick()

    // TdDialog focuses its container deliberately (see the component comment):
    // the reason field is the first focusable inside, one Tab away.
    const dialog = document.body.querySelector('.td-dialog') as HTMLElement
    expect(dialog.contains(document.activeElement)).toBe(true)
    const focusable = dialog.querySelectorAll(
      'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
    )
    expect(focusable[0]).toBe(reasonField())

    await wrapper.setProps({ proposal: null })
    await wrapper.vm.$nextTick()
    expect(document.activeElement).toBe(trigger)

    trigger.remove()
  })

  it('clears a typed reason between proposals', async () => {
    const wrapper = mountDialog(makeProposal({ id: 'first' }))
    await type(wrapper, 'wrong box')

    await wrapper.setProps({ proposal: null })
    await wrapper.vm.$nextTick()
    await wrapper.setProps({ proposal: makeProposal({ id: 'second' }) })
    await wrapper.vm.$nextTick()

    expect(reasonField().value).toBe('')
  })

  it('withholds the accept while a decision is in flight', () => {
    mountDialog(makeProposal(), { busy: true })
    expect(acceptButton().disabled).toBe(true)
  })
})
