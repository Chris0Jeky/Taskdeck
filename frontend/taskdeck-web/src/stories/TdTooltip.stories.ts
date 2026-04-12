import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdTooltip from '../components/ui/TdTooltip.vue'
import TdButton from '../components/ui/TdButton.vue'

const meta = {
  title: 'UI Primitives/TdTooltip',
  component: TdTooltip,
  tags: ['autodocs'],
  argTypes: {
    text: { control: 'text' },
    position: {
      control: 'select',
      options: ['top', 'bottom', 'left', 'right'],
    },
    delay: { control: 'number' },
  },
  args: {
    text: 'Tooltip text',
    position: 'top',
    delay: 300,
  },
  render: (args) => ({
    components: { TdTooltip, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div style="padding: 4rem; display: flex; justify-content: center;">
        <TdTooltip v-bind="args">
          <TdButton variant="secondary">Hover me</TdButton>
        </TdTooltip>
      </div>
    `,
  }),
} satisfies Meta<typeof TdTooltip>

export default meta
type Story = StoryObj<typeof meta>

export const Top: Story = {
  args: { text: 'Tooltip on top', position: 'top' },
}

export const Bottom: Story = {
  args: { text: 'Tooltip on bottom', position: 'bottom' },
}

export const Left: Story = {
  args: { text: 'Tooltip on left', position: 'left' },
}

export const Right: Story = {
  args: { text: 'Tooltip on right', position: 'right' },
}

export const NoDelay: Story = {
  args: { text: 'Instant tooltip', delay: 0 },
}
