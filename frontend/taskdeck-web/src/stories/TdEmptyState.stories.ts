import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdEmptyState from '../components/ui/TdEmptyState.vue'
import TdButton from '../components/ui/TdButton.vue'

const meta = {
  title: 'UI Primitives/TdEmptyState',
  component: TdEmptyState,
  tags: ['autodocs'],
  argTypes: {
    title: { control: 'text' },
    description: { control: 'text' },
  },
  args: {
    title: 'No items yet',
    description: '',
  },
} satisfies Meta<typeof TdEmptyState>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {
  args: { title: 'No tasks found' },
}

export const WithDescription: Story = {
  args: {
    title: 'No tasks found',
    description: 'Create your first task to get started with Taskdeck.',
  },
}

export const WithAction: Story = {
  render: () => ({
    components: { TdEmptyState, TdButton },
    template: `
      <TdEmptyState
        title="No proposals"
        description="Capture some input to generate proposals for review."
      >
        <template #action>
          <TdButton variant="primary">Capture Input</TdButton>
        </template>
      </TdEmptyState>
    `,
  }),
}

export const WithIcon: Story = {
  render: () => ({
    components: { TdEmptyState, TdButton },
    template: `
      <TdEmptyState
        title="Inbox is empty"
        description="All caught up! No pending items to review."
      >
        <template #icon>
          <svg width="48" height="48" viewBox="0 0 48 48" fill="none">
            <circle cx="24" cy="24" r="20" stroke="currentColor" stroke-width="2" opacity="0.3"/>
            <path d="M16 24L22 30L32 18" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </template>
      </TdEmptyState>
    `,
  }),
}
