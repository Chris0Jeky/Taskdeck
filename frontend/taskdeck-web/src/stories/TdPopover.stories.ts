import type { Meta, StoryObj } from '@storybook/vue3-vite'
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
  render: (args) => ({
    components: { TdPopover, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div style="padding: 2rem;">
        <TdPopover :open="args.open" :align="args.align" :position="args.position" @close="args.open = false">
          <template #trigger>
            <TdButton variant="secondary" @click="args.open = !args.open">Show info</TdButton>
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
  args: {
    position: 'top',
  },
  render: (args) => ({
    components: { TdPopover, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div style="padding: 6rem 2rem 2rem;">
        <TdPopover :open="args.open" :align="args.align" :position="args.position" @close="args.open = false">
          <template #trigger>
            <TdButton variant="secondary" @click="args.open = !args.open">Show above</TdButton>
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
  args: {
    align: 'center',
  },
  render: (args) => ({
    components: { TdPopover, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div style="padding: 2rem; display: flex; justify-content: center;">
        <TdPopover :open="args.open" :align="args.align" :position="args.position" @close="args.open = false">
          <template #trigger>
            <TdButton variant="secondary" @click="args.open = !args.open">Center aligned</TdButton>
          </template>
          <p style="margin: 0; color: var(--td-text-primary); font-size: var(--td-font-sm);">
            Centered popover content.
          </p>
        </TdPopover>
      </div>
    `,
  }),
}
