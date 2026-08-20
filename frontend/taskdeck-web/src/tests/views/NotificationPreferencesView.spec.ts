import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import NotificationPreferencesView from '../../views/NotificationPreferencesView.vue'
import notificationPrefsSource from '../../views/NotificationPreferencesView.vue?raw'

const mockNotificationStore = reactive({
  preferences: {
    userId: 'user-1',
    inAppChannelEnabled: true,
    mentionImmediateEnabled: true,
    mentionDigestEnabled: false,
    assignmentImmediateEnabled: true,
    assignmentDigestEnabled: false,
    proposalOutcomeImmediateEnabled: true,
    proposalOutcomeDigestEnabled: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  },
  loading: false,
  error: null as string | null,
  fetchPreferences: vi.fn<() => Promise<void>>(),
  updatePreferences: vi.fn<(payload: Record<string, boolean>) => Promise<void>>(),
})

vi.mock('../../store/notificationStore', () => ({
  useNotificationStore: () => mockNotificationStore,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('NotificationPreferencesView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockNotificationStore.fetchPreferences.mockResolvedValue(undefined)
    mockNotificationStore.updatePreferences.mockResolvedValue(undefined)
    mockNotificationStore.preferences = {
      userId: 'user-1',
      inAppChannelEnabled: true,
      mentionImmediateEnabled: true,
      mentionDigestEnabled: false,
      assignmentImmediateEnabled: true,
      assignmentDigestEnabled: false,
      proposalOutcomeImmediateEnabled: true,
      proposalOutcomeDigestEnabled: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
  })

  it('loads preferences on mount', async () => {
    mount(NotificationPreferencesView)
    await waitForUi()

    expect(mockNotificationStore.fetchPreferences).toHaveBeenCalledTimes(1)
  })

  // #1816: the no-legacy-hooks guard from #1808 landed in only 4 of the 6
  // restyled Settings specs; this is NotificationPreferences' copy.
  it('renders with the Paper theme class hooks (not the legacy Obsidian ones)', async () => {
    const wrapper = mount(NotificationPreferencesView)
    await waitForUi()

    expect(wrapper.find('.paper-prefs').exists()).toBe(true)
    expect(wrapper.find('.paper-prefs__hero').exists()).toBe(true)
    expect(wrapper.find('.paper-prefs__panel').exists()).toBe(true)
    // The toggle rows are the bulk of the restyled markup; pin one so the
    // negative assertions below cannot pass on an unrendered form.
    expect(wrapper.find('.paper-prefs__toggle-row').exists()).toBe(true)

    expect(wrapper.find('[class*="td-notification-preferences"]').exists()).toBe(false)
    expect(wrapper.find('[class*="td-toggle-row"]').exists()).toBe(false)
    expect(wrapper.find('[class*="td-panel"]').exists()).toBe(false)
    expect(wrapper.find('[class*="td-btn"]').exists()).toBe(false)
  })

  it('submits updated preference values', async () => {
    const wrapper = mount(NotificationPreferencesView)
    await waitForUi()

    const toggles = wrapper.findAll('input[type="checkbox"]')
    await toggles[1].setValue(false)
    await toggles[2].setValue(true)

    await wrapper.get('form').trigger('submit.prevent')

    expect(mockNotificationStore.updatePreferences).toHaveBeenCalledWith(expect.objectContaining({
      mentionImmediateEnabled: false,
      mentionDigestEnabled: true,
    }))
  })
})

// ── #1808 review (MEDIUM): Legacy ("off") mode substrate guard ──
// Paper tokens exist only under `.paper` / `.paper-night` (paper-tokens.css), so
// in Legacy mode this view's `color: var(--ink, …)` resolves to the near-black
// literal while AppShell's `.td-content` still paints `--td-surface-base`
// (#131313) — ~1.05:1 on the hero. A root that sets the Paper ink MUST therefore
// also paint the Paper substrate; that is a no-op under `.paper`/`.paper-night`.
// Source is read through Vite's `?raw` rather than `node:fs` because
// `tsconfig.vitest.json` deliberately omits the "node" types.
// #1815 tracks unifying these per-view assertions into one wave-wide spec.
describe('NotificationPreferencesView Legacy-mode substrate', () => {
  it('paints --paper on the root wherever it sets --ink', () => {
    const rule = notificationPrefsSource.match(/^\.paper-prefs \{([\s\S]*?)\}/m)?.[1]
    expect(rule, '.paper-prefs root rule').toBeTruthy()
    // Guard the guard: if the ink declaration were dropped or renamed, the
    // substrate assertion below would otherwise pass vacuously.
    expect(rule).toMatch(/color:\s*var\(--ink,\s*#[0-9a-fA-F]{3,8}\s*\)/)
    expect(rule).toMatch(/background:\s*var\(--paper,\s*#[0-9a-fA-F]{3,8}\s*\)/)
  })
})
