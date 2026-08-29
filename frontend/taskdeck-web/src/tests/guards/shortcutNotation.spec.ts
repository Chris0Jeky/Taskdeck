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

const HARDCODED_MODIFIER = /⌘|Ctrl\/Cmd|Ctrl\+/g

function hardcodedModifierLines(source: string): string[] {
  return source.split('\n')
    .filter((line) => {
      HARDCODED_MODIFIER.lastIndex = 0
      return HARDCODED_MODIFIER.test(line)
    })
    .map((line) => line.trim())
}

/**
 * A bare `Ctrl+` literal is only a user-visible lie where it is RENDERED, so
 * the widened detector runs over the `<template>` block of a `.vue` file with HTML
 * comments stripped. Prose in script comments may still say "Ctrl+K" when it
 * is describing the Windows binding; that is not a display defect.
 */
const VUE_TEMPLATE = /<template>([\s\S]*)<\/template>/
const HTML_COMMENT = /<!--[\s\S]*?-->/g

function renderedModifierLines(path: string, source: string): string[] {
  if (!path.endsWith('.vue')) return hardcodedModifierLines(source)

  const template = VUE_TEMPLATE.exec(source)?.[1] ?? ''
  return hardcodedModifierLines(template.replace(HTML_COMMENT, ''))
}

describe('shortcut modifier notation', () => {
  it('scans live source with a detector that catches both forbidden forms', () => {
    expect(Object.keys(SOURCES).length).toBeGreaterThan(100)
    expect(hardcodedModifierLines('<kbd>⌘K</kbd>')).toHaveLength(1)
    expect(hardcodedModifierLines('Press Ctrl/Cmd+Enter')).toHaveLength(1)
    expect(hardcodedModifierLines('<kbd>Ctrl+K</kbd>')).toHaveLength(1)
    // A rendered `.vue` keycap is an offender; the same literal in a script
    // comment or an HTML comment is not.
    expect(renderedModifierLines(
      '../../components/x.vue',
      '<template><kbd>Ctrl+K</kbd></template>',
    )).toHaveLength(1)
    expect(renderedModifierLines(
      '../../components/x.vue',
      '<script setup lang="ts">// Ctrl+K opens it</script><template><kbd>{{ keys }}</kbd></template>',
    )).toHaveLength(0)
    expect(renderedModifierLines(
      '../../components/x.vue',
      '<template><!-- Ctrl+K opens it --><kbd>{{ keys }}</kbd></template>',
    )).toHaveLength(0)
  })

  it('keeps hardcoded modifier notation inside the bounded release-lane quarantine', () => {
    const offenders = Object.entries(SOURCES)
      // Production entries resolve two levels up (`../../components/...`);
      // sibling specs resolve one level up (`../components/...`).
      .filter(([path]) => path.startsWith('../../'))
      .filter(([path]) => path !== '../../utils/keyboardShortcuts.ts')
      .flatMap(([path, source]) => renderedModifierLines(path, source).map((line) => ({ path, line })))

    expect([...new Set(offenders.map((offender) => offender.path))].sort())
      .toEqual([...RELEASE_LANE_QUARANTINE].sort())
    for (const path of RELEASE_LANE_QUARANTINE) {
      expect(offenders.some((offender) => offender.path === path)).toBe(true)
    }
  })
})
