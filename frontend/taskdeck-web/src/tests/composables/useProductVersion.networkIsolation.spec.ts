import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { versionApi } from '../../api/versionApi'
import {
  resetProductVersionForTests,
  useProductVersion,
} from '../../composables/useProductVersion'

describe('useProductVersion default unit fixture', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetProductVersionForTests()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('keeps the default version read off the live HTTP client', async () => {
    const httpGet = vi.spyOn(http, 'get')

    const { displayVersion, ensureLoaded } = useProductVersion()
    await ensureLoaded()

    expect(versionApi.getProductVersion).toHaveBeenCalledTimes(1)
    expect(displayVersion.value).toBeNull()
    expect(httpGet).not.toHaveBeenCalled()
  })
})
