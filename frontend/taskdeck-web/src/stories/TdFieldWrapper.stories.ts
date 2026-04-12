import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdFieldWrapper from '../components/ui/TdFieldWrapper.vue'
import TdInput from '../components/ui/TdInput.vue'

const meta = {
  title: 'UI Primitives/TdFieldWrapper',
  component: TdFieldWrapper,
  tags: ['autodocs'],
  argTypes: {
    label: { control: 'text' },
    error: { control: 'text' },
    hint: { control: 'text' },
    required: { control: 'boolean' },
    fieldId: { control: 'text' },
  },
  args: {
    label: 'Task name',
    error: '',
    hint: '',
    required: false,
    fieldId: 'demo-field',
  },
  render: (args) => ({
    components: { TdFieldWrapper, TdInput },
    setup() {
      return { args }
    },
    template: `
      <TdFieldWrapper v-bind="args" style="max-width: 320px;">
        <TdInput :id="args.fieldId" placeholder="Enter task name..." :error="!!args.error" />
      </TdFieldWrapper>
    `,
  }),
} satisfies Meta<typeof TdFieldWrapper>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const WithHint: Story = {
  args: { hint: 'Use a descriptive name for your task.' },
}

export const WithError: Story = {
  args: { error: 'Task name is required.' },
}

export const Required: Story = {
  args: { required: true },
}

export const FullExample: Story = {
  render: () => ({
    components: { TdFieldWrapper, TdInput },
    template: `
      <div style="display: flex; flex-direction: column; gap: 1rem; max-width: 320px;">
        <TdFieldWrapper label="Name" required field-id="f1" hint="Your full name">
          <TdInput id="f1" placeholder="Jane Doe" />
        </TdFieldWrapper>
        <TdFieldWrapper label="Email" field-id="f2" error="Invalid email address">
          <TdInput id="f2" type="email" model-value="not-an-email" :error="true" />
        </TdFieldWrapper>
      </div>
    `,
  }),
}
