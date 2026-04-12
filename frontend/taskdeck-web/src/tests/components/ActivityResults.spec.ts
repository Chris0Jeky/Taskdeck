import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive, ref, computed } from 'vue'
import ActivityResults from '../../components/activity/ActivityResults.vue'

const mockAuditStore = reactive({
  loading: false,
  entries: [] as Array<{
    action: string | number
    timestamp: string
    entityType: string
    entityId: string
    userName: string | null
    changes: string | null
  }>,
})

vi.mock('../../store/auditStore', () => ({
  useAuditStore: () => mockAuditStore,
}))

// Mock the virtual list composable to render items directly
vi.mock('../../composables/useVirtualList', () => ({
  useVirtualList: (options: { count: { value: number } }) => ({
    parentRef: ref(null),
    virtualItemEls: ref([]),
    virtualRows: computed(() =>
      Array.from({ length: options.count.value }, (_, i) => ({
        index: i,
        key: i,
        start: i * 100,
        end: (i + 1) * 100,
        size: 100,
      })),
    ),
    totalSize: computed(() => options.count.value * 100),
    translateY: computed(() => 0),
  }),
}))

vi.mock('../../composables/useActivityQuery', () => ({
  formatAction: (action: string | number) => (typeof action === 'string' ? action : `Action${action}`),
  formatTimestamp: (ts: string) => ts,
}))

describe('ActivityResults', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockAuditStore.loading = false
    mockAuditStore.entries = []
  })

  it('shows loading state', () => {
    mockAuditStore.loading = true
    const wrapper = mount(ActivityResults, {
      props: { emptyStateTitle: 'No Activity', emptyStateBody: 'Nothing here.' },
    })
    expect(wrapper.text()).toContain('Loading activity...')
  })

  it('shows empty state with custom title and body', () => {
    const wrapper = mount(ActivityResults, {
      props: { emptyStateTitle: 'No Activity', emptyStateBody: 'Nothing here yet.' },
    })
    expect(wrapper.text()).toContain('No Activity')
    expect(wrapper.text()).toContain('Nothing here yet.')
  })

  it('shows action buttons in empty state', () => {
    const wrapper = mount(ActivityResults, {
      props: { emptyStateTitle: 'No Activity', emptyStateBody: 'Nothing.' },
    })
    expect(wrapper.text()).toContain('Open Review')
    expect(wrapper.text()).toContain('Open Boards')
  })

  it('emits navigate when empty state action buttons are clicked', async () => {
    const wrapper = mount(ActivityResults, {
      props: { emptyStateTitle: 'No Activity', emptyStateBody: 'Nothing.' },
    })
    const reviewBtn = wrapper.findAll('button').find((b) => b.text() === 'Open Review')
    expect(reviewBtn).toBeTruthy()
    await reviewBtn?.trigger('click')
    expect(wrapper.emitted('navigate')?.[0]).toEqual(['/workspace/review'])

    const boardsBtn = wrapper.findAll('button').find((b) => b.text() === 'Open Boards')
    expect(boardsBtn).toBeTruthy()
    await boardsBtn?.trigger('click')
    expect(wrapper.emitted('navigate')?.[1]).toEqual(['/workspace/boards'])
  })

  it('renders timeline entries', () => {
    mockAuditStore.entries = [
      {
        action: 'Created',
        timestamp: '2026-03-15T10:00:00Z',
        entityType: 'Card',
        entityId: 'card-1',
        userName: 'alice',
        changes: null,
      },
      {
        action: 1,
        timestamp: '2026-03-15T11:00:00Z',
        entityType: 'Board',
        entityId: 'board-1',
        userName: 'bob',
        changes: 'Updated name',
      },
    ]
    const wrapper = mount(ActivityResults, {
      props: { emptyStateTitle: 'No Activity', emptyStateBody: 'Nothing.' },
    })
    expect(wrapper.text()).toContain('Created')
    expect(wrapper.text()).toContain('Card - card-1')
    expect(wrapper.text()).toContain('by alice')
    expect(wrapper.text()).toContain('Action1')
    expect(wrapper.text()).toContain('Board - board-1')
    expect(wrapper.text()).toContain('by bob')
    expect(wrapper.text()).toContain('Updated name')
  })

  it('does not show actor when userName is null', () => {
    mockAuditStore.entries = [
      {
        action: 'Archived',
        timestamp: '2026-03-15T10:00:00Z',
        entityType: 'Card',
        entityId: 'card-2',
        userName: null,
        changes: null,
      },
    ]
    const wrapper = mount(ActivityResults, {
      props: { emptyStateTitle: 'No Activity', emptyStateBody: 'Nothing.' },
    })
    expect(wrapper.text()).not.toContain('by')
  })
})
