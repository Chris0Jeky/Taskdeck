import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import BoardDialogHost from '../../components/board/BoardDialogHost.vue'

const board = {
  id: 'board-1',
  name: 'Ops Board',
  description: 'Primary board',
  columns: [],
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
}

describe('BoardDialogHost', () => {
  it('wires board settings visibility updates from close and updated events', async () => {
    const wrapper = mount(BoardDialogHost, {
      props: {
        board,
        boardId: 'board-1',
        boardLabels: [],
        showBoardSettings: true,
        showLabelManager: false,
        showStarterPackCatalog: false,
        showKeyboardHelp: false,
        showCaptureModal: false,
      },
      global: {
        stubs: {
          BoardSettingsModal: {
            template: `
              <div>
                <button data-testid="settings-close" type="button" @click="$emit('close')">close</button>
                <button data-testid="settings-updated" type="button" @click="$emit('updated')">updated</button>
              </div>
            `,
          },
          StarterPackCatalogModal: true,
          LabelManagerModal: true,
          KeyboardShortcutsHelp: true,
          CaptureModal: true,
        },
      },
    })

    await wrapper.get('[data-testid="settings-close"]').trigger('click')
    await wrapper.get('[data-testid="settings-updated"]').trigger('click')

    expect(wrapper.emitted('update:showBoardSettings')).toEqual([[false], [false]])
  })

  it('wires starter pack, label manager, and keyboard help close flows', async () => {
    const wrapper = mount(BoardDialogHost, {
      props: {
        board,
        boardId: 'board-1',
        boardLabels: [{ id: 'label-1', boardId: 'board-1', name: 'Urgent', color: '#f00', createdAt: '', updatedAt: '' }],
        showBoardSettings: false,
        showLabelManager: true,
        showStarterPackCatalog: true,
        showKeyboardHelp: true,
        showCaptureModal: false,
      },
      global: {
        stubs: {
          BoardSettingsModal: true,
          StarterPackCatalogModal: {
            props: ['boardId', 'isOpen'],
            template: `
              <button data-testid="starter-pack-applied" type="button" @click="$emit('applied')">applied</button>
            `,
          },
          LabelManagerModal: {
            props: ['isOpen', 'boardId', 'labels'],
            template: `
              <button data-testid="label-close" type="button" @click="$emit('close')">close</button>
            `,
          },
          KeyboardShortcutsHelp: {
            props: ['isOpen'],
            template: `
              <button data-testid="keyboard-close" type="button" @click="$emit('close')">close</button>
            `,
          },
          CaptureModal: true,
        },
      },
    })

    await wrapper.get('[data-testid="starter-pack-applied"]').trigger('click')
    await wrapper.get('[data-testid="label-close"]').trigger('click')
    await wrapper.get('[data-testid="keyboard-close"]').trigger('click')

    expect(wrapper.emitted('update:showStarterPackCatalog')).toEqual([[false]])
    expect(wrapper.emitted('update:showLabelManager')).toEqual([[false]])
    expect(wrapper.emitted('update:showKeyboardHelp')).toEqual([[false]])
  })

  it('shows capture modal only when both board and visibility are present', async () => {
    const wrapper = mount(BoardDialogHost, {
      props: {
        board,
        boardId: 'board-1',
        boardLabels: [],
        showBoardSettings: false,
        showLabelManager: false,
        showStarterPackCatalog: false,
        showKeyboardHelp: false,
        showCaptureModal: true,
      },
      global: {
        stubs: {
          BoardSettingsModal: true,
          StarterPackCatalogModal: true,
          LabelManagerModal: true,
          KeyboardShortcutsHelp: true,
          CaptureModal: {
            props: ['boardId', 'boardName'],
            template: `
              <button data-testid="capture-close" type="button" @click="$emit('close')">{{ boardName }}|{{ boardId }}</button>
            `,
          },
        },
      },
    })

    expect(wrapper.get('[data-testid="capture-close"]').text()).toBe('Ops Board|board-1')
    await wrapper.get('[data-testid="capture-close"]').trigger('click')
    expect(wrapper.emitted('update:showCaptureModal')).toEqual([[false]])

    await wrapper.setProps({ board: null })
    expect(wrapper.find('[data-testid="capture-close"]').exists()).toBe(false)
  })
})
