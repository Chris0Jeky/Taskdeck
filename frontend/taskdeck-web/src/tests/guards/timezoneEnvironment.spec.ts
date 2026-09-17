import { describe, expect, it } from 'vitest'
import { findTimezoneStubs, isTimezoneHelperSelfTest } from '../utils/timezoneSourceGuard'
import timezoneEnvironmentSource from './timezoneEnvironment.spec.ts?raw'

// No node:fs dependency in the Vitest/browser type-check project. Source parsing
// ignores instrumentation/layout changes; unlike a facade regex, it reads calls.
// Vite deliberately omits the importing module from an eager glob, so include this
// guard through a query-distinct raw import to ensure the convention also guards itself.
const discoveredSources = import.meta.glob([
  '../**/*.{ts,tsx,js,mjs}',
  '!../guards/timezoneEnvironment.spec.ts',
  '../../../tests/**/*.{ts,tsx,js,mjs}',
  '!../../../tests/e2e/**',
  '!../../../tests/visual/**',
  '!../../../tests/pwa-generated-worker.spec.ts',
], {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>
const sources: Record<string, string> = {
  ...discoveredSources,
  '../guards/timezoneEnvironment.spec.ts': timezoneEnvironmentSource,
}

describe('timezone environment convention (#3013)', () => {
  it('scans test sources, including the helper and ordinary component specs', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(200)
    expect(sources['../utils/timeZone.spec.ts']).toBeTruthy()
    expect(sources['../components/CardModal.spec.ts']).toBeTruthy()
    expect(sources['../guards/timezoneEnvironment.spec.ts']).toBeTruthy()
    expect(sources['../../../tests/demo-run.spec.ts']).toBeTruthy()
    expect(Object.keys(sources).some(path => /\/tests\/(e2e|visual)\//.test(path))).toBe(false)
    expect(sources['../../../tests/pwa-generated-worker.spec.ts']).toBeUndefined()
  })

  it('permits exactly the intentional helper self-test and no other literal TZ stub', () => {
    const calls = Object.entries(sources).flatMap(([path, source]) =>
      findTimezoneStubs(source, path).map(match => ({ path, ...match })))
    const exceptions = calls.filter(match => isTimezoneHelperSelfTest(match.path, match))
    expect(exceptions).toHaveLength(1)
    expect(calls.filter(match => !isTimezoneHelperSelfTest(match.path, match))).toEqual([])
  })

  it.each([
    "vi.stubEnv('TZ', 'UTC')",
    'vi.stubEnv("TZ", "UTC")',
    "vi /* explanation */ . stubEnv (\n 'TZ', 'UTC'\n)",
    "vi['stubEnv']('TZ', 'UTC')",
    'vi.stubEnv(`TZ`, `UTC`)',
    "stubEnv('TZ', 'UTC')",
    "vitest.stubEnv('TZ', 'UTC')",
  ])('recognizes the literal call: %s', source => {
    expect(findTimezoneStubs(source)).toHaveLength(1)
  })

  it.each([
    "// vi.stubEnv('TZ', 'UTC')",
    "/* vi.stubEnv('TZ', 'UTC') */",
    'const example = "vi.stubEnv(\'TZ\', \'UTC\')"',
    "vi.stubEnv('LANG', 'en-US')",
    'const example = `vi.stubEnv("TZ", "UTC")`',
  ])('ignores comments, fixture strings and unrelated environment values: %s', source => {
    expect(findTimezoneStubs(source)).toEqual([])
  })

  it('parses TypeScript generics without treating them as JSX', () => {
    const source = "const identity = <T>(value: T) => value; vi.stubEnv('TZ', 'UTC')"
    expect(findTimezoneStubs(source, 'generic.ts')).toHaveLength(1)
  })

  it('does not exempt another test in the helper file or another file with the same title', () => {
    const title = 'is unaffected by a TZ env stub, in any pool'
    const source = `it('${title}', () => { vi.stubEnv('TZ', 'Pacific/Midway') })`
    const matches = findTimezoneStubs(source)
    expect(matches).toHaveLength(1)
    expect(isTimezoneHelperSelfTest('../utils/timeZone.spec.ts', matches[0]!)).toBe(true)
    expect(isTimezoneHelperSelfTest('../components/Other.spec.ts', matches[0]!)).toBe(false)
    expect(isTimezoneHelperSelfTest('../../../tests/demo-run.spec.ts', matches[0]!)).toBe(false)
    const other = findTimezoneStubs("it('another test', () => vi.stubEnv('TZ', 'Pacific/Midway'))")
    expect(isTimezoneHelperSelfTest('../utils/timeZone.spec.ts', other[0]!)).toBe(false)
  })
})
