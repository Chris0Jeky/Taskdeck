import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdSkeleton from '../components/ui/TdSkeleton.vue'

const meta = {
  title: 'UI Primitives/TdSkeleton',
  component: TdSkeleton,
  tags: ['autodocs'],
  argTypes: {
    width: { control: 'text' },
    height: { control: 'text' },
    rounded: { control: 'boolean' },
    circle: { control: 'boolean' },
  },
  args: {
    width: '100%',
    height: '1rem',
    rounded: true,
    circle: false,
  },
} satisfies Meta<typeof TdSkeleton>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const TextLine: Story = {
  args: { width: '200px', height: '1rem' },
}

export const Avatar: Story = {
  args: { width: '40px', height: '40px', circle: true },
}

export const Card: Story = {
  args: { width: '300px', height: '120px' },
}

export const CardLayout: Story = {
  render: () => ({
    components: { TdSkeleton },
    template: `
      <div style="display: flex; flex-direction: column; gap: 0.75rem; max-width: 320px;">
        <div style="display: flex; gap: 0.75rem; align-items: center;">
          <TdSkeleton width="40px" height="40px" circle />
          <div style="flex: 1; display: flex; flex-direction: column; gap: 0.4rem;">
            <TdSkeleton width="60%" height="0.875rem" />
            <TdSkeleton width="40%" height="0.75rem" />
          </div>
        </div>
        <TdSkeleton width="100%" height="4rem" />
        <div style="display: flex; gap: 0.5rem;">
          <TdSkeleton width="4rem" height="1.5rem" />
          <TdSkeleton width="3rem" height="1.5rem" />
        </div>
      </div>
    `,
  }),
}
