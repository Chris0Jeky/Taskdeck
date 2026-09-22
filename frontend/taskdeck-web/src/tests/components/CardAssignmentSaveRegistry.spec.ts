import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import CardAssignmentField from '../../components/board/CardAssignmentField.vue'
import { cardsApi } from '../../api/cardsApi'
import {
  assignmentSaveRegistryKey,
  createAssignmentSaveRegistry,
} from '../../composables/useAssignmentSaveRegistry'
import type { Card } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getParticipants: vi.fn(),
    replaceAssignments: vi.fn(),
    getCard: vi.fn(),
  },
}))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ userId: 'me' }) }))

const card: Card = {
  id: 'card',
  boardId: 'board',
  columnId: 'col',
  title: 'Draft',
  description: '',
  labels: [],
  isBlocked: false,
  blockReason: null,
  dueDate: null,
  position: 0,
  createdAt: 'old',
  updatedAt: 'v1',
  assignments: [],
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((settle, fail) => {
    resolve = settle
    reject = fail
  })
  return { promise, resolve, reject }
}

function mountField(events: boolean[]) {
  const registry = createAssignmentSaveRegistry(saving => events.push(saving))
  return mount(CardAssignmentField, {
    props: { card, readOnly: false },
    global: {
      provide: {
        [assignmentSaveRegistryKey as symbol]: registry,
      },
    },
  })
}

function saveButton(wrapper: ReturnType<typeof mount>) {
  return wrapper.findAll('button').find(button => button.text() === 'Save assignments')!
}

describe('CardAssignmentField assignment-save registry', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([
      { userId: 'me', displayName: 'Owner' },
    ])
  })

  it('releases a successful PUT after the field unmounts', async () => {
    const request = deferred<Card>()
    vi.mocked(cardsApi.replaceAssignments).mockReturnValueOnce(request.promise)
    const events: boolean[] = []
    const wrapper = mountField(events)
    await flushPromises()

    await wrapper.find('input').setValue(true)
    await saveButton(wrapper).trigger('click')
    expect(events).toEqual([true])

    wrapper.unmount()
    request.resolve({ ...card, updatedAt: 'v2' })
    await flushPromises()

    expect(events).toEqual([true, false])
  })

  it('releases a failed PUT after the field unmounts', async () => {
    const request = deferred<Card>()
    vi.mocked(cardsApi.replaceAssignments).mockReturnValueOnce(request.promise)
    const events: boolean[] = []
    const wrapper = mountField(events)
    await flushPromises()

    await wrapper.find('input').setValue(true)
    await saveButton(wrapper).trigger('click')
    expect(events).toEqual([true])

    wrapper.unmount()
    request.reject(new Error('save failed'))
    await flushPromises()

    expect(events).toEqual([true, false])
  })
})
