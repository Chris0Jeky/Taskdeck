import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdIconButton from '../components/ui/TdIconButton.vue'

const meta = {
  title: 'UI Primitives/TdIconButton',
  component: TdIconButton,
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
    label: { control: 'text' },
  },
  args: {
    variant: 'ghost',
    size: 'md',
    disabled: false,
    loading: false,
    label: 'Close',
  },
  render: (args) => ({
    components: { TdIconButton },
    setup() {
      return { args }
    },
    template: `
      <TdIconButton v-bind="args">
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
          <path d="M4 4L12 12M12 4L4 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
        </svg>
      </TdIconButton>
    `,
  }),
} satisfies Meta<typeof TdIconButton>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const Primary: Story = {
  args: { variant: 'primary' },
}

export const Secondary: Story = {
  args: { variant: 'secondary' },
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
    components: { TdIconButton },
    template: `
      <div style="display: flex; gap: 0.5rem; align-items: center;">
        <TdIconButton variant="primary" label="Primary">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M8 3V13M3 8H13" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>
        </TdIconButton>
        <TdIconButton variant="secondary" label="Secondary">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M8 3V13M3 8H13" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>
        </TdIconButton>
        <TdIconButton variant="ghost" label="Ghost">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M8 3V13M3 8H13" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>
        </TdIconButton>
        <TdIconButton variant="danger" label="Danger">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M4 4L12 12M12 4L4 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>
        </TdIconButton>
      </div>
    `,
  }),
}
