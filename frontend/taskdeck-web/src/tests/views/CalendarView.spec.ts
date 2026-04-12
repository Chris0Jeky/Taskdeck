import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import CalendarView from '../../views/CalendarView.vue'
import type { CalendarData } from '../../types/workspace'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

// Freeze time to April 2026 to match mock data
const MOCK_DATE = new Date('2026-04-05T12:00:00Z')

const mockCalendarData: CalendarData = {
  from: '2026-04-01T00:00:00Z',
  to: '2026-05-01T00:00:00Z',
  totalCards: 3,
  cards: [
    {
      cardId: 'card-1',
      boardId: 'board-1',
      boardName: 'Alpha Board',
      columnId: 'col-1',
      columnName: 'In Progress',
      title: 'Ship feature X',
      dueDate: '2026-04-10T00:00:00Z',
      isBlocked: false,
      blockReason: null,
      isOverdue: false,
      updatedAt: '2026-04-05T12:00:00Z',
    },
    {
      cardId: 'card-2',
      boardId: 'board-2',
      boardName: 'Beta Board',
      columnId: 'col-2',
      columnName: 'Todo',
      title: 'Fix urgent bug',
      dueDate: '2026-04-03T00:00:00Z',
      isBlocked: false,
      blockReason: null,
      isOverdue: true,
      updatedAt: '2026-04-02T12:00:00Z',
    },
    {
      cardId: 'card-3',
      boardId: 'board-1',
      boardName: 'Alpha Board',
      columnId: 'col-1',
      columnName: 'In Progress',
      title: 'Blocked task',
      dueDate: '2026-04-15T00:00:00Z',
      isBlocked: true,
      blockReason: 'Waiting on API key',
      isOverdue: false,
      updatedAt: '2026-04-05T12:00:00Z',
    },
  ],
}

const mockGetCalendar = vi.fn<(from: string, to: string) => Promise<CalendarData>>()

vi.mock('../../api/workspaceApi', () => ({
  workspaceApi: {
    getCalendar: (...args: [string, string]) => mockGetCalendar(...args),
  },
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
}))

