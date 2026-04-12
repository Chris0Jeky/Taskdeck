import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdSelect from '../components/ui/TdSelect.vue'

const meta = {
  title: 'UI Primitives/TdSelect',
  component: TdSelect,
  tags: ['autodocs'],
  argTypes: {
    disabled: { control: 'boolean' },
    error: { control: 'boolean' },
    placeholder: { control: 'text' },
    modelValue: { control: 'text' },
  },
  args: {
    placeholder: 'Choose an option...',
    modelValue: '',
    disabled: false,
    error: false,
  },
  render: (args) => ({
    components: { TdSelect },
    setup() {
      return { args }
    },
    template: `
      <TdSelect v-bind="args" style="max-width: 320px;">
        <option value="backlog">Backlog</option>
        <option value="todo">To Do</option>
        <option value="in-progress">In Progress</option>
        <option value="done">Done</option>
      </TdSelect>
    `,
  }),
} satisfies Meta<typeof TdSelect>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const WithSelection: Story = {
  args: { modelValue: 'in-progress' },
}

export const Error: Story = {
  args: { error: true },
}

export const Disabled: Story = {
  args: { disabled: true, modelValue: 'todo' },
}
