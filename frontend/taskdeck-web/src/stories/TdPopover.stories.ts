import type { Meta, StoryObj } from '@storybook/vue3-vite'
import { ref } from 'vue'
import TdPopover from '../components/ui/TdPopover.vue'
import TdButton from '../components/ui/TdButton.vue'

const meta = {
  title: 'UI Primitives/TdPopover',
  component: TdPopover,
  tags: ['autodocs'],
  argTypes: {
    open: { control: 'boolean' },
    align: {
      control: 'select',
      options: ['left', 'right', 'center'],
    },
    position: {
      control: 'select',
      options: ['top', 'bottom'],
    },
  },
  args: {
    open: false,
    align: 'left',
    position: 'bottom',
  },
} satisfies Meta<typeof TdPopover>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {
  render: () => ({
    components: { TdPopover, TdButton },
    setup() {
      const isOpen = ref(false)
      return { isOpen }
    },
    template: `
      <div style="padding: 2rem;">
        <TdPopover :open="isOpen" @close="isOpen = false">
          <template #trigger>
            <TdButton variant="secondary" @click="isOpen = !isOpen">Show info</TdButton>
          </template>
          <p style="margin: 0; color: var(--td-text-primary); font-size: var(--td-font-sm);">
            Popover content can include any elements.
          </p>
        </TdPopover>
      </div>
    `,
  }),
}

export const TopPosition: Story = {
  render: () => ({
    components: { TdPopover, TdButton },
    setup() {
      const isOpen = ref(false)
      return { isOpen }
    },
    template: `
      <div style="padding: 6rem 2rem 2rem;">
        <TdPopover :open="isOpen" position="top" @close="isOpen = false">
          <template #trigger>
            <TdButton variant="secondary" @click="isOpen = !isOpen">Show above</TdButton>
          </template>
          <p style="margin: 0; color: var(--td-text-primary); font-size: var(--td-font-sm);">
            This popover appears above the trigger.
          </p>
        </TdPopover>
      </div>
    `,
  }),
}

export const CenterAligned: Story = {
  render: () => ({
    components: { TdPopover, TdButton },
    setup() {
      const isOpen = ref(false)
      return { isOpen }
    },
    template: `
      <div style="padding: 2rem; display: flex; justify-content: center;">
        <TdPopover :open="isOpen" align="center" @close="isOpen = false">
          <template #trigger>
            <TdButton variant="secondary" @click="isOpen = !isOpen">Center aligned</TdButton>
          </template>
          <p style="margin: 0; color: var(--td-text-primary); font-size: var(--td-font-sm);">
            Centered popover content.
          </p>
        </TdPopover>
      </div>
    `,
  }),
}
