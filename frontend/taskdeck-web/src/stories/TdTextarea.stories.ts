import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdTextarea from '../components/ui/TdTextarea.vue'

const meta = {
  title: 'UI Primitives/TdTextarea',
  component: TdTextarea,
  tags: ['autodocs'],
  argTypes: {
    disabled: { control: 'boolean' },
    readonly: { control: 'boolean' },
    error: { control: 'boolean' },
    placeholder: { control: 'text' },
    modelValue: { control: 'text' },
    rows: { control: 'number' },
  },
  args: {
    placeholder: 'Enter description...',
    modelValue: '',
    disabled: false,
    readonly: false,
    error: false,
    rows: 3,
  },
} satisfies Meta<typeof TdTextarea>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const WithValue: Story = {
  args: { modelValue: 'This is a multi-line\ntextarea with content.' },
}

export const Error: Story = {
  args: { error: true, modelValue: 'Invalid content' },
}

export const Disabled: Story = {
  args: { disabled: true, modelValue: 'Cannot edit' },
}

export const Readonly: Story = {
  args: { readonly: true, modelValue: 'Read-only content' },
}

export const TallTextarea: Story = {
  args: { rows: 8, placeholder: 'More room to write...' },
}
