import { afterEach, describe, expect, it } from 'vitest'
import { h } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import ReviewDecisionRail from '../../../../views/paper/review/ReviewDecisionRail.vue'
// Vite's `?raw` rather than `node:fs`: the spec tree is type-checked with
// `types: ["vite/client", ...]` and deliberately WITHOUT node types
// (tsconfig.vitest.json), so a `readFileSync` here would fail `npm run typecheck`.
import railSource from '../../../../views/paper/review/ReviewDecisionRail.vue?raw'

/** The label face the user actually sees on the primary button. */
function activeApplyLabel(wrapper: VueWrapper): string {
  return wrapper.get('[data-testid="decision-apply-label"]').text()
}

/** The hidden face that only exists to reserve the button's width (GH-1942). */
function reservedApplyLabel(wrapper: VueWrapper): string {
  return wrapper.get('[data-testid="decision-apply-reserve"]').text()
}

function mountRail(
  props: Partial<{
    summary: string
    busy: boolean
    dismissable: boolean
    applyPhase: 'approve' | 'execute'
    editLock: 'off' | 'editing' | 'saving'
    applyOnly: boolean
    decisionDescriptionIds: string
  }> = {},
  options: { attachTo?: boolean } = {},
) {
  return mount(ReviewDecisionRail, {
    attachTo: options.attachTo ? document.body : undefined,
    props: {
      summary: '1 operation · explicit review · atomic apply',
      ...props,
    },
  })
}

/** The decision controls the rail renders in its actionable (non-filing) mode. */
const DECISION_TESTIDS = [
  'decision-reject',
  'decision-edit',
  'decision-defer',
  'decision-apply',
] as const

