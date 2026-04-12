import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdButton from '../components/ui/TdButton.vue'

const meta = {
  title: 'UI Primitives/TdButton',
  component: TdButton,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: 'select',
      options: ['primary', 'secondary', 'ghost', 'danger'],
    },
    size: {
      control: 'select',
      options: ['sm', 'md', 'lg'],
    },
    disabled: { control: 'boolean' },
    loading: { control: 'boolean' },
    type: {
      control: 'select',
      options: ['button', 'submit', 'reset'],
    },
  },
  args: {
    variant: 'primary',
    size: 'md',
    disabled: false,
    loading: false,
    type: 'button',
  },
  render: (args) => ({
    components: { TdButton },
    setup() {
      return { args }
    },
    template: '<TdButton v-bind="args">Button</TdButton>',
  }),
} satisfies Meta<typeof TdButton>

export default meta
type Story = StoryObj<typeof meta>

export const Primary: Story = {
  args: { variant: 'primary' },
}

export const Secondary: Story = {
  args: { variant: 'secondary' },
}

export const Ghost: Story = {
  args: { variant: 'ghost' },
}

export const Danger: Story = {
  args: { variant: 'danger' },
}

export const Small: Story = {
  args: { size: 'sm' },
}

export const Large: Story = {
  args: { size: 'lg' },
}

export const Disabled: Story = {
  args: { disabled: true },
}

export const Loading: Story = {
  args: { loading: true },
}

export const AllVariants: Story = {
  render: () => ({
    components: { TdButton },
    template: `
      <div style="display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap;">
        <TdButton variant="primary">Primary</TdButton>
        <TdButton variant="secondary">Secondary</TdButton>
        <TdButton variant="ghost">Ghost</TdButton>
        <TdButton variant="danger">Danger</TdButton>
      </div>
    `,
  }),
}

export const AllSizes: Story = {
  render: () => ({
    components: { TdButton },
    template: `
      <div style="display: flex; gap: 0.5rem; align-items: center;">
        <TdButton size="sm">Small</TdButton>
        <TdButton size="md">Medium</TdButton>
        <TdButton size="lg">Large</TdButton>
      </div>
    `,
  }),
}
