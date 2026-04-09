import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdInlineAlert from '../components/ui/TdInlineAlert.vue'

const meta = {
  title: 'UI Primitives/TdInlineAlert',
  component: TdInlineAlert,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: 'select',
      options: ['info', 'success', 'warning', 'error'],
    },
    dismissible: { control: 'boolean' },
  },
  args: {
    variant: 'info',
    dismissible: false,
  },
  render: (args) => ({
    components: { TdInlineAlert },
    setup() {
      return { args }
    },
    template: '<TdInlineAlert v-bind="args">This is an inline alert message.</TdInlineAlert>',
  }),
} satisfies Meta<typeof TdInlineAlert>

export default meta
type Story = StoryObj<typeof meta>

export const Info: Story = {
  args: { variant: 'info' },
  render: (args) => ({
    components: { TdInlineAlert },
    setup() { return { args } },
    template: '<TdInlineAlert v-bind="args">Board changes require review before applying.</TdInlineAlert>',
  }),
}

export const Success: Story = {
  args: { variant: 'success' },
  render: (args) => ({
    components: { TdInlineAlert },
    setup() { return { args } },
    template: '<TdInlineAlert v-bind="args">Proposal approved and applied.</TdInlineAlert>',
  }),
}

export const Warning: Story = {
  args: { variant: 'warning' },
  render: (args) => ({
    components: { TdInlineAlert },
    setup() { return { args } },
    template: '<TdInlineAlert v-bind="args">Offline mode: changes will sync later.</TdInlineAlert>',
  }),
}

export const Error: Story = {
  args: { variant: 'error' },
  render: (args) => ({
    components: { TdInlineAlert },
    setup() { return { args } },
    template: '<TdInlineAlert v-bind="args">Failed to load board data.</TdInlineAlert>',
  }),
}

export const Dismissible: Story = {
  args: { variant: 'warning', dismissible: true },
  render: (args) => ({
    components: { TdInlineAlert },
    setup() { return { args } },
    template: '<TdInlineAlert v-bind="args">This alert can be dismissed.</TdInlineAlert>',
  }),
}

export const AllVariants: Story = {
  render: () => ({
    components: { TdInlineAlert },
    template: `
      <div style="display: flex; flex-direction: column; gap: 0.5rem; max-width: 400px;">
        <TdInlineAlert variant="info">Info alert message.</TdInlineAlert>
        <TdInlineAlert variant="success">Success alert message.</TdInlineAlert>
        <TdInlineAlert variant="warning">Warning alert message.</TdInlineAlert>
        <TdInlineAlert variant="error">Error alert message.</TdInlineAlert>
      </div>
    `,
  }),
}
