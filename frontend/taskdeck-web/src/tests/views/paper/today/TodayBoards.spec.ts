import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TodayBoards from '../../../../views/paper/today/TodayBoards.vue'
import type { DossierBoardLine } from '../../../../composables/useTodayDossier'

describe('TodayBoards', () => {
  it('uses stable board ids so duplicate names do not trigger duplicate key warnings', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const boards: DossierBoardLine[] = [
      { id: 'board-a', name: 'Operations', moves: 2, proposals: 1 },
      { id: 'board-b', name: 'Operations', moves: 1, proposals: 0 },
    ]

    const wrapper = mount(TodayBoards, { props: { boards } })

    expect(wrapper.findAll('.today-board')).toHaveLength(2)
    expect(warn.mock.calls.flat().join(' ')).not.toContain('Duplicate keys')
    warn.mockRestore()
  })
})
