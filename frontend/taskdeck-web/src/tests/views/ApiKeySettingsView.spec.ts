import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, VueWrapper } from '@vue/test-utils'
import ApiKeySettingsView from '../../views/ApiKeySettingsView.vue'
import apiKeysSource from '../../views/ApiKeySettingsView.vue?raw'

const mocks = vi.hoisted(() => ({
  listKeys: vi.fn(),
  createKey: vi.fn(),
  revokeKey: vi.fn(),
}))

vi.mock('../../api/apiKeysApi', () => ({
  apiKeysApi: {
    listKeys: mocks.listKeys,
    createKey: mocks.createKey,
    revokeKey: mocks.revokeKey,
  },
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

vi.mock('../../composables/useEscapeStack', () => ({
  registerEscapeHandler: () => () => {},
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

function bodyText(): string {
  return document.body.textContent ?? ''
}

function findBodyButton(text: string): HTMLButtonElement | undefined {
  return Array.from(document.body.querySelectorAll('button')).find((el) =>
    el.textContent?.includes(text),
  ) as HTMLButtonElement | undefined
}

const activeKey = {
  id: 'key-1',
  keyPrefix: 'tdsk_abc',
  name: 'CI Pipeline',
  createdAt: '2025-06-01T10:00:00Z',
  expiresAt: null,
  revokedAt: null,
  lastUsedAt: '2025-06-05T15:30:00Z',
  isActive: true,
}

const revokedKey = {
  id: 'key-2',
  keyPrefix: 'tdsk_xyz',
  name: 'Old Key',
  createdAt: '2025-01-01T00:00:00Z',
  expiresAt: null,
  revokedAt: '2025-03-01T00:00:00Z',
  lastUsedAt: null,
  isActive: false,
}

const expiredKey = {
  id: 'key-3',
  keyPrefix: 'tdsk_exp',
  name: 'Expired Key',
  createdAt: '2024-01-01T00:00:00Z',
  expiresAt: '2024-06-01T00:00:00Z',
  revokedAt: null,
  lastUsedAt: '2024-05-30T00:00:00Z',
  isActive: false,
}

describe('ApiKeySettingsView', () => {
  let wrapper: VueWrapper

  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    mocks.listKeys.mockResolvedValue([])
  })

  afterEach(() => {
    wrapper?.unmount()
    document.body.innerHTML = ''
  })

  it('shows empty state when no keys exist', async () => {
    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    expect(bodyText()).toContain('No API keys yet')
    expect(bodyText()).toContain('Create an API key to authenticate MCP server requests')
  })

  it('shows loading skeleton initially', () => {
    mocks.listKeys.mockReturnValue(new Promise(() => {})) // never resolves
    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })

    const skeletons = document.body.querySelectorAll('[aria-hidden="true"]')
    expect(skeletons.length).toBeGreaterThanOrEqual(1)
  })

  it('renders with the Paper theme class hooks (not the legacy Obsidian ones)', async () => {
    mocks.listKeys.mockResolvedValue([activeKey])

    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    // The page's own chrome uses the Paper (`paper-api-keys__*`) idiom. The
    // shared `components/ui/Td*` primitives it composes are out of scope and
    // keep their own class hooks.
    expect(wrapper.find('.paper-api-keys').exists()).toBe(true)
    expect(wrapper.find('.paper-api-keys__panel').exists()).toBe(true)
    expect(wrapper.find('.paper-api-keys__card').exists()).toBe(true)
    expect(wrapper.find('[class*="td-settings"]').exists()).toBe(false)
    expect(wrapper.find('[class*="td-key-"]').exists()).toBe(false)
  })

  // #1816 / #1808 review: the mixed-surface residual was recorded only in the
  // PR body, so nothing would notice it changing. This spec pins it: the page
  // deliberately still composes the shared Obsidian-styled `Td*` primitives
  // inside Paper chrome, because none of them has a Paper variant and
  // `PaperHLBtn` has no `:loading` equivalent (swapping TdButton would leave
  // the Create Key button clickable mid-request). When the shared primitives
  // gain a Paper variant, this test is the thing that must be updated -- flip
  // it to assert the absence of `td-btn` / `td-badge`, and drop it.
  it('pins the known mixed-surface residual: shared Td* primitives inside Paper chrome', async () => {
    mocks.listKeys.mockResolvedValue([activeKey])

    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    // Paper chrome around ...
    expect(wrapper.find('.paper-api-keys__panel').exists()).toBe(true)
    // ... Obsidian-styled shared primitives.
    expect(wrapper.find('.td-btn').exists()).toBe(true)
    expect(wrapper.find('.td-badge').exists()).toBe(true)

    // The scope note in the view's style block is the human-readable half of
    // this residual; keep it and the assertion above in step.
    expect(apiKeysSource).toMatch(/components\/ui\/Td\*|shared .*primitive/i)
  })

  it('shows error state with retry button on load failure', async () => {
    mocks.listKeys.mockRejectedValue(new Error('network failure'))

    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    expect(bodyText()).toContain('Failed to load API keys.')
    expect(findBodyButton('Retry')).toBeDefined()
  })

  it('renders active, expired, and revoked keys in separate sections', async () => {
    mocks.listKeys.mockResolvedValue([activeKey, revokedKey, expiredKey])

    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    expect(bodyText()).toContain('CI Pipeline')
    expect(bodyText()).toContain('Active')
    expect(bodyText()).toContain('Old Key')
    expect(bodyText()).toContain('Revoked')
    expect(bodyText()).toContain('Expired Key')
    expect(bodyText()).toContain('Expired')
    expect(bodyText()).toContain('tdsk_abc...')
    expect(bodyText()).toContain('tdsk_xyz...')
    expect(bodyText()).toContain('tdsk_exp...')
  })

  it('shows expired key with Expired badge and expiry date, not Revoked', async () => {
    mocks.listKeys.mockResolvedValue([expiredKey])

    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    expect(bodyText()).toContain('Expired Key')
    expect(bodyText()).toContain('Expired')
    expect(bodyText()).toContain('Expired:')
    expect(bodyText()).toContain('tdsk_exp...')
    // Should NOT show "Revoked" badge or "Revoked:" label for expired keys
    expect(bodyText()).not.toContain('Revoked')
  })

  it('shows key prefix, created date, and last used date for active keys', async () => {
    mocks.listKeys.mockResolvedValue([activeKey])

    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    expect(bodyText()).toContain('Prefix:')
    expect(bodyText()).toContain('Created:')
    expect(bodyText()).toContain('Last used:')
  })

  describe('create key flow', () => {
    it('opens create dialog and creates a key', async () => {
      mocks.listKeys.mockResolvedValue([])
      mocks.createKey.mockResolvedValue({
        id: 'new-key',
        key: 'tdsk_secret_plaintext_value',
        keyPrefix: 'tdsk_sec',
        name: 'New Key',
        createdAt: '2025-06-10T00:00:00Z',
        expiresAt: null,
      })

      wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
      await waitForUi()

      // Click Create API Key in empty state
      const createBtn = findBodyButton('Create API Key')
      expect(createBtn).toBeDefined()
      createBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      // Dialog should now be visible in the body
      expect(bodyText()).toContain('Key Name')

      // Fill in key name
      const input = document.querySelector('#api-key-name') as HTMLInputElement
      expect(input).not.toBeNull()
      input.value = 'New Key'
      input.dispatchEvent(new Event('input', { bubbles: true }))
      await wrapper.vm.$nextTick()

      // Click Create Key in the dialog
      const submitBtn = findBodyButton('Create Key')
      expect(submitBtn).toBeDefined()
      submitBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      expect(mocks.createKey).toHaveBeenCalledWith('New Key')

      // The plaintext key should be shown
      expect(bodyText()).toContain('tdsk_secret_plaintext_value')
      expect(bodyText()).toContain('Copy this key now')
      expect(bodyText()).toContain('will not be shown again')
    })

    it('disables submit when name is empty', async () => {
      mocks.listKeys.mockResolvedValue([activeKey])

      wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
      await waitForUi()

      // Open create dialog
      const createBtn = findBodyButton('Create Key')
      expect(createBtn).toBeDefined()
      createBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      // The dialog Create Key button should be disabled
      const dialogButtons = Array.from(
        document.body.querySelectorAll('button'),
      ).filter((b) => b.textContent?.includes('Create Key'))
      const submitBtn = dialogButtons[dialogButtons.length - 1]
      expect(submitBtn).toBeDefined()
      expect(submitBtn!.disabled).toBe(true)
    })

    it('shows error when create API call fails', async () => {
      mocks.listKeys.mockResolvedValue([activeKey])
      mocks.createKey.mockRejectedValue(new Error('server error'))

      wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
      await waitForUi()

      // Open create dialog
      const createBtn = findBodyButton('Create Key')
      createBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      // Fill name
      const input = document.querySelector('#api-key-name') as HTMLInputElement
      input.value = 'Test Key'
      input.dispatchEvent(new Event('input', { bubbles: true }))
      await wrapper.vm.$nextTick()

      // Submit
      const dialogButtons = Array.from(
        document.body.querySelectorAll('button'),
      ).filter((b) => b.textContent?.includes('Create Key'))
      const submitBtn = dialogButtons[dialogButtons.length - 1]
      submitBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      expect(bodyText()).toContain('Failed to create API key.')
    })

    it('prevents closing create dialog while creation is in-flight', async () => {
      let resolveCreate!: (value: unknown) => void
      mocks.listKeys.mockResolvedValue([activeKey])
      mocks.createKey.mockReturnValue(
        new Promise((resolve) => {
          resolveCreate = resolve
        }),
      )

      wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
      await waitForUi()

      // Open create dialog
      const createBtn = findBodyButton('Create Key')
      createBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      // Fill name
      const input = document.querySelector('#api-key-name') as HTMLInputElement
      input.value = 'In-Flight Key'
      input.dispatchEvent(new Event('input', { bubbles: true }))
      await wrapper.vm.$nextTick()

      // Click Create Key to start the in-flight request
      const dialogButtons = Array.from(
        document.body.querySelectorAll('button'),
      ).filter((b) => b.textContent?.includes('Create Key'))
      const submitBtn = dialogButtons[dialogButtons.length - 1]
      submitBtn!.click()
      await wrapper.vm.$nextTick()

      // Try to close (Cancel button should be disabled, but also test closeCreateDialog guard)
      const cancelBtn = findBodyButton('Cancel')
      expect(cancelBtn).toBeDefined()
      // Cancel button should be disabled during creation
      expect(cancelBtn!.disabled).toBe(true)

      // Dialog should still be open
      expect(bodyText()).toContain('Key Name')

      // Resolve the promise to finish the request
      resolveCreate({
        id: 'new-key',
        key: 'tdsk_inflight_value',
        keyPrefix: 'tdsk_inf',
        name: 'In-Flight Key',
        createdAt: '2025-06-10T00:00:00Z',
        expiresAt: null,
      })
      await wrapper.vm.$nextTick()
      await waitForUi()

      // Now the created key should be displayed
      expect(bodyText()).toContain('tdsk_inflight_value')
    })
  })

  describe('revoke key flow', () => {
    it('opens revoke confirmation dialog and revokes key', async () => {
      mocks.listKeys
        .mockResolvedValueOnce([activeKey])
        .mockResolvedValueOnce([
          { ...activeKey, isActive: false, revokedAt: '2025-06-10T00:00:00Z' },
        ])
      mocks.revokeKey.mockResolvedValue(undefined)

      wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
      await waitForUi()

      // Click Revoke button on the key card
      const revokeBtn = findBodyButton('Revoke')
      expect(revokeBtn).toBeDefined()
      revokeBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      // Confirmation dialog
      expect(bodyText()).toContain('Are you sure you want to revoke')
      expect(bodyText()).toContain('CI Pipeline')
      expect(bodyText()).toContain('cannot be undone')

      // Confirm revoke
      const confirmBtn = findBodyButton('Revoke Key')
      expect(confirmBtn).toBeDefined()
      confirmBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      expect(mocks.revokeKey).toHaveBeenCalledWith('key-1')
      expect(mocks.listKeys).toHaveBeenCalledTimes(2)
    })

    it('shows error when revoke fails', async () => {
      mocks.listKeys.mockResolvedValue([activeKey])
      mocks.revokeKey.mockRejectedValue(new Error('forbidden'))

      wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
      await waitForUi()

      const revokeBtn = findBodyButton('Revoke')
      revokeBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      const confirmBtn = findBodyButton('Revoke Key')
      confirmBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      expect(bodyText()).toContain('Failed to revoke API key.')
    })

    it('cancel closes revoke dialog without revoking', async () => {
      mocks.listKeys.mockResolvedValue([activeKey])

      wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
      await waitForUi()

      const revokeBtn = findBodyButton('Revoke')
      revokeBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      const cancelBtn = findBodyButton('Cancel')
      expect(cancelBtn).toBeDefined()
      cancelBtn!.click()
      await wrapper.vm.$nextTick()
      await waitForUi()

      expect(mocks.revokeKey).not.toHaveBeenCalled()
    })
  })

  it('retries loading on Retry click after error', async () => {
    mocks.listKeys
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce([activeKey])

    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    expect(bodyText()).toContain('Failed to load API keys.')

    const retryBtn = findBodyButton('Retry')
    retryBtn!.click()
    await wrapper.vm.$nextTick()
    await waitForUi()

    expect(mocks.listKeys).toHaveBeenCalledTimes(2)
    expect(bodyText()).toContain('CI Pipeline')
  })

  it('displays MCP description text', async () => {
    wrapper = mount(ApiKeySettingsView, { attachTo: document.body })
    await waitForUi()

    expect(bodyText()).toContain('MCP server HTTP transport authentication')
    expect(bodyText()).toContain('tdsk_')
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
describe('ApiKeySettingsView Legacy-mode substrate', () => {
  it('paints --paper on the root wherever it sets --ink', () => {
    const rule = apiKeysSource.match(/^\.paper-api-keys \{([\s\S]*?)\}/m)?.[1]
    expect(rule, '.paper-api-keys root rule').toBeTruthy()
    // Guard the guard: if the ink declaration were dropped or renamed, the
    // substrate assertion below would otherwise pass vacuously.
    expect(rule).toMatch(/color:\s*var\(--ink,\s*#[0-9a-fA-F]{3,8}\s*\)/)
    expect(rule).toMatch(/background:\s*var\(--paper,\s*#[0-9a-fA-F]{3,8}\s*\)/)
  })
})
