import type { Meta, StoryObj } from '@storybook/vue3-vite'
import TdDialog from '../components/ui/TdDialog.vue'
import TdButton from '../components/ui/TdButton.vue'

const meta = {
  title: 'UI Primitives/TdDialog',
  component: TdDialog,
  tags: ['autodocs'],
  argTypes: {
    open: { control: 'boolean' },
    title: { control: 'text' },
    description: { control: 'text' },
    closeOnBackdrop: { control: 'boolean' },
  },
  args: {
    open: false,
    title: 'Confirm Action',
    description: 'Are you sure you want to proceed?',
    closeOnBackdrop: true,
  },
} satisfies Meta<typeof TdDialog>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {
  render: (args) => ({
    components: { TdDialog, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div>
        <TdButton @click="args.open = true">Open Dialog</TdButton>
        <TdDialog
          :open="args.open"
          :title="args.title"
          :description="args.description"
          :close-on-backdrop="args.closeOnBackdrop"
          @close="args.open = false"
        >
          <p style="color: var(--td-text-secondary); font-size: var(--td-font-sm);">
            This action cannot be undone.
          </p>
          <template #footer>
            <TdButton variant="ghost" @click="args.open = false">Cancel</TdButton>
            <TdButton variant="primary" @click="args.open = false">Confirm</TdButton>
          </template>
        </TdDialog>
      </div>
    `,
  }),
}

export const DangerDialog: Story = {
  render: (args) => ({
    components: { TdDialog, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div>
        <TdButton variant="danger" @click="args.open = true">Delete Item</TdButton>
        <TdDialog
          :open="args.open"
          :title="args.title"
          :description="args.description"
          :close-on-backdrop="args.closeOnBackdrop"
          @close="args.open = false"
        >
          <template #footer>
            <TdButton variant="ghost" @click="args.open = false">Cancel</TdButton>
            <TdButton variant="danger" @click="args.open = false">Delete</TdButton>
          </template>
        </TdDialog>
      </div>
    `,
  }),
}

export const NoBackdropClose: Story = {
  render: (args) => ({
    components: { TdDialog, TdButton },
    setup() {
      return { args }
    },
    template: `
      <div>
        <TdButton @click="args.open = true">Open (no backdrop close)</TdButton>
        <TdDialog
          :open="args.open"
          :title="args.title"
          :description="args.description"
          :close-on-backdrop="args.closeOnBackdrop"
          @close="args.open = false"
        >
          <p style="color: var(--td-text-secondary); font-size: var(--td-font-sm);">
            You must use the button below to close this dialog.
          </p>
          <template #footer>
            <TdButton variant="primary" @click="args.open = false">Acknowledge</TdButton>
          </template>
        </TdDialog>
      </div>
    `,
  }),
}
