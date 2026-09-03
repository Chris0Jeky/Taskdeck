import { readFileSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { beforeAll, describe, expect, it } from 'vitest'

type RuntimeMatcher = RegExp

function deserializeRuntimeMatcher(source: string): RuntimeMatcher {
  const closingDelimiter = source.lastIndexOf('/')
  return new RegExp(source.slice(1, closingDelimiter), source.slice(closingDelimiter + 1))
}

function loadGeneratedWorker(): string {
  const projectRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
  return readFileSync(resolve(projectRoot, 'dist', 'sw.js'), 'utf8')
}

function buildWithNestedApiBase(): void {
  const projectRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
  const viteBin = resolve(projectRoot, 'node_modules', 'vite', 'bin', 'vite.js')
  execFileSync(process.execPath, [viteBin, 'build'], {
    cwd: projectRoot,
    env: { ...process.env, VITE_API_BASE_URL: '/assets/api' },
    stdio: 'pipe',
  })
}

function loadGeneratedRuntimeMatchers(): RuntimeMatcher[] {
  const worker = loadGeneratedWorker()
  const sources = [...worker.matchAll(
    /registerRoute\((\/\^https\?:[\s\S]+?\/[a-z]*),\s*new\s+\w+\.(?:StaleWhileRevalidate|CacheFirst)/g,
  )].map((match) => deserializeRuntimeMatcher(match[1]))

  expect(sources).toHaveLength(2)
  return sources
}

describe('generated PWA worker runtime-cache contract', () => {
  beforeAll(() => {
    buildWithNestedApiBase()
  })

  it('does not generate any NetworkFirst strategy or legacy API cache', () => {
    const worker = loadGeneratedWorker()

    expect(worker).not.toMatch(/new\s+[$\w]+\.NetworkFirst\s*\(/)
    expect(worker).not.toContain('taskdeck-api-cache')
  })

  it('serializes self-contained matchers that still reject every API spelling', () => {
    const [localeMatcher, staticMatcher] = loadGeneratedRuntimeMatchers()

    expect(localeMatcher.test('https://taskdeck.example/assets/it-a.js')).toBe(true)
    expect(staticMatcher.test('https://taskdeck.example/assets/avatar.png')).toBe(true)

    for (const url of [
      'https://taskdeck.example/api/assets/it-a.js',
      'https://cdn.example/%61pi/avatar.png',
      'https://taskdeck.example/api%2Favatar.png',
      'https://taskdeck.example//api/avatar.png',
    ]) {
      expect(localeMatcher.test(url)).toBe(false)
      expect(staticMatcher.test(url)).toBe(false)
    }
  })

  it('excludes a prefixed API base that the /api denial cannot see', () => {
    const [, staticMatcher] = loadGeneratedRuntimeMatchers()

    expect(staticMatcher.test('https://taskdeck.example/assets/api/users/by-username/alice.png')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/assets/api/boards/1/cover.svg')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/assets/%61pi/users/by-username/alice.png')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/icons/icon-192x192.png')).toBe(true)
  })
})
