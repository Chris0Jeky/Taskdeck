import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdInput from '../components/ui/TdInput.vue'

const meta = {
  title: 'UI Primitives/TdInput',
  component: TdInput,
  tags: ['autodocs'],
  argTypes: {
    type: {
      control: 'select',
      options: ['text', 'email', 'password', 'number', 'search', 'url', 'tel'],
    },
    disabled: { control: 'boolean' },
    readonly: { control: 'boolean' },
    error: { control: 'boolean' },
    placeholder: { control: 'text' },
    modelValue: { control: 'text' },
  },
  args: {
    type: 'text',
    placeholder: 'Enter text...',
    modelValue: '',
    disabled: false,
    readonly: false,
    error: false,
  },
} satisfies Meta<typeof TdInput>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const WithValue: Story = {
  args: { modelValue: 'Hello, Taskdeck' },
}

export const WithPlaceholder: Story = {
  args: { placeholder: 'Search tasks...' },
}

export const Error: Story = {
  args: { error: true, modelValue: 'Invalid input' },
}

export const Disabled: Story = {
  args: { disabled: true, modelValue: 'Cannot edit' },
}

export const Readonly: Story = {
  args: { readonly: true, modelValue: 'Read-only value' },
}

export const Password: Story = {
  args: { type: 'password', placeholder: 'Enter password...' },
}

export const AllStates: Story = {
  render: () => ({
    components: { TdInput },
    template: `
      <div style="display: flex; flex-direction: column; gap: 0.75rem; max-width: 320px;">
        <TdInput placeholder="Default" />
        <TdInput model-value="With value" />
        <TdInput error model-value="Error state" />
        <TdInput disabled model-value="Disabled" />
        <TdInput readonly model-value="Read-only" />
      </div>
    `,
  }),
}
