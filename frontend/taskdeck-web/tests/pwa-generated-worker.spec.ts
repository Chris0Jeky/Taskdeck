import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

type RuntimeMatcher = (context: { url: URL }) => boolean

function loadGeneratedWorker(): string {
  const projectRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
  return readFileSync(resolve(projectRoot, 'dist', 'sw.js'), 'utf8')
}

function loadGeneratedRuntimeMatchers(): RuntimeMatcher[] {
  const worker = loadGeneratedWorker()
  const sources = [...worker.matchAll(
    /registerRoute\((\(\{url:\w+\}\)=>.+?),new \w+\.(?:StaleWhileRevalidate|CacheFirst)/g,
  )].map((match) => match[1])

  expect(sources).toHaveLength(2)
  return sources.map((source) => new Function(`return (${source})`)() as RuntimeMatcher)
}

describe('generated PWA worker runtime-cache contract', () => {
  it('does not generate any NetworkFirst strategy or legacy API cache', () => {
    const worker = loadGeneratedWorker()

    expect(worker).not.toMatch(/new\s+[$\w]+\.NetworkFirst\s*\(/)
    expect(worker).not.toContain('taskdeck-api-cache')
  })

  it('serializes self-contained matchers that still reject every API spelling', () => {
    const [localeMatcher, staticMatcher] = loadGeneratedRuntimeMatchers()

    expect(localeMatcher({ url: new URL('https://taskdeck.example/assets/it-a.js') })).toBe(true)
    expect(staticMatcher({ url: new URL('https://taskdeck.example/assets/avatar.png') })).toBe(true)

    for (const url of [
      'https://taskdeck.example/api/assets/it-a.js',
      'https://cdn.example/%61pi/avatar.png',
      'https://taskdeck.example/api%2Favatar.png',
      'https://taskdeck.example//api/avatar.png',
    ]) {
      expect(localeMatcher({ url: new URL(url) })).toBe(false)
      expect(staticMatcher({ url: new URL(url) })).toBe(false)
    }
  })

  it('excludes a prefixed API base that the /api denial cannot see', () => {
    const [, staticMatcher] = loadGeneratedRuntimeMatchers()

    expect(staticMatcher({
      url: new URL('https://taskdeck.example/taskdeck/api/users/by-username/alice.png'),
    })).toBe(false)
    expect(staticMatcher({ url: new URL('https://taskdeck.example/icons/icon-192x192.png') })).toBe(true)
  })
})
