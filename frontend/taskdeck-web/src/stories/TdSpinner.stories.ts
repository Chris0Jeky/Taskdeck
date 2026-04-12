import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdSpinner from '../components/ui/TdSpinner.vue'

const meta = {
  title: 'UI Primitives/TdSpinner',
  component: TdSpinner,
  tags: ['autodocs'],
  argTypes: {
    size: {
      control: 'select',
      options: ['sm', 'md', 'lg'],
    },
    label: { control: 'text' },
  },
  args: {
    size: 'md',
    label: 'Loading',
  },
} satisfies Meta<typeof TdSpinner>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const Small: Story = {
  args: { size: 'sm', label: 'Loading...' },
}

export const Large: Story = {
  args: { size: 'lg', label: 'Please wait' },
}

export const CustomLabel: Story = {
  args: { label: 'Generating proposal...' },
}

export const AllSizes: Story = {
  render: () => ({
    components: { TdSpinner },
    template: `
      <div style="display: flex; gap: 1.5rem; align-items: center;">
        <TdSpinner size="sm" label="Small" />
        <TdSpinner size="md" label="Medium" />
        <TdSpinner size="lg" label="Large" />
      </div>
    `,
  }),
}
