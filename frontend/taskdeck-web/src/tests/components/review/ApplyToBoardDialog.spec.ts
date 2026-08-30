import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import ApplyToBoardDialog from '../../../components/review/ApplyToBoardDialog.vue'
import type { Proposal } from '../../../types/automation'

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  return {
    id: 'p-1',
    sourceType: 'Queue',
    sourceReferenceId: null,
    boardId: 'b-1',
    requestedByUserId: 'u-1',
    status: 'Approved',
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

function mountDialog(proposal: Proposal | null, busy = false, revisionCount?: number | null) {
  const wrapper = mount(ApplyToBoardDialog, {
    props: { proposal, busy, revisionCount },
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

describe('ApplyToBoardDialog', () => {
  it('renders nothing when there is no proposal awaiting confirmation', () => {
    mountDialog(null)
    expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
  })

  it('carries the proposal summary so the user confirms what will be written', () => {
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
    const dialog = document.body.querySelector('[data-testid="apply-confirm-dialog"]')
    expect(dialog).not.toBeNull()
    expect(
      document.body.querySelector('[data-testid="apply-confirm-summary"]')?.textContent,
    ).toContain('Split “dark mode” into 3 cards')
  })

  it('falls back to the raw summary when there is no presentation summary', () => {
    mountDialog(makeProposal())
    expect(
      document.body.querySelector('[data-testid="apply-confirm-summary"]')?.textContent,
    ).toContain('Raw summary from the backend')
  })

  it('states that the board has not been written to yet', () => {
    mountDialog(makeProposal())
    const dialog = document.body.querySelector('[data-testid="apply-confirm-dialog"]')
    // GH-1942: this dialog now opens straight after approve, so it is THE
    // remaining step, not a confirmation of the "Confirm apply" click that used
    // to sit between the two. It must still say the board is untouched.
    expect(dialog?.textContent).toContain('Approved.')
    expect(dialog?.textContent).toContain('this is the step that applies it')
    expect(dialog?.textContent).toContain('Nothing has been written to the board yet')
    expect(dialog?.textContent).not.toContain('second and final step')
  })

  it('pluralizes the operation count it is about to apply', () => {
    mountDialog(
      makeProposal({
        operations: [
          { id: 'o-1' } as Proposal['operations'][number],
          { id: 'o-2' } as Proposal['operations'][number],
        ],
      }),
    )
    expect(
      document.body.querySelector('[data-testid="apply-confirm-operations"]')?.textContent,
    ).toContain('2 operations will be applied')
  })

  // --- #1830 round 2: the count must not lie on the revision-aware path -----

  it('does not claim "0 operations" when the proposal has no operations but has a revision', () => {
    // PaperReviewView.onApply deliberately lets this exact shape through (#1235:
    // a saved revision is materialized server-side, so an Approved proposal with
    // zero ORIGINAL operations still applies something). The dialog must say so.
    mountDialog(makeProposal({ operations: [] }), false, 1)

    expect(document.body.querySelector('[data-testid="apply-confirm-operations"]')).toBeNull()
    const revisionLine = document.body.querySelector('[data-testid="apply-confirm-revision"]')
    expect(revisionLine?.textContent).toContain('latest saved revision')
    expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')?.textContent).not.toContain(
      '0 operations',
    )
  })

  it('prefers the revision copy over a stale original count when both exist', () => {
    // Same reason: with a revision saved, the original operations are NOT what
    // the backend applies, so their count is not the truth either.
    mountDialog(
      makeProposal({
        operations: [
          { id: 'o-1' } as Proposal['operations'][number],
          { id: 'o-2' } as Proposal['operations'][number],
        ],
      }),
      false,
      2,
    )

    expect(document.body.querySelector('[data-testid="apply-confirm-operations"]')).toBeNull()
    expect(
      document.body.querySelector('[data-testid="apply-confirm-revision"]')?.textContent,
    ).toContain('not the original operations')
  })

  it('still shows the count when the surface knows there is no revision', () => {
    mountDialog(
      makeProposal({
        operations: [{ id: 'o-1' } as Proposal['operations'][number]],
      }),
      false,
      0,
    )

    expect(
      document.body.querySelector('[data-testid="apply-confirm-operations"]')?.textContent,
    ).toContain('1 operation will be applied')
    expect(document.body.querySelector('[data-testid="apply-confirm-revision"]')).toBeNull()
  })

  it('omits the number entirely when revision state is unknown and there are no operations', () => {
    // The Legacy surface does not track revisions, so it passes no count. An
    // empty operations list there could still be a revised proposal — claiming
    // "0 operations" would be the same lie.
    mountDialog(makeProposal({ operations: [] }))

    const line = document.body.querySelector('[data-testid="apply-confirm-operations"]')
    expect(line?.textContent).toContain('The approved contents of this proposal will be applied')
    expect(line?.textContent).not.toContain('0 operations')
  })

  it('emits confirm only from the accept button', async () => {
    const wrapper = mountDialog(makeProposal())
    const accept = document.body.querySelector(
      '[data-testid="apply-confirm-accept"]',
    ) as HTMLButtonElement
    accept.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('confirm')).toHaveLength(1)
    expect(wrapper.emitted('cancel')).toBeUndefined()
  })

  it('emits cancel from the cancel button', async () => {
    const wrapper = mountDialog(makeProposal())
    const cancel = document.body.querySelector(
      '[data-testid="apply-confirm-cancel"]',
    ) as HTMLButtonElement
    cancel.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(wrapper.emitted('confirm')).toBeUndefined()
  })

  it('disables the accept button while the execute call is in flight', () => {
    mountDialog(makeProposal(), true)
    const accept = document.body.querySelector(
      '[data-testid="apply-confirm-accept"]',
    ) as HTMLButtonElement
    expect(accept.disabled).toBe(true)
  })

  // --- GH-1942: the dialog opens by itself, so the backdrop is a trap --------
  //
  // Before GH-1942 this dialog only appeared after a deliberate click on it.
  // Now approve hands straight to it, so it materializes under a pointer that is
  // still moving toward the rail's primary button. A backdrop that closes on
  // click would let the user's habitual second click — the exact habit the
  // collapsed flow serves — discard the remaining step.
  describe('backdrop dismissal (GH-1942)', () => {
    function clickBackdrop() {
      const backdrop = document.body.querySelector('.td-dialog-backdrop') as HTMLElement | null
      expect(backdrop, 'expected the dialog backdrop to be rendered').not.toBeNull()
      // `@click.self` compares target to currentTarget, so click the backdrop
      // itself rather than anything nested inside it.
      backdrop!.click()
      return backdrop!
    }

    it('does not close when the backdrop is clicked while the dialog is open', async () => {
      const wrapper = mountDialog(makeProposal())
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()

      clickBackdrop()
      await wrapper.vm.$nextTick()

      expect(wrapper.emitted('cancel')).toBeUndefined()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()
    })

    it('keeps the backdrop inert whether or not the execute call is in flight', async () => {
      // The previous binding was `!busy`, i.e. dismissable for the whole time
      // the dialog is idle — which is all of the window the user clicks in.
      for (const busy of [false, true]) {
        const wrapper = mountDialog(makeProposal(), busy)
        clickBackdrop()
        await wrapper.vm.$nextTick()
        expect(wrapper.emitted('cancel')).toBeUndefined()
        wrapper.unmount()
        document.body.innerHTML = ''
      }
    })

    it('still offers the deliberate exits', async () => {
      const wrapper = mountDialog(makeProposal())
      const cancel = document.body.querySelector(
        '[data-testid="apply-confirm-cancel"]',
      ) as HTMLButtonElement
      cancel.click()
      await wrapper.vm.$nextTick()
      expect(wrapper.emitted('cancel')).toHaveLength(1)
    })

    it('closes on Escape', async () => {
      const wrapper = mountDialog(makeProposal())
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
      await wrapper.vm.$nextTick()
      expect(wrapper.emitted('cancel')).toHaveLength(1)
    })
  })

  /**
   * GH-1983 — reaching the accept from the keyboard.
   *
   * The dialog opens by itself the instant approve returns and TdDialog focuses
   * its container, so ⏎ did nothing here and a keyboard reviewer had to Tab
   * first — on the one dialog they did not open by hand. Container focus stays
   * (focusing the accept would let a HELD ⏎ auto-repeat straight into an
   * execute); the dialog binds ⏎ itself instead, guarded so the accept is
   * reachable from the keyboard but never by accident.
   */
  describe('Enter-to-confirm (GH-1983)', () => {
    function dialogEl(): HTMLElement {
      const el = document.body.querySelector('.td-dialog') as HTMLElement | null
      expect(el).not.toBeNull()
      return el!
    }

    function pressEnter(options: KeyboardEventInit = {}) {
      dialogEl().dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true, ...options }),
      )
    }

    /**
     * Mount and let the dialog settle. The binding is attached from a watcher on
     * the body element, so it lands one tick after the dialog's DOM does —
     * irrelevant to a human keypress, but a synchronous dispatch in the same
     * tick as `mount()` would miss it and prove nothing.
     */
    async function openDialog(busy = false) {
      const wrapper = mountDialog(makeProposal(), busy)
      await wrapper.vm.$nextTick()
      await wrapper.vm.$nextTick()
      return wrapper
    }

    it('confirms on a deliberate Enter while focus is still on the container', async () => {
      const wrapper = await openDialog()
      // The premise: focus is on the container, not the accept button.
      expect(document.activeElement).toBe(dialogEl())

      pressEnter()
      await wrapper.vm.$nextTick()

      expect(wrapper.emitted('confirm')).toHaveLength(1)
    })

    it('ignores an auto-repeat Enter — the approving keypress, still held', async () => {
      const wrapper = await openDialog()
      pressEnter({ repeat: true })
      pressEnter({ repeat: true })
      await wrapper.vm.$nextTick()

      expect(wrapper.emitted('confirm')).toBeUndefined()

      // A genuine press after the key is released still works.
      pressEnter()
      await wrapper.vm.$nextTick()
      expect(wrapper.emitted('confirm')).toHaveLength(1)
    })

    it('fires at most once per open', async () => {
      const wrapper = await openDialog()
      pressEnter()
      pressEnter()
      pressEnter()
      await wrapper.vm.$nextTick()

      expect(wrapper.emitted('confirm')).toHaveLength(1)
    })

    it('re-arms when the dialog opens again', async () => {
      const wrapper = await openDialog()
      pressEnter()
      await wrapper.vm.$nextTick()
      expect(wrapper.emitted('confirm')).toHaveLength(1)

      await wrapper.setProps({ proposal: null })
      await wrapper.vm.$nextTick()
      await wrapper.setProps({ proposal: makeProposal() })
      await wrapper.vm.$nextTick()

      pressEnter()
      await wrapper.vm.$nextTick()
      expect(wrapper.emitted('confirm')).toHaveLength(2)
    })

    it('leaves Enter on a focused control to that control', async () => {
      const wrapper = await openDialog()
      const cancel = document.body.querySelector(
        '[data-testid="apply-confirm-cancel"]',
      ) as HTMLButtonElement
      cancel.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
      )
      await wrapper.vm.$nextTick()

      // The browser turns that into a click on the button itself; what must NOT
      // happen is the dialog reading it as an accept.
      expect(wrapper.emitted('confirm')).toBeUndefined()
    })

    it('does not confirm while the execute is already in flight', async () => {
      const wrapper = await openDialog(true)
      pressEnter()
      await wrapper.vm.$nextTick()

      expect(wrapper.emitted('confirm')).toBeUndefined()
    })

    it('does not let the confirming Enter reach the surface behind it', async () => {
      // Confirming closes the dialog synchronously, so a propagating event
      // reaches the window with the keymap's dialog-open guard already dropped.
      // On the live surface the busy lock catches it anyway (see the component
      // comment); this pins the containment as the dialog's OWN property, so it
      // does not silently become load-bearing if that ordering changes.
      const wrapper = await openDialog()
      const behind = vi.fn()
      window.addEventListener('keydown', behind)
      try {
        pressEnter()
        await wrapper.vm.$nextTick()
        expect(wrapper.emitted('confirm')).toHaveLength(1)
        expect(behind).not.toHaveBeenCalled()
      } finally {
        window.removeEventListener('keydown', behind)
      }
    })

    it('binds nothing once the dialog is closed', async () => {
      const wrapper = await openDialog()
      await wrapper.setProps({ proposal: null })
      await wrapper.vm.$nextTick()

      expect(document.body.querySelector('.td-dialog')).toBeNull()
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
      await wrapper.vm.$nextTick()
      expect(wrapper.emitted('confirm')).toBeUndefined()
    })
  })
})
