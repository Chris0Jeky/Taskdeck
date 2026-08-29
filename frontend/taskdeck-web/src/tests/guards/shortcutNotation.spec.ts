import { describe, expect, it } from 'vitest'

const SOURCES = import.meta.glob('../../**/*.{ts,vue}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

/**
 * These two files are release-lane owned for the v0.3 RC. The runtime PaperHLBtn
 * boundary normalizes PaperInboxView's shipped hint, but this source quarantine
 * stays explicit until the release lane can remove its literals. It may shrink;
 * adding another path would weaken the guard.
 */
const RELEASE_LANE_QUARANTINE = [
  '../../composables/useReviewKeymap.ts',
  '../../views/paper/PaperInboxView.vue',
] as const

const HARDCODED_MODIFIER = /⌘|Ctrl\/Cmd/g

function hardcodedModifierLines(source: string): string[] {
  return source.split('\n')
    .filter((line) => {
      HARDCODED_MODIFIER.lastIndex = 0
      return HARDCODED_MODIFIER.test(line)
    })
    .map((line) => line.trim())
}

describe('shortcut modifier notation', () => {
  it('scans live source with a detector that catches both forbidden forms', () => {
    expect(Object.keys(SOURCES).length).toBeGreaterThan(100)
    expect(hardcodedModifierLines('<kbd>⌘K</kbd>')).toHaveLength(1)
    expect(hardcodedModifierLines('Press Ctrl/Cmd+Enter')).toHaveLength(1)
  })

  it('keeps hardcoded modifier notation inside the bounded release-lane quarantine', () => {
    const offenders = Object.entries(SOURCES)
      // Production entries resolve two levels up (`../../components/...`);
      // sibling specs resolve one level up (`../components/...`).
      .filter(([path]) => path.startsWith('../../'))
      .filter(([path]) => path !== '../../utils/keyboardShortcuts.ts')
      .flatMap(([path, source]) => hardcodedModifierLines(source).map((line) => ({ path, line })))

    expect([...new Set(offenders.map((offender) => offender.path))].sort())
      .toEqual([...RELEASE_LANE_QUARANTINE].sort())
    for (const path of RELEASE_LANE_QUARANTINE) {
      expect(offenders.some((offender) => offender.path === path)).toBe(true)
    }
  })
})
