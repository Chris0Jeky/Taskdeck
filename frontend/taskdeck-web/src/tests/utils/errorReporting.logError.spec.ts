import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// The module under test reads `import.meta.env.DEV` at call time, so we
// can toggle behaviour by stubbing the env flag between test cases.

describe('logError / logWarn sanitisation', () => {
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>
  let consoleWarnSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
  })

  afterEach(() => {
    consoleErrorSpy.mockRestore()
    consoleWarnSpy.mockRestore()
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  // -----------------------------------------------------------------------
  // logError
  // -----------------------------------------------------------------------
  describe('logError', () => {
    it('in DEV mode, logs full error object alongside context', async () => {
      vi.stubEnv('DEV', true)
      const { logError } = await import('../../utils/errorReporting')

      const err = new Error('something broke')
      logError('API Error:', err)

      expect(consoleErrorSpy).toHaveBeenCalledOnce()
      // In dev mode the raw Error instance is passed through
      expect(consoleErrorSpy).toHaveBeenCalledWith('API Error:', err)
    })

    it('in PROD mode, logs only the error message string (not the full object)', async () => {
      vi.stubEnv('DEV', false)
      const { logError } = await import('../../utils/errorReporting')

      const err = new Error('something broke')
      logError('API Error:', err)

      expect(consoleErrorSpy).toHaveBeenCalledOnce()
      expect(consoleErrorSpy).toHaveBeenCalledWith('API Error:', 'something broke')
    })

    it('in PROD mode, handles string errors', async () => {
      vi.stubEnv('DEV', false)
      const { logError } = await import('../../utils/errorReporting')

      logError('Oops:', 'raw string error')

      expect(consoleErrorSpy).toHaveBeenCalledWith('Oops:', 'raw string error')
    })

    it('in PROD mode, handles non-Error non-string values with generic message', async () => {
      vi.stubEnv('DEV', false)
      const { logError } = await import('../../utils/errorReporting')

      logError('Unexpected:', { status: 500, body: 'secret' })

      expect(consoleErrorSpy).toHaveBeenCalledWith('Unexpected:', 'An error occurred')
    })

    it('in PROD mode, handles null/undefined errors gracefully', async () => {
      vi.stubEnv('DEV', false)
      const { logError } = await import('../../utils/errorReporting')

      logError('Null error:', null)
      expect(consoleErrorSpy).toHaveBeenCalledWith('Null error:', 'An error occurred')

      logError('Undefined error:', undefined)
      expect(consoleErrorSpy).toHaveBeenCalledWith('Undefined error:', 'An error occurred')
    })

    it('in DEV mode, passes through arbitrary objects for full inspection', async () => {
      vi.stubEnv('DEV', true)
      const { logError } = await import('../../utils/errorReporting')

      const obj = { response: { data: { secret: 'token123' } } }
      logError('Debug:', obj)

      expect(consoleErrorSpy).toHaveBeenCalledWith('Debug:', obj)
    })
  })

  // -----------------------------------------------------------------------
  // logWarn
  // -----------------------------------------------------------------------
  describe('logWarn', () => {
    it('in DEV mode, logs context and all additional arguments', async () => {
      vi.stubEnv('DEV', true)
      const { logWarn } = await import('../../utils/errorReporting')

      logWarn('Token issue', 'detail1', { key: 'val' })

      expect(consoleWarnSpy).toHaveBeenCalledOnce()
      expect(consoleWarnSpy).toHaveBeenCalledWith('Token issue', 'detail1', { key: 'val' })
    })

    it('in PROD mode, logs only the context string (no extra args)', async () => {
      vi.stubEnv('DEV', false)
      const { logWarn } = await import('../../utils/errorReporting')

      logWarn('Token issue', 'should-not-appear', { secret: 'data' })

      expect(consoleWarnSpy).toHaveBeenCalledOnce()
      expect(consoleWarnSpy).toHaveBeenCalledWith('Token issue')
    })

    it('in PROD mode, works with no extra arguments', async () => {
      vi.stubEnv('DEV', false)
      const { logWarn } = await import('../../utils/errorReporting')

      logWarn('Simple warning')

      expect(consoleWarnSpy).toHaveBeenCalledWith('Simple warning')
    })
  })
})
