import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdTag from '../components/ui/TdTag.vue'

const meta = {
  title: 'UI Primitives/TdTag',
  component: TdTag,
  tags: ['autodocs'],
  argTypes: {
    color: { control: 'color' },
    removable: { control: 'boolean' },
  },
  args: {
    color: '',
    removable: false,
  },
  render: (args) => ({
    components: { TdTag },
    setup() {
      return { args }
    },
    template: '<TdTag v-bind="args">feature</TdTag>',
  }),
} satisfies Meta<typeof TdTag>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const CustomColor: Story = {
  args: { color: '#4ade80' },
  render: (args) => ({
    components: { TdTag },
    setup() { return { args } },
    template: '<TdTag v-bind="args">bug-fix</TdTag>',
  }),
}

export const Removable: Story = {
  args: { removable: true },
  render: (args) => ({
    components: { TdTag },
    setup() { return { args } },
    template: '<TdTag v-bind="args">removable</TdTag>',
  }),
}

export const RemovableWithColor: Story = {
  args: { removable: true, color: '#fbbf24' },
  render: (args) => ({
    components: { TdTag },
    setup() { return { args } },
    template: '<TdTag v-bind="args">priority</TdTag>',
  }),
}

export const TagGroup: Story = {
  render: () => ({
    components: { TdTag },
    template: `
      <div style="display: flex; gap: 0.4rem; flex-wrap: wrap;">
        <TdTag>frontend</TdTag>
        <TdTag color="#4ade80">feature</TdTag>
        <TdTag color="#fbbf24">priority</TdTag>
        <TdTag color="#ff4d4d" removable>bug</TdTag>
        <TdTag color="#ffb3ae">design</TdTag>
      </div>
    `,
  }),
}
