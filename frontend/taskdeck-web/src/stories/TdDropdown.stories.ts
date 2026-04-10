import type { Meta, StoryObj } from '@storybook/vue3-vite'
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
  render: (args) => ({
    components: { TdDropdown, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div style="padding: 1rem;">
        <TdDropdown :open="args.open" :align="args.align" @close="args.open = false">
          <template #trigger>
            <TdButton variant="secondary" @click="args.open = !args.open">Actions</TdButton>
          </template>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="args.open = false">Edit</button>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="args.open = false">Duplicate</button>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-color-error); cursor: pointer; font-size: var(--td-font-sm);" @click="args.open = false">Delete</button>
        </TdDropdown>
      </div>
    `,
  }),
}

export const AlignRight: Story = {
  render: (args) => ({
    components: { TdDropdown, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div style="padding: 1rem; display: flex; justify-content: flex-end;">
        <TdDropdown :open="args.open" :align="args.align" @close="args.open = false">
          <template #trigger>
            <TdButton variant="secondary" @click="args.open = !args.open">Menu</TdButton>
          </template>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="args.open = false">Settings</button>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="args.open = false">Profile</button>
          <button style="display: block; width: 100%; text-align: left; padding: 0.4rem 0.8rem; background: none; border: none; color: var(--td-text-primary); cursor: pointer; font-size: var(--td-font-sm);" @click="args.open = false">Logout</button>
        </TdDropdown>
      </div>
    `,
  }),
}
