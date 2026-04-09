import type { Meta, StoryObj } from '@storybook/vue3-vite'
import { ref } from 'vue'
import TdDropdown from '../components/ui/TdDropdown.vue'
import TdButton from '../components/ui/TdButton.vue'

const meta = {
  title: 'UI Primitives/TdDropdown',
  component: TdDropdown,
  tags: ['autodocs'],
  argTypes: {
    open: { control: 'boolean' },
    align: {
      control: 'select',
      options: ['left', 'right'],
    },
  },
  args: {
    open: false,
    align: 'left',
  },
} satisfies Meta<typeof TdDropdown>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {
  render: () => ({
    components: { TdDropdown, TdButton },
    setup() {
      const isOpen = ref(false)
      return { isOpen }
    },
    template: `
      <div style="padding: 1rem;">
        <TdDropdown :open="isOpen" @close="isOpen = false">
          <template #trigger>
            <TdButton variant="secondary" @click="isOpen = !isOpen">Actions</TdButton>
          </template>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="isOpen = false">Edit</button>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="isOpen = false">Duplicate</button>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-color-error); cursor: pointer; font-size: var(--td-font-sm);" @click="isOpen = false">Delete</button>
        </TdDropdown>
      </div>
    `,
  }),
}

export const AlignRight: Story = {
  render: () => ({
    components: { TdDropdown, TdButton },
    setup() {
      const isOpen = ref(false)
      return { isOpen }
    },
    template: `
      <div style="padding: 1rem; display: flex; justify-content: flex-end;">
        <TdDropdown :open="isOpen" align="right" @close="isOpen = false">
          <template #trigger>
            <TdButton variant="secondary" @click="isOpen = !isOpen">Menu</TdButton>
          </template>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="isOpen = false">Settings</button>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="isOpen = false">Profile</button>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="isOpen = false">Logout</button>
        </TdDropdown>
      </div>
    `,
  }),
}
