import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
import PaperTriageTable from '../../views/paper/inbox/PaperTriageTable.vue'
import PaperCaptureComposer from '../../views/paper/inbox/PaperCaptureComposer.vue'
import PaperCaptureNib from '../../views/paper/inbox/PaperCaptureNib.vue'
import PaperTriageRowEdit from '../../views/paper/inbox/PaperTriageRowEdit.vue'
import type { CaptureItem, CaptureItemSummary } from '../../types/capture'
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
 * THE INVENTORY THIS FILE CLAIMS.
 *
 * A primary action, for AC3, is a control whose activation WRITES or ENQUEUES:
 * it mutates server state, or it queues work that will. A navigation, a
 * disclosure toggle, a filter, a cancel and a re-read are not primary actions
 * and are deliberately absent — they have nothing to be silent about.
 *
 * COVERED CONTROLS, and for each one EVERY precondition its `disabled` binding
 * reads, marked registered or not. A precondition that is not marked
 * "registered" is NOT proven by this file: read an unmarked entry as unexamined,
 * never as safe. Registering one precondition of a control proves that the
 * control honours its binding at all; it says nothing about the others.
 *
 *  - `PaperTriageTable` accept-on-board (`data-action="accept-on-board"`),
 *    which enqueues triage for a capture.
 *    Binding: `isActionDisabled(item) || boardPickBlock !== null`.
 *      `boardPickBlock === 'noBoard'` — REGISTERED.
 *      `boardPickBlock === 'loadFailed'` — REGISTERED.
 *      `boardPickBlock === 'viewOnly'` — REGISTERED.
 *      `boardPickBlock === 'loading'` — not registered: a transient state
 *        between the picker opening and the board fetch settling, not a
 *        standing condition a user acts against.
 *      `boardPickBlock === 'noBoards'` — not registered: out of this slice.
 *      `isActionDisabled`: `readOnly`, `hasMutationInFlight`,
 *        `triagePollingItemId === item.id`, `!canMutate(item)`,
 *        `isEditing(item)`, `isEditingElsewhere(item)` — six further
 *        disjuncts, none registered, all out of this slice.
 *  - `PaperTriageRowEdit` save (`data-action="edit-save"`), which writes the
 *    capture suggestion.
 *    Binding: `saveBlock !== null || saving`.
 *      `saveBlock === 'busyElsewhere'` — REGISTERED.
 *      `saveBlock === 'empty'` — REGISTERED.
 *      `saveBlock === 'unchanged'` — REGISTERED.
 *      `saving` — not registered: it is the in-flight state of a write the
 *        user already started, not a precondition for starting one.
 *    This is the only control here whose every standing precondition is
 *    registered.
 *  - `PaperCaptureNib` submit, which enqueues a capture.
 *    Binding: `!canSubmit`, two conjuncts.
 *      draft is non-blank — REGISTERED.
 *      `!props.submitting` — not registered: parent-owned in-flight state.
 *  - `PaperCaptureComposer` submit, which enqueues a capture.
 *    Binding: `!canSubmit`, FOUR conjuncts.
 *      body is non-blank — REGISTERED.
 *      `!props.submitting` — not registered: parent-owned in-flight state.
 *      `selectedBoardIsWritable` — not registered, out of this slice. It is
 *        the composer's analogue of the table's `viewOnly`, and worth adding.
 *      `!transcriptTooLong` — not registered, out of this slice. It is the one
 *        conjunct here that is a content-length rule rather than a state flag.
 *
 * DELIBERATELY OUT, and why:
 *  - `PaperReviewView` approve / reject / execute — the review surface's
 *    primary actions. GH-2629 holds those files; registering them from here
 *    would conflict with that PR's markup. They are the first thing to add
 *    once it lands, and this file makes NO claim about them today.
 *  - Board and card mutations (`PaperBoardView`, `PaperCardComposer`) and the
 *    settings forms. Their submits are native `type="submit"` inside a real
 *    `<form>`, a shape the sibling source guard already scans; whether their
 *    preconditions are honestly reported is unproven and unclaimed.
 *  - Every other writing control in the app. Absence from this list is not
 *    evidence of a guarded action; it is the absence of evidence.
 *
 * NOT COVERED: whether the validation text is correct, whether a disabled
 * reason is announced to assistive tech, every precondition marked "not
 * registered" above, and every primary action absent from the inventory
 * entirely. Route-walking runtime coverage (AC4) remains open on GH-1949.
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

