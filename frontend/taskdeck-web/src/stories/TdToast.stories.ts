import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdToast from '../components/ui/TdToast.vue'

const meta = {
  title: 'UI Primitives/TdToast',
  component: TdToast,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: 'select',
      options: ['info', 'success', 'warning', 'error'],
    },
    message: { control: 'text' },
    dismissible: { control: 'boolean' },
  },
  args: {
    variant: 'info',
    message: 'This is a toast notification.',
    dismissible: true,
  },
} satisfies Meta<typeof TdToast>

export default meta
type Story = StoryObj<typeof meta>

export const Info: Story = {
  args: { variant: 'info', message: 'New proposal available for review.' },
}

export const Success: Story = {
  args: { variant: 'success', message: 'Task created successfully.' },
}

export const Warning: Story = {
  args: { variant: 'warning', message: 'Connection is unstable.' },
}

export const Error: Story = {
  args: { variant: 'error', message: 'Failed to save changes.' },
}

export const NonDismissible: Story = {
  args: { variant: 'info', message: 'Processing...', dismissible: false },
}

export const AllVariants: Story = {
  render: () => ({
    components: { TdToast },
    template: `
      <div style="display: flex; flex-direction: column; gap: 0.5rem;">
        <TdToast variant="info" message="New proposal available for review." />
        <TdToast variant="success" message="Task created successfully." />
        <TdToast variant="warning" message="Connection is unstable." />
        <TdToast variant="error" message="Failed to save changes." />
      </div>
    `,
  }),
}
