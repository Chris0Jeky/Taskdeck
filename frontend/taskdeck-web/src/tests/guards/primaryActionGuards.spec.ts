import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperTriageTable from '../../views/paper/inbox/PaperTriageTable.vue'
import PaperCaptureComposer from '../../views/paper/inbox/PaperCaptureComposer.vue'
import type { CaptureItemSummary } from '../../types/capture'
import { expectGuardedPrimaryAction } from '../utils/guardedPrimaryAction'

/**
 * GH-1949 AC3 registry — "a primary action whose preconditions are unmet must
 * be `disabled` or must render validation, never enabled-and-silent".
 *
 * WHY A REGISTRY AND NOT A SCAN. The sibling guard
 * (`deadAnchors.spec.ts`) scans every SFC, because its questions are decidable
 * from source text. AC3's are not: "precondition unmet" is component state and
 * "the user was told" is post-activation render. So this file is an explicit,
 * opt-in list. It proves the contract for the actions named below and makes NO
 * claim about any action it does not list — adding one is the whole point.
 *
 * HOW TO REGISTER A PRIMARY ACTION.
 *  1. Add an `it(...)` below, named for the control and its unmet precondition.
 *  2. Mount the owning component with that precondition already unmet — this
 *     file sets the state up; the helper never does.
 *  3. Find the trigger (prefer a stable `data-action` hook over a text match).
 *  4. Call `expectGuardedPrimaryAction(wrapper, trigger, { unmetPreconditions })`,
 *     passing `toastSpy` / `validationSelectors` / `errorEvents` when the
 *     feedback for that control leaves the wrapper or uses a bespoke selector.
 * The helper passes on EITHER branch — disabled, or enabled-with-feedback — so
 * registering a control does not dictate which of the two designs it uses.
 *
 * NOT COVERED: whether the validation text is correct, whether a disabled
 * reason is announced to assistive tech, and every primary action absent from
 * this list. Route-walking runtime coverage (AC4) remains open on GH-1949.
 */

type MockBoard = { id: string; name: string; canWrite?: boolean }

const defaultBoards = (): MockBoard[] => [
  { id: 'board-alpha', name: 'Alpha' },
  { id: 'board-beta', name: 'Beta' },
]

const mockBoardStore = reactive({
  boards: defaultBoards() as MockBoard[],
  fetchBoards: vi.fn<() => Promise<void>>(),
})

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

const mockCaptureStore = {
  fetchDetail: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  updateSuggestion: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
}

vi.mock('../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

function boardlessItems(): CaptureItemSummary[] {
  return [
    {
      id: 'capture-1',
      userId: 'user-1',
      boardId: null,
      status: 'New',
      source: 'Typed',
      textExcerpt: 'First excerpt',
      createdAt: new Date('2026-04-25T09:42:00Z').toISOString(),
      processedAt: null,
    },
  ] as CaptureItemSummary[]
}

describe('primary action guards (GH-1949 AC3)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardStore.boards = defaultBoards()
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
    mockCaptureStore.fetchDetail.mockResolvedValue({})
  })

  /**
   * The detector must be able to fail, or every registration below is a no-op.
   * This synthetic control is the exact #1944 shape before its fix: enabled,
   * and silent on click.
   */
  it('fails an enabled-and-silent primary action', async () => {
    const Silent = {
      template: '<div><button data-action="go">Do it</button></div>',
    }
    const wrapper = mount(Silent)

    await expect(
      expectGuardedPrimaryAction(wrapper, wrapper.get('button[data-action="go"]'), {
        unmetPreconditions: 'synthetic control with nothing wired up',
      }),
    ).rejects.toThrow(/Enabled-and-silent primary action/)
  })

  it('accepts a control that renders validation instead of disabling', async () => {
    const Validating = {
      data: () => ({ shown: false }),
      template:
        '<div><button data-action="go" @click="shown = true">Do it</button>' +
        '<p v-if="shown" role="alert">Pick a board first</p></div>',
    }
    const wrapper = mount(Validating)

    await expectGuardedPrimaryAction(wrapper, wrapper.get('button[data-action="go"]'), {
      unmetPreconditions: 'synthetic control that explains itself',
    })
  })

  // ---- Registered primary actions ----

  it('Inbox "Accept on board" is guarded with no board selected (#1944)', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: boardlessItems() } })

    // Reveal the inline board picker without choosing a board: the precondition.
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    const confirm = wrapper.get('button[data-action="accept-on-board"]')

    await expectGuardedPrimaryAction(wrapper, confirm, {
      unmetPreconditions: 'board picker open, no board chosen',
    })
    // The original defect was a silent no-op, so also pin the absence of an emit.
    expect(wrapper.emitted('accept')).toBeUndefined()
  })

  it('Capture composer submit is guarded with an empty body', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const textarea = wrapper.get('textarea')
    await textarea.setValue('   ')

    const submit = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Capture'))
    expect(submit, 'composer submit button not found').toBeDefined()

    await expectGuardedPrimaryAction(wrapper, submit!, {
      unmetPreconditions: 'composer body is whitespace only',
    })
    expect(wrapper.emitted('submit')).toBeUndefined()
  })
})
