import type { Meta, StoryObj } from '@storybook/vue3-vite'
import { ref } from 'vue'
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
  render: () => ({
    components: { TdDialog, TdButton },
    setup() {
      const isOpen = ref(false)
      return { isOpen }
    },
    template: `
      <div>
        <TdButton @click="isOpen = true">Open Dialog</TdButton>
        <TdDialog
          :open="isOpen"
          title="Confirm Action"
          description="Are you sure you want to proceed with this action?"
          @close="isOpen = false"
        >
          <p style="color: var(--td-text-secondary); font-size: var(--td-font-sm);">
            This action cannot be undone.
          </p>
          <template #footer>
            <TdButton variant="ghost" @click="isOpen = false">Cancel</TdButton>
            <TdButton variant="primary" @click="isOpen = false">Confirm</TdButton>
          </template>
        </TdDialog>
      </div>
    `,
  }),
}

export const DangerDialog: Story = {
  render: () => ({
    components: { TdDialog, TdButton },
    setup() {
      const isOpen = ref(false)
      return { isOpen }
    },
    template: `
      <div>
        <TdButton variant="danger" @click="isOpen = true">Delete Item</TdButton>
        <TdDialog
          :open="isOpen"
          title="Delete Task"
          description="This will permanently remove the task and all associated data."
          @close="isOpen = false"
        >
          <template #footer>
            <TdButton variant="ghost" @click="isOpen = false">Cancel</TdButton>
            <TdButton variant="danger" @click="isOpen = false">Delete</TdButton>
          </template>
        </TdDialog>
      </div>
    `,
  }),
}

export const NoBackdropClose: Story = {
  render: () => ({
    components: { TdDialog, TdButton },
    setup() {
      const isOpen = ref(false)
      return { isOpen }
    },
    template: `
      <div>
        <TdButton @click="isOpen = true">Open (no backdrop close)</TdButton>
        <TdDialog
          :open="isOpen"
          title="Important Notice"
          :close-on-backdrop="false"
          @close="isOpen = false"
        >
          <p style="color: var(--td-text-secondary); font-size: var(--td-font-sm);">
            You must use the button below to close this dialog.
          </p>
          <template #footer>
            <TdButton variant="primary" @click="isOpen = false">Acknowledge</TdButton>
          </template>
        </TdDialog>
      </div>
    `,
  }),
}
