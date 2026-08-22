import { beforeEach, describe, expect, it, vi } from 'vitest'
import { versionApi } from '../../api/versionApi'
import {
  ensureProductVersionLoaded,
  resetProductVersionForTests,
  useProductVersion,
} from '../../composables/useProductVersion'

vi.mock('../../api/versionApi', () => ({
  versionApi: {
    getProductVersion: vi.fn(),
  },
}))

// `isDemoMode` is a module constant in production; expose it as a getter so a
// single spec file can exercise both deployments.
const demoMode = { value: false }
vi.mock('../../utils/demoMode', () => ({
  get isDemoMode() {
    return demoMode.value
  },
}))

describe('useProductVersion', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetProductVersionForTests()
    demoMode.value = false
  })

  it('displays the version the source of truth reports, not a literal', async () => {
    // A value no build could ever carry: it can only reach the surface by
    // being read from the backend-reported version.
    vi.mocked(versionApi.getProductVersion).mockResolvedValue('9.99.0-guard')

    const { version, displayVersion, ensureLoaded } = useProductVersion()
    await ensureLoaded()

    expect(version.value).toBe('9.99.0-guard')
    expect(displayVersion.value).toBe('v9.99.0-guard')
  })

  it('does not double-prefix a version that already carries a v', async () => {
    vi.mocked(versionApi.getProductVersion).mockResolvedValue('v0.1.1')

    const { displayVersion, ensureLoaded } = useProductVersion()
    await ensureLoaded()

    expect(displayVersion.value).toBe('v0.1.1')
  })

  it('renders nothing until the version has been established', async () => {
    vi.mocked(versionApi.getProductVersion).mockResolvedValue('0.1.1')

    const { displayVersion, ensureLoaded } = useProductVersion()

    expect(displayVersion.value).toBeNull()

    // Settle the in-flight load so it cannot resolve into the next test.
    await ensureLoaded()
  })

  it('warns when the backend answers but reports no usable version', async () => {
    // Reachable-but-useless is the quiet failure: a proxy serving the SPA's
    // index.html for /health/live resolves fine and yields nothing. The footer
    // is empty either way, so the warning is the only diagnosable signal.
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(versionApi.getProductVersion).mockResolvedValue(null)

    const { displayVersion, ensureLoaded } = useProductVersion()
    await ensureLoaded()

    expect(displayVersion.value).toBeNull()
    expect(warn).toHaveBeenCalledWith(
      expect.stringContaining('/health/live returned no usable version'),
    )

    // Unlike a transport failure, a real answer keeps the memo: a second reader
    // must not re-ask. (Guards the "behaviour otherwise identical" claim.)
    const second = useProductVersion()
    await second.ensureLoaded()
    expect(versionApi.getProductVersion).toHaveBeenCalledTimes(1)

    warn.mockRestore()
  })

  it('stays silent when the version resolves', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(versionApi.getProductVersion).mockResolvedValue('0.1.1')

    const { displayVersion, ensureLoaded } = useProductVersion()
    await ensureLoaded()

    expect(displayVersion.value).toBe('v0.1.1')
    expect(warn).not.toHaveBeenCalled()
    warn.mockRestore()
  })

  it('shows nothing rather than a stale guess when the backend cannot be reached', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(versionApi.getProductVersion).mockRejectedValue(new Error('Network Error'))

    const { version, displayVersion, ensureLoaded } = useProductVersion()
    await ensureLoaded()

    expect(version.value).toBeNull()
    expect(displayVersion.value).toBeNull()
    expect(warn).toHaveBeenCalled()
    warn.mockRestore()
  })

  it('retries after a failed read so a slow-starting backend still resolves', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(versionApi.getProductVersion)
      .mockRejectedValueOnce(new Error('Network Error'))
      .mockResolvedValueOnce('0.1.1')

    await ensureProductVersionLoaded()
    const { displayVersion, ensureLoaded } = useProductVersion()
    await ensureLoaded()

    expect(versionApi.getProductVersion).toHaveBeenCalledTimes(2)
    expect(displayVersion.value).toBe('v0.1.1')
    warn.mockRestore()
  })

  it('costs at most one request no matter how many surfaces read it', async () => {
    vi.mocked(versionApi.getProductVersion).mockResolvedValue('0.1.1')

    const first = useProductVersion()
    const second = useProductVersion()
    await Promise.all([first.ensureLoaded(), second.ensureLoaded()])

    expect(versionApi.getProductVersion).toHaveBeenCalledTimes(1)
    expect(second.displayVersion.value).toBe('v0.1.1')
  })

  it('asks no backend in demo mode, where there is none to ask', async () => {
    demoMode.value = true
    vi.mocked(versionApi.getProductVersion).mockResolvedValue('0.1.1')

    const { displayVersion, ensureLoaded } = useProductVersion()
    await ensureLoaded()

    expect(versionApi.getProductVersion).not.toHaveBeenCalled()
    expect(displayVersion.value).toBeNull()
  })
})