describe('ReviewDecisionRail', () => {
  it('renders the four decision actions in the default (actionable) state', () => {
    const wrapper = mountRail()
    expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-edit"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-defer"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-file-away"]').exists()).toBe(false)
    expect(wrapper.find('[role="toolbar"]').attributes('aria-label')).toBe('Decision actions')
    expect(wrapper.text()).toContain('DECISION')
  })

  it('emits each decision event when its button is clicked', async () => {
    const wrapper = mountRail()
    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await wrapper.find('[data-testid="decision-reject"]').trigger('click')
    await wrapper.find('[data-testid="decision-edit"]').trigger('click')
    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    expect(wrapper.emitted('apply')).toHaveLength(1)
    expect(wrapper.emitted('reject')).toHaveLength(1)
    expect(wrapper.emitted('request-edit')).toHaveLength(1)
    expect(wrapper.emitted('defer')).toHaveLength(1)
  })

  it('becomes a filing rail with a single "File away" button when dismissable', () => {
    const wrapper = mountRail({ dismissable: true })
    expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-edit"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-defer"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)

    const fileAway = wrapper.find('[data-testid="decision-file-away"]')
    expect(fileAway.exists()).toBe(true)
    // Accessible name must CONTAIN the visible label "File away" (WCAG 2.5.3
    // Label in Name) — it reads "File away proposal", not "Dismiss proposal".
    expect(fileAway.text()).toContain('File away')
    expect(fileAway.attributes('aria-label')).toBe('File away proposal')
    expect(wrapper.find('[role="toolbar"]').attributes('aria-label')).toBe('Filing actions')
    expect(wrapper.text()).toContain('SETTLED')
  })

  it('emits dismiss when File away is clicked', async () => {
    const wrapper = mountRail({ dismissable: true })
    await wrapper.find('[data-testid="decision-file-away"]').trigger('click')
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
  })

  it('disables the File away button while a network action is in flight', () => {
    const wrapper = mountRail({ dismissable: true, busy: true })
    expect(wrapper.find('[data-testid="decision-file-away"]').attributes('disabled')).toBeDefined()
  })

  // --- #1818: the two-phase apply must be legible on the rail itself ---

  describe('two-phase apply presentation', () => {
    it('phase 1 (pending) offers Approve and says the board is untouched', () => {
      const wrapper = mountRail()
      const apply = wrapper.find('[data-testid="decision-apply"]')
      // The VISIBLE face — the button also carries the other phase's label as a
      // hidden width reservation, so assert the face, not the whole button.
      expect(activeApplyLabel(wrapper)).toBe('Approve')
      expect(apply.attributes('data-apply-phase')).toBe('approve')
      expect(apply.attributes('aria-label')).toContain('step 1 of 2')
      expect(apply.attributes('aria-label')).toContain('does not change the board')

      const hint = wrapper.find('[data-testid="decision-step-hint"]')
      expect(hint.text()).toContain('Step 1 of 2')
      expect(hint.text()).toContain('does not change the board')
      expect(wrapper.find('[role="toolbar"]').attributes('data-apply-phase')).toBe('approve')
    })

    it('phase 2 (approved) offers Apply to board and names the board write', () => {
      const wrapper = mountRail({ applyPhase: 'execute' })
      const apply = wrapper.find('[data-testid="decision-apply"]')
      expect(activeApplyLabel(wrapper)).toBe('Apply to board')
      expect(apply.attributes('data-apply-phase')).toBe('execute')
      expect(apply.attributes('aria-label')).toContain('step 2 of 2')
      expect(apply.attributes('aria-label')).toContain('writes this change to the board')

      const hint = wrapper.find('[data-testid="decision-step-hint"]')
      expect(hint.text()).toContain('Step 2 of 2')
      expect(hint.text()).toContain('writes it to the board')
      expect(wrapper.find('[role="toolbar"]').attributes('data-apply-phase')).toBe('execute')
    })

    it('renders approved-but-not-executed distinctly from pending', () => {
      const pending = mountRail()
      const approved = mountRail({ applyPhase: 'execute' })

      // The whole point of #1818: the two states must not look identical.
      // Compare the visible faces — both buttons carry both labels in the DOM
      // (the width reservation, GH-1942), so whole-button text is now equal by
      // construction and would not discriminate.
      expect(activeApplyLabel(approved)).not.toBe(activeApplyLabel(pending))
      expect(approved.find('[role="toolbar"]').attributes('data-apply-phase')).not.toBe(
        pending.find('[role="toolbar"]').attributes('data-apply-phase'),
      )
      expect(approved.find('[data-testid="decision-step-hint"]').text()).not.toBe(
        pending.find('[data-testid="decision-step-hint"]').text(),
      )
    })

    it('a settled proposal has no apply phase and no step hint', () => {
      const wrapper = mountRail({ dismissable: true, applyPhase: 'execute' })
      expect(wrapper.find('[role="toolbar"]').attributes('data-apply-phase')).toBe('settled')
      expect(wrapper.find('[data-testid="decision-step-hint"]').exists()).toBe(false)
    })

    it('still emits a single apply event in the execute phase', async () => {
      const wrapper = mountRail({ applyPhase: 'execute' })
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      expect(wrapper.emitted('apply')).toHaveLength(1)
    })
  })

  // --- GH-1942 / GH-1943: the row's geometry must not be label-driven -------
  //
  // These defects are geometric, and neither happy-dom nor jsdom lays out text,
  // so a height assertion here would be a fake green (every element measures 0).
  // What IS honestly assertable is the MECHANISM: the DOM-level width
  // reservation, and the presence of the two CSS rules that make the row's
  // height independent of any single label. Pixel proof belongs to the
  // visual-regression baseline the issues point at (#1363) and is NOT claimed
  // by this file.
  describe('label-driven geometry', () => {
    const railStyle = railSource.slice(railSource.indexOf('<style'))

    it('reserves the widest phase label so the phase flip cannot resize the button', () => {
      const pending = mountRail()
      const approved = mountRail({ applyPhase: 'execute' })

      // Both phases render BOTH labels, so the button's intrinsic width is
      // max(labels) either way and cannot grow when the phase flips.
      const labels = (wrapper: VueWrapper) =>
        wrapper
          .findAll('.paper-review-decision__apply-face')
          .map((face) => face.text())
          .sort()
      expect(labels(pending)).toEqual(['Apply to board', 'Approve'])
      expect(labels(approved)).toEqual(labels(pending))

      // …and exactly one of them is the visible face in each phase.
      expect(activeApplyLabel(pending)).toBe('Approve')
      expect(reservedApplyLabel(pending)).toBe('Apply to board')
      expect(activeApplyLabel(approved)).toBe('Apply to board')
      expect(reservedApplyLabel(approved)).toBe('Approve')

      // The reserved face must not reach assistive tech or it would read the
      // button as "Approve Apply to board".
      const hidden = approved.get('[data-testid="decision-apply-reserve"]')
      expect(hidden.attributes('aria-hidden')).toBe('true')
      expect(hidden.attributes('data-active')).toBe('false')
      expect(
        approved.get('[data-testid="decision-apply-label"]').attributes('aria-hidden'),
      ).toBeUndefined()
    })

    it('declares the rules that keep every decision button the same height', () => {
      // GH-1943's measured cause: "Request edit" wrapped to two lines because
      // nothing pinned `white-space` on the button label, so that one button
      // was taller than Reject / Defer / Approve.
      expect(railStyle).toMatch(
        /\.paper-review-decision\s+:deep\(\.phlbtn-label\)\s*\{[^}]*white-space:\s*nowrap/,
      )
      // One shared min-height, applied to every button in the rail rather than
      // to any single one, so no label can drive the row.
      expect(railStyle).toMatch(/\.paper-review-decision\s+:deep\(\.pbtn\)\s*\{[^}]*min-height:/)
      // The reservation is width-only: the inactive face keeps its box (and so
      // its width) instead of being removed from flow with `display: none`.
      expect(railStyle).toMatch(
        /\.paper-review-decision__apply-face\[data-active='false'\]\s*\{[^}]*visibility:\s*hidden/,
      )
    })

    // GH-1943's own fix is what creates this hazard: `flex: none` + nowrap makes
    // every button unshrinkable, so a narrowing column has nowhere to put them
    // but horizontal overflow — sooner in it/es, whose labels ("Applica alla
    // bacheca", "Chiedi modifica") are longer than the en ones. The rail needs a
    // wrap path, and the asymmetry fix has to survive it.
    it('gives the rail a wrap path so the fixed-width buttons cannot overflow', () => {
      expect(railStyle).toMatch(/\.paper-review-decision\s*\{[^}]*flex-wrap:\s*wrap/)
      // The buttons wrap among THEMSELVES too, for columns too narrow for one
      // row of four.
      expect(railStyle).toMatch(/\.paper-review-decision__actions\s*\{[^}]*flex-wrap:\s*wrap/)
      // Load-bearing: flexbox breaks lines on items' hypothetical main sizes
      // BEFORE shrinking them, so a content-sized meta group would wrap the row
      // while there was still room to ellipsise the summary. `flex: 1 1 0`
      // keeps the actions group the only item that decides the break.
      expect(railStyle).toMatch(/\.paper-review-decision__meta\s*\{[^}]*flex:\s*1\s+1\s+0/)
      // The same rule is what absorbs the leftover width (the job the removed
      // `__spacer` element did), so the meta group must be able to shrink past
      // its content or the summary would stop ellipsising.
      expect(railStyle).toMatch(/\.paper-review-decision__meta\s*\{[^}]*min-width:\s*0/)
      // On the line it wraps onto, the actions group stays right-aligned.
      expect(railStyle).toMatch(/\.paper-review-decision__actions\s*\{[^}]*margin-left:\s*auto/)
    })

    it('keeps the equal-size button rules in force alongside the wrap', () => {
      // The wrap must not have been bought by letting buttons shrink again —
      // that is exactly the asymmetry GH-1943 fixed.
      expect(railStyle).toMatch(/\.paper-review-decision\s+:deep\(\.pbtn\)\s*\{[^}]*flex:\s*none/)
      expect(railStyle).toMatch(/\.paper-review-decision\s+:deep\(\.pbtn\)\s*\{[^}]*min-height:/)
      expect(railStyle).toMatch(
        /\.paper-review-decision\s+:deep\(\.phlbtn-label\)\s*\{[^}]*white-space:\s*nowrap/,
      )
    })

    it('puts every decision button inside the wrapping actions group', () => {
      // The CSS above only bites if the buttons are actually in that group.
      const wrapper = mountRail()
      const actions = wrapper.get('.paper-review-decision__actions')
      for (const testid of [
        'decision-reject',
        'decision-edit',
        'decision-defer',
        'decision-apply',
      ]) {
        expect(actions.find(`[data-testid="${testid}"]`).exists()).toBe(true)
      }
      // The meta half holds the text and no controls.
      const meta = wrapper.get('.paper-review-decision__meta')
      expect(meta.find('[data-testid="decision-step-hint"]').exists()).toBe(true)
      expect(meta.findAll('button')).toHaveLength(0)

      const settled = mountRail({ dismissable: true })
      expect(
        settled
          .get('.paper-review-decision__actions')
          .find('[data-testid="decision-file-away"]')
          .exists(),
      ).toBe(true)
    })

    it('routes every decision button through the shared label element the rules target', () => {
      const wrapper = mountRail()
      const buttons = ['decision-reject', 'decision-edit', 'decision-defer', 'decision-apply']
      for (const testid of buttons) {
        const button = wrapper.get(`[data-testid="${testid}"]`)
        expect(button.classes()).toContain('pbtn')
        expect(button.findAll('.phlbtn-label')).toHaveLength(1)
      }
      // The filing rail's single button is sized by the same rules.
      const settled = mountRail({ dismissable: true })
      const fileAway = settled.get('[data-testid="decision-file-away"]')
      expect(fileAway.classes()).toContain('pbtn')
      expect(fileAway.findAll('.phlbtn-label')).toHaveLength(1)
    })
  })

  /**
   * GH-1964 — the rail carries the lock's explanation and its exit.
   *
   * The rail is the surface that goes inert, so it is the surface that has to
   * say why. Before this the only cancel lived inside the composer that caused
   * the lock, at the bottom of a column the reviewer had not been scrolled to.
   */
  describe('edit lock (GH-1964)', () => {
    it('says nothing extra when no edit is in progress', () => {
      const wrapper = mountRail({ busy: true })
      expect(wrapper.find('[data-testid="decision-lock-note"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="decision-cancel-edit"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="decision-apply"]').attributes('aria-describedby')).toBeUndefined()
    })

    it('explains the lock and describes the disabled buttons while editing', () => {
      const wrapper = mountRail({ busy: true, editLock: 'editing' })
      const note = wrapper.get('[data-testid="decision-lock-note"]')
      expect(note.attributes('role')).toBe('status')
      expect(note.text()).toContain('save or cancel the edit')

      const noteId = note.attributes('id')
      expect(noteId).toBeTruthy()
      for (const testid of ['decision-reject', 'decision-edit', 'decision-defer', 'decision-apply']) {
        expect(wrapper.get(`[data-testid="${testid}"]`).attributes('aria-describedby')).toBe(noteId)
      }
    })

    it('offers an enabled cancel that emits cancel-edit', async () => {
      const wrapper = mountRail({ busy: true, editLock: 'editing' })
      const cancel = wrapper.get('[data-testid="decision-cancel-edit"]')
      // Not gated on `busy`: `busy` is exactly what this button exists to end.
      expect(cancel.attributes('disabled')).toBeUndefined()
      await cancel.trigger('click')
      expect(wrapper.emitted('cancel-edit')).toHaveLength(1)
    })

    it('keeps explaining while the revision saves, but has nothing left to cancel', () => {
      const wrapper = mountRail({ busy: true, editLock: 'saving' })
      expect(wrapper.get('[data-testid="decision-lock-note"]').text()).toContain('Saving your edit')
      expect(wrapper.get('[data-testid="decision-cancel-edit"]').attributes('disabled')).toBeDefined()
    })

    it('never shows the lock on a settled (filing) rail', () => {
      const wrapper = mountRail({ busy: true, dismissable: true, editLock: 'editing' })
      expect(wrapper.find('[data-testid="decision-lock-note"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="decision-cancel-edit"]').exists()).toBe(false)
    })

    it('gives two rails in the same app distinct note ids', () => {
      // Two rails never coexist on the Review surface today (one active
      // proposal, one rail), so this asserts the id is DERIVED rather than
      // hardcoded — a shared literal id would make `aria-describedby` ambiguous
      // the first time that changes. Both rails must live in ONE app: `useId`
      // counts per app instance, so two separate `mount()` calls would both
      // report the first id and prove nothing.
      const host = mount({
        render: () =>
          h('div', [
            h(ReviewDecisionRail, { summary: 'first', busy: true, editLock: 'editing' }),
            h(ReviewDecisionRail, { summary: 'second', busy: true, editLock: 'editing' }),
          ]),
      })
      const ids = host
        .findAll('[data-testid="decision-lock-note"]')
        .map((note) => note.attributes('id'))
      expect(ids).toHaveLength(2)
      expect(ids[0]).toBeTruthy()
      expect(ids[0]).not.toBe(ids[1])
    })
  })

  /**
   * #2461 — the post-revision refresh lock is drawn by the Review view, above
   * the rail. It used to describe only the non-focusable column wrapper, so a
   * reviewer inspecting a disabled decision button was told nothing about why
   * the whole row had gone inert.
   */
  describe('external decision lock description (#2461)', () => {
    const EXTERNAL_ID = 'external-refresh-lock'

    // Registered by each test, run even when an assertion throws, so a leaked
    // note id cannot resolve for a later test that expects it to be absent.
    const cleanups: Array<() => void> = []

    function drainCleanups(): void {
      const errors: unknown[] = []
      while (cleanups.length > 0) {
        const cleanup = cleanups.pop()
        if (!cleanup) continue
        try {
          cleanup()
        } catch (error: unknown) {
          errors.push(error)
        }
      }

      if (errors.length === 1) throw errors[0]
      if (errors.length > 1) {
        throw new AggregateError(errors, 'One or more cleanup callbacks failed')
      }
    }

    afterEach(() => {
      drainCleanups()
    })

    function renderExternalNote(): void {
      const note = document.createElement('p')
      note.id = EXTERNAL_ID
      note.textContent = 'Refreshing this proposal before your decision.'
      document.body.appendChild(note)
      cleanups.push(() => note.remove())
    }

    it('drains every cleanup when one callback throws', () => {
      const events: string[] = []
      const throwingCleanup = new Error('cleanup failed')
      cleanups.push(() => events.push('oldest'))
      cleanups.push(() => {
        events.push('throwing')
        throw throwingCleanup
      })
      cleanups.push(() => events.push('newest'))

      expect(drainCleanups).toThrow(throwingCleanup)
      expect(events).toEqual(['newest', 'throwing', 'oldest'])
      expect(cleanups).toHaveLength(0)
    })

    it('describes every disabled decision control with the external explanation', () => {
      renderExternalNote()
      const wrapper = mountRail(
        { busy: true, decisionDescriptionIds: EXTERNAL_ID },
        { attachTo: true },
      )
      cleanups.push(() => wrapper.unmount())

      for (const testid of DECISION_TESTIDS) {
        const button = wrapper.get(`[data-testid="${testid}"]`)
        expect(button.attributes('disabled')).toBeDefined()
        const ids = (button.attributes('aria-describedby') ?? '').split(' ').filter(Boolean)
        expect(ids).toEqual([EXTERNAL_ID])
        // Attached to the real document, so every referenced id must resolve.
        for (const id of ids) {
          expect(document.getElementById(id)).not.toBeNull()
        }
      }
    })

    it('carries the external explanation and its own edit-lock note together', () => {
      renderExternalNote()
      const wrapper = mountRail(
        { busy: true, editLock: 'editing', decisionDescriptionIds: EXTERNAL_ID },
        { attachTo: true },
      )
      cleanups.push(() => wrapper.unmount())

      const noteId = wrapper.get('[data-testid="decision-lock-note"]').attributes('id')
      expect(noteId).toBeTruthy()
      for (const testid of DECISION_TESTIDS) {
        const ids = (wrapper.get(`[data-testid="${testid}"]`).attributes('aria-describedby') ?? '')
          .split(' ')
          .filter(Boolean)
        expect(ids).toEqual([EXTERNAL_ID, noteId])
        for (const id of ids) {
          expect(document.getElementById(id)).not.toBeNull()
        }
      }
    })

    it('describes the only remaining control when a receipt leaves Apply alone', () => {
      renderExternalNote()
      const wrapper = mountRail(
        { busy: true, applyOnly: true, decisionDescriptionIds: EXTERNAL_ID },
        { attachTo: true },
      )
      cleanups.push(() => wrapper.unmount())

      expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(false)
      const apply = wrapper.get('[data-testid="decision-apply"]')
      expect(apply.attributes('disabled')).toBeDefined()
      expect(apply.attributes('aria-describedby')).toBe(EXTERNAL_ID)
    })

    it('adds no attribute when there is no explanation to point at', () => {
      // A blank string is the shape a caller produces when it joins an empty id
      // list. It must not become an aria-describedby that references nothing.
      const wrapper = mountRail({ busy: true, decisionDescriptionIds: '   ' })
      for (const testid of DECISION_TESTIDS) {
        expect(wrapper.get(`[data-testid="${testid}"]`).attributes('aria-describedby'))
          .toBeUndefined()
      }
    })
  })
})