vi.mock('../../components/workspace/WorkspaceHelpCallout.vue', () => ({
  default: {
    template: '<div data-testid="workspace-help-callout" />',
    props: ['topic', 'title', 'description'],
  },
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

describe('CalendarView', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(MOCK_DATE)
    vi.clearAllMocks()
    mockGetCalendar.mockResolvedValue(mockCalendarData)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('loads calendar data on mount', async () => {
    mount(CalendarView)
    await waitForUi()

    expect(mockGetCalendar).toHaveBeenCalledTimes(1)
  })

  it('renders the page title and hero description', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    expect(wrapper.text()).toContain('Calendar')
    expect(wrapper.text()).toContain('Planning')
    expect(wrapper.text()).toContain('due-date-backed work')
  })

  it('shows loading state while fetching', async () => {
    // Never resolve
    mockGetCalendar.mockReturnValue(new Promise(() => {}))
    const wrapper = mount(CalendarView)
    await waitForUi()

    expect(wrapper.text()).toContain('Loading calendar data')
  })

  it('shows error state when fetch fails', async () => {
    mockGetCalendar.mockRejectedValue(new Error('Network error'))
    const wrapper = mount(CalendarView)
    await waitForUi()

    expect(wrapper.text()).toContain('Network error')
  })

  it('shows error state with retry button', async () => {
    mockGetCalendar.mockRejectedValue(new Error('Server down'))
    const wrapper = mount(CalendarView)
    await waitForUi()

    const retryBtn = wrapper.find('.td-btn--ghost.td-btn--sm')
    expect(retryBtn.exists()).toBe(true)
    expect(retryBtn.text()).toContain('Retry')
  })

  it('shows empty state when no cards have due dates', async () => {
    mockGetCalendar.mockResolvedValue({
      from: '2026-04-01T00:00:00Z',
      to: '2026-05-01T00:00:00Z',
      totalCards: 0,
      cards: [],
    })
    const wrapper = mount(CalendarView)
    await waitForUi()

    expect(wrapper.text()).toContain('No due dates this month')
  })

  it('renders card count for the month', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    expect(wrapper.text()).toContain('3 cards this month')
  })

  it('renders calendar grid with weekday headers', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const weekdays = wrapper.findAll('.td-calendar__weekday')
    expect(weekdays).toHaveLength(7)
    expect(weekdays[0].text()).toBe('Sun')
    expect(weekdays[6].text()).toBe('Sat')
  })

  it('renders cards in the calendar grid', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const cardElements = wrapper.findAll('.td-cal-card')
    expect(cardElements.length).toBeGreaterThan(0)
    expect(wrapper.text()).toContain('Ship feature X')
  })

  it('applies overdue class to overdue cards', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const overdueCards = wrapper.findAll('.td-cal-card--overdue')
    expect(overdueCards.length).toBeGreaterThan(0)
  })

  it('applies blocked class to blocked cards', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const blockedCards = wrapper.findAll('.td-cal-card--blocked')
    expect(blockedCards.length).toBeGreaterThan(0)
  })

  it('navigates to board on card click in calendar grid', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const card = wrapper.find('.td-cal-card')
    await card.trigger('click')

    expect(routerMocks.push).toHaveBeenCalled()
    const pushedPath = routerMocks.push.mock.calls[0][0] as string
    expect(pushedPath).toMatch(/\/workspace\/boards\/board-/)
  })

  it('switches to timeline view', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const buttons = wrapper.findAll('.td-calendar__hero-actions .td-btn')
    const timelineBtn = buttons.find(b => b.text() === 'Timeline')
    expect(timelineBtn).toBeDefined()
    await timelineBtn!.trigger('click')

    expect(wrapper.findAll('.td-timeline-group').length).toBeGreaterThan(0)
  })

  it('renders timeline cards with status indicators', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    // Switch to timeline
    const buttons = wrapper.findAll('.td-calendar__hero-actions .td-btn')
    const timelineBtn = buttons.find(b => b.text() === 'Timeline')
    await timelineBtn!.trigger('click')

    expect(wrapper.text()).toContain('Ship feature X')
    expect(wrapper.text()).toContain('Fix urgent bug')
    expect(wrapper.text()).toContain('Blocked task')
    expect(wrapper.text()).toContain('Overdue')
    expect(wrapper.text()).toContain('Blocked')
  })

  it('shows block reason in timeline view for blocked cards', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const buttons = wrapper.findAll('.td-calendar__hero-actions .td-btn')
    const timelineBtn = buttons.find(b => b.text() === 'Timeline')
    await timelineBtn!.trigger('click')

    expect(wrapper.text()).toContain('Waiting on API key')
  })

  it('renders month navigation', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const nav = wrapper.find('.td-calendar__nav')
    expect(nav.exists()).toBe(true)
    expect(nav.text()).toContain('Today')
  })

  it('navigates to next month on arrow click', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const initialCallCount = mockGetCalendar.mock.calls.length

    const nextBtn = wrapper.findAll('.td-calendar__nav .td-btn--ghost')[1]
    await nextBtn.trigger('click')
    await waitForUi()

    // Should have fetched again
    expect(mockGetCalendar.mock.calls.length).toBeGreaterThan(initialCallCount)
  })

  it('navigates to previous month on arrow click', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const initialCallCount = mockGetCalendar.mock.calls.length

    const prevBtn = wrapper.findAll('.td-calendar__nav .td-btn--ghost')[0]
    await prevBtn.trigger('click')
    await waitForUi()

    expect(mockGetCalendar.mock.calls.length).toBeGreaterThan(initialCallCount)
  })

  it('navigates to board from timeline card click', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const buttons = wrapper.findAll('.td-calendar__hero-actions .td-btn')
    const timelineBtn = buttons.find(b => b.text() === 'Timeline')
    await timelineBtn!.trigger('click')

    const card = wrapper.find('.td-timeline-card')
    await card.trigger('click')

    expect(routerMocks.push).toHaveBeenCalled()
  })

  it('shows board and column names in timeline meta', async () => {
    const wrapper = mount(CalendarView)
    await waitForUi()

    const buttons = wrapper.findAll('.td-calendar__hero-actions .td-btn')
    const timelineBtn = buttons.find(b => b.text() === 'Timeline')
    await timelineBtn!.trigger('click')

    expect(wrapper.text()).toContain('Alpha Board')
    expect(wrapper.text()).toContain('In Progress')
  })
})
