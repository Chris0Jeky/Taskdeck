import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'

import CardAssignmentField from '../../components/board/CardAssignmentField.vue'
import { cardsApi } from '../../api/cardsApi'
import type { Card } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getParticipants: vi.fn(),
    getCard: vi.fn(),
    replaceAssignments: vi.fn(),
  },
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({ userId: 'owner-1' }),
}))

const originalVersion = '2026-09-18T08:00:00Z'
const committedVersion = '2026-09-18T09:00:00Z'
const savedVersion = '2026-09-18T10:00:00Z'

const card: Card = {
  id: 'card-1',
  boardId: 'board-1',
  columnId: 'column-1',
  title: 'Retain assignment draft',
  description: '',
  labels: [],
  assignments: [],
  isBlocked: false,
  isArchived: false,
  blockReason: null,
  dueDate: null,
  position: 0,
  createdAt: '2026-09-18T07:00:00Z',
  updatedAt: originalVersion,
}

const committedReceipt: Card = {
  ...card,
  updatedAt: committedVersion,
}

describe('CardAssignmentField committed lifecycle receipt', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([
      { userId: 'member-1', displayName: 'Member One' },
    ])
    vi.mocked(cardsApi.getCard).mockResolvedValue(committedReceipt)
    vi.mocked(cardsApi.replaceAssignments).mockResolvedValue({
      ...committedReceipt,
      updatedAt: savedVersion,
      assignments: [
        {
          userId: 'member-1',
          displayName: 'Member One',
          assignedAt: '2026-09-18T09:30:00Z',
          assignedByUserId: 'owner-1',
        },
      ],
    })
  })

  it('advances only the write version while retaining a pre-existing local selection', async () => {
    const wrapper = mount(CardAssignmentField, {
      props: {
        card,
        committedCard: null,
        readOnly: false,
      },
    })
    await flushPromises()

    const member = wrapper.get('input[type="checkbox"][value="member-1"]')
    await member.setValue(true)
    expect((member.element as HTMLInputElement).checked).toBe(true)
    expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([true])

    await wrapper.setProps({ committedCard: committedReceipt })
    await flushPromises()

    expect((member.element as HTMLInputElement).checked).toBe(true)
    const save = wrapper.findAll('button').find(button => button.text() === 'Save assignments')
    expect(save).toBeDefined()
    expect(save!.attributes('disabled')).toBeUndefined()

    await save!.trigger('click')
    await flushPromises()

    expect(cardsApi.replaceAssignments).toHaveBeenCalledWith(
      card.boardId,
      card.id,
      ['member-1'],
      committedVersion,
    )
  })
})
