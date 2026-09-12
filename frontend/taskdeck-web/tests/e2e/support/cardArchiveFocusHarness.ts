import { createApp, h, ref } from 'vue'
import { createPinia } from 'pinia'
import CardArchiveAction from '../../../src/components/board/CardArchiveAction.vue'
import { useToastStore } from '../../../src/store/toastStore'
import type { Card } from '../../../src/types/board'

// Real Vue dialog and HTTP action, isolated from route/auth setup so Chromium
// can prove native disabled-element focus behavior with synthetic responses.
const card = ref({
  id: 'focus-card-a', boardId: 'focus-board', columnId: 'focus-column',
  title: 'Archive focus fixture', description: '', labels: [],
  updatedAt: '2026-09-12T10:00:00Z', isArchived: false,
} as unknown as Card)

createApp({
  setup: () => {
    const toast = useToastStore()
    return () => h('main', [
    h(CardArchiveAction, { card: card.value, canWrite: true }),
    h('button', { type: 'button' }, 'Unrelated action'),
    h('button', {
      type: 'button',
      onClick: () => { card.value = { ...card.value, id: 'focus-card-b' } },
    }, 'Switch card'),
    h('output', { 'data-testid': 'write-notice' }, toast.toasts.map(item => item.message).join('\n')),
    ])
  },
}).use(createPinia()).mount('#archive-focus')
