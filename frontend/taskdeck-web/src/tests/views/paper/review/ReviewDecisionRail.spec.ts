import { describe, expect, it } from 'vitest'
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
  }> = {},
) {
  return mount(ReviewDecisionRail, {
    props: {
      summary: '1 operation · explicit review · atomic apply',
      ...props,
    },
  })
}

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
})
