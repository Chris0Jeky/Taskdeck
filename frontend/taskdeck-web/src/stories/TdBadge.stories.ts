import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdBadge from '../components/ui/TdBadge.vue'

const meta = {
  title: 'UI Primitives/TdBadge',
  component: TdBadge,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: 'select',
      options: ['default', 'primary', 'success', 'warning', 'error', 'info'],
    },
    size: {
      control: 'select',
      options: ['sm', 'md'],
    },
  },
  args: {
    variant: 'default',
    size: 'md',
  },
  render: (args) => ({
    components: { TdBadge },
    setup() {
      return { args }
    },
    template: '<TdBadge v-bind="args">Badge</TdBadge>',
  }),
} satisfies Meta<typeof TdBadge>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const Primary: Story = {
  args: { variant: 'primary' },
  render: (args) => ({
    components: { TdBadge },
    setup() { return { args } },
    template: '<TdBadge v-bind="args">Active</TdBadge>',
  }),
}

export const Success: Story = {
  args: { variant: 'success' },
  render: (args) => ({
    components: { TdBadge },
    setup() { return { args } },
    template: '<TdBadge v-bind="args">Done</TdBadge>',
  }),
}

export const Warning: Story = {
  args: { variant: 'warning' },
  render: (args) => ({
    components: { TdBadge },
    setup() { return { args } },
    template: '<TdBadge v-bind="args">Pending</TdBadge>',
  }),
}

export const Error: Story = {
  args: { variant: 'error' },
  render: (args) => ({
    components: { TdBadge },
    setup() { return { args } },
    template: '<TdBadge v-bind="args">Failed</TdBadge>',
  }),
}

export const Info: Story = {
  args: { variant: 'info' },
  render: (args) => ({
    components: { TdBadge },
    setup() { return { args } },
    template: '<TdBadge v-bind="args">New</TdBadge>',
  }),
}

export const SmallSize: Story = {
  args: { size: 'sm' },
  render: (args) => ({
    components: { TdBadge },
    setup() { return { args } },
    template: '<TdBadge v-bind="args">Small</TdBadge>',
  }),
}

export const AllVariants: Story = {
  render: () => ({
    components: { TdBadge },
    template: `
      <div style="display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap;">
        <TdBadge variant="default">Default</TdBadge>
        <TdBadge variant="primary">Primary</TdBadge>
        <TdBadge variant="success">Success</TdBadge>
        <TdBadge variant="warning">Warning</TdBadge>
        <TdBadge variant="error">Error</TdBadge>
        <TdBadge variant="info">Info</TdBadge>
      </div>
    `,
  }),
}