/**
 * A capture detail the row editor will actually offer for editing:
 * `canEditSuggestion` must be exactly `true` or the editor renders its
 * "not editable" explanation instead of a textarea and a Save button.
 */
function editableDetail(overrides: Partial<CaptureItem> = {}): CaptureItem {
  return {
    id: 'capture-1',
    userId: 'user-1',
    boardId: 'board-alpha',
    status: 'New',
    source: 'Typed',
    textExcerpt: 'Ship the release notes…',
    rawText: 'Ship the release notes before Friday',
    createdAt: new Date('2026-04-25T09:42:00Z').toISOString(),
    processedAt: null,
    retryCount: 0,
    provenance: null,
    canEditSuggestion: true,
    ...overrides,
  } as CaptureItem
}

/** Mount the row editor with its detail fetch already settled. */
async function mountRowEditor(mutationInFlight = false) {
  mockCaptureStore.fetchDetail.mockResolvedValue(editableDetail())
  const wrapper = mount(PaperTriageRowEdit, {
    props: { itemId: 'capture-1', mutationInFlight },
  })
  await flushPromises()
  return wrapper
}

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

  /**
   * Regression for the emit-snapshot aliasing defect. `wrapper.emitted()`
   * returns the LIVE record and Vue Test Utils appends to those arrays in
   * place, so a shallow-spread snapshot aliased them and the errorEvents branch
   * could never fire for an event the component had already emitted once.
   * This control emits `error` in setup AND again on click: it satisfies the
   * contract, so it must PASS. Against the shallow spread it failed.
   */
  it('counts a repeat error emit as feedback when one was already emitted', async () => {
    const AlreadyErrored = {
      emits: ['error'],
      setup(_props: unknown, { emit }: { emit: (event: string, ...args: unknown[]) => void }) {
        emit('error', 'a pre-existing complaint')
        return { fire: () => emit('error', 'pick a board first') }
      },
      template: '<div><button data-action="go" @click="fire">Do it</button></div>',
    }
    const wrapper = mount(AlreadyErrored)
    expect(wrapper.emitted('error')).toHaveLength(1)

    await expectGuardedPrimaryAction(wrapper, wrapper.get('button[data-action="go"]'), {
      unmetPreconditions: 'control that already emitted error before the click',
    })

    // The click really did add a second emit — the helper is not passing by
    // accident on the setup-time one.
    expect(wrapper.emitted('error')).toHaveLength(2)
  })

  // ---- Registered primary actions ----

  it('Inbox "Accept on board" is guarded with no board selected (#1944)', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: boardlessItems() } })

    // Reveal the inline board picker without choosing a board: the precondition.
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    const confirm = wrapper.get('button[data-action="accept-on-board"]')

    // Pin WHICH branch of the contract this control satisfies. The helper
    // accepts either, so without this the test would still pass if the button
    // silently became enabled-with-feedback; #1944's fix was specifically to
    // disable it. `expectGuardedPrimaryAction` never clicks a disabled trigger,
    // so asserting "no accept emitted" afterwards would be trivially true.
    expect(confirm.attributes('disabled')).toBeDefined()

    await expectGuardedPrimaryAction(wrapper, confirm, {
      unmetPreconditions: 'board picker open, no board chosen',
    })
  })

  it('Capture composer submit is guarded with an empty body', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const textarea = wrapper.get('textarea')
    await textarea.setValue('   ')

    const submit = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Capture'))
    expect(submit, 'composer submit button not found').toBeDefined()

    // As above: record which branch is in force rather than asserting the
    // absence of an emit the helper never had a chance to trigger.
    expect(submit!.attributes('disabled')).toBeDefined()

    await expectGuardedPrimaryAction(wrapper, submit!, {
      unmetPreconditions: 'composer body is whitespace only',
    })
  })

  /**
   * `PaperTriageRowEdit` save writes the capture suggestion, and one computed
   * (`saveBlock`) drives the disabled binding, the guard inside `save()` and
   * the visible reason. All three of its states are registered so a future
   * change that keeps the button enabled for one of them cannot pass by
   * satisfying the other two.
   */
  it('Inbox capture-edit Save is guarded with an empty draft', async () => {
    const wrapper = await mountRowEditor()
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('   ')

    const save = wrapper.get('button[data-action="edit-save"]')
    // Pin the branch AND the reason: the helper accepts either design, and
    // `saveBlock` reports `busyElsewhere` before `empty`, so without the
    // reason assertion this test could be silently exercising a different state.
    expect(save.attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="capture-edit-save-reason"]').attributes('data-reason')).toBe('empty')

    await expectGuardedPrimaryAction(wrapper, save, {
      unmetPreconditions: 'capture edit draft is whitespace only',
    })
  })

  it('Inbox capture-edit Save is guarded with an unchanged draft', async () => {
    // Straight off the load: the draft IS the fetched text and no metadata
    // changed, so the write would be a no-op.
    const wrapper = await mountRowEditor()

    const save = wrapper.get('button[data-action="edit-save"]')
    expect(save.attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="capture-edit-save-reason"]').attributes('data-reason')).toBe('unchanged')

    await expectGuardedPrimaryAction(wrapper, save, {
      unmetPreconditions: 'capture edit draft is identical to the loaded text',
    })
  })

  it('Inbox capture-edit Save is guarded while another mutation owns the busy slot', async () => {
    // The one block the user cannot clear from the textarea: another row's
    // Accept or Reject holds the capture store's single busy slot, and saving
    // through it would steal that write's in-flight state.
    const wrapper = await mountRowEditor(true)
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('Ship the release notes on Thursday')

    const save = wrapper.get('button[data-action="edit-save"]')
    expect(save.attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="capture-edit-save-reason"]').attributes('data-reason')).toBe('busyElsewhere')

    await expectGuardedPrimaryAction(wrapper, save, {
      unmetPreconditions: 'another capture mutation holds the shared busy slot',
    })
  })

  it('Inbox capture nib submit is guarded with an empty draft', async () => {
    const wrapper = mount(PaperCaptureNib)

    // The nib footer has exactly one button. Asserting that before selecting it
    // means a markup change that adds another fails loudly here rather than
    // silently registering the wrong control.
    const buttons = wrapper.findAll('button')
    expect(buttons, 'nib should render exactly one button').toHaveLength(1)
    const submit = buttons[0]
    expect(submit.attributes('disabled')).toBeDefined()

    await expectGuardedPrimaryAction(wrapper, submit, {
      unmetPreconditions: 'nib draft is empty',
    })
  })

  // ---- PaperTriageTable registrations (see the boundaries note in the PR) ----

  it('Inbox "Accept on board" is guarded when the board list failed to load', async () => {
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockRejectedValue(new Error('boards unavailable'))
    const wrapper = mount(PaperTriageTable, { props: { items: boardlessItems() } })
    // The failing load is the one `onMounted` primes, not one the click starts:
    // by the time the picker opens, `boardListLoadState` is already `failed`,
    // and `onAccept` re-triggers a load only from `idle`.
    await flushPromises()

    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="board-pick-reason"]').attributes('data-reason')).toBe('loadFailed')
    // The failure is recoverable and says so: the retry control is the reason
    // this state is not simply a dead end.
    expect(wrapper.find('button[data-action="retry-board-load"]').exists()).toBe(true)

    const confirm = wrapper.get('button[data-action="accept-on-board"]')
    expect(confirm.attributes('disabled')).toBeDefined()

    await expectGuardedPrimaryAction(wrapper, confirm, {
      unmetPreconditions: 'board picker open, board list load failed',
    })
  })

  it('Inbox "Accept on board" is guarded when the picked board turns read-only', async () => {
    // `viewOnly` is defence in depth: the picker disables a read-only option,
    // so this state is reached when write access to an ALREADY picked board is
    // revoked and a board-list refresh brings that back (`BoardDto.CanWrite`,
    // #1836). Accepting onto it would 403 at the server.
    mockBoardStore.boards = [{ id: 'board-alpha', name: 'Alpha', canWrite: true }]
    const wrapper = mount(PaperTriageTable, { props: { items: boardlessItems() } })

    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    await wrapper.get('select').setValue('board-alpha')
    // Nothing is blocking yet — the pick is live. That is what makes the flip
    // below the thing under test rather than a state that was always off.
    expect(wrapper.find('[data-testid="board-pick-reason"]').exists()).toBe(false)

    mockBoardStore.boards = [{ id: 'board-alpha', name: 'Alpha', canWrite: false }]
    await nextTick()

    expect(wrapper.get('[data-testid="board-pick-reason"]').attributes('data-reason')).toBe('viewOnly')

    const confirm = wrapper.get('button[data-action="accept-on-board"]')
    expect(confirm.attributes('disabled')).toBeDefined()

    await expectGuardedPrimaryAction(wrapper, confirm, {
      unmetPreconditions: 'board picker open, picked board is read-only',
    })
  })
})
