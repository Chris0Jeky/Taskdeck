import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { performance } from 'node:perf_hooks'
import test from 'node:test'

import {
  extractLocalTargets,
  findBrokenLinks,
  findMaskingDiagnostics,
} from './check-doc-links.mjs'

function withFixture(files, assertions) {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-doc-links-residuals-'))
  try {
    for (const [relativePath, contents] of Object.entries(files)) {
      const fullPath = join(root, relativePath)
      mkdirSync(join(fullPath, '..'), { recursive: true })
      writeFileSync(fullPath, contents, 'utf8')
    }
    assertions(root)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
}

test('a literal less-than expression cannot hide a later valid HTML link', () => {
  withFixture(
    {
      'docs/index.md': 'Comparison: 2 < 3 <a href="./gone.md">missing guide</a>\n',
    },
    (root) => {
      assert.deepEqual(
        findBrokenLinks(root).map(({ line, target, reason }) => ({ line, target, reason })),
        [{ line: 1, target: './gone.md', reason: 'missing' }],
      )
    },
  )
})

test('attribute-like text inside a quoted HTML attribute is not parsed as href', () => {
  withFixture(
    {
      'docs/index.md': '<a title="example href=\'./ghost.md\'" href="./real.md">real guide</a>\n',
      'docs/real.md': '# Real\n',
    },
    (root) => {
      assert.deepEqual(findBrokenLinks(root), [])
    },
  )
})

test('an unterminated HTML comment is diagnosed without masking a later paragraph', () => {
  withFixture(
    {
      'docs/index.md': ['<!-- unfinished comment', '', '[bad](./gone.md)'].join('\n'),
    },
    (root) => {
      assert.deepEqual(
        findMaskingDiagnostics(root).map(({ line, target, reason }) => ({ line, target, reason })),
        [{ line: 1, target: '<!--', reason: 'unterminated HTML comment' }],
      )
      assert.deepEqual(
        findBrokenLinks(root).map(({ line, target, reason }) => ({ line, target, reason })),
        [{ line: 3, target: './gone.md', reason: 'missing' }],
      )
    },
  )
})

test('reference definitions inside blockquote and list containers are collected', () => {
  withFixture(
    {
      'docs/index.md': [
        '> [quoted-ref]: ./quoted.md',
        '- [listed-ref]: ./listed.md',
        '1. [ordered-ref]: ./ordered.md',
        '> - [nested-ref]: ./nested.md',
      ].join('\n'),
    },
    (root) => {
      assert.deepEqual(
        findBrokenLinks(root).map(({ line, target, reason }) => ({ line, target, reason })),
        [
          { line: 1, target: './quoted.md', reason: 'missing' },
          { line: 2, target: './listed.md', reason: 'missing' },
          { line: 3, target: './ordered.md', reason: 'missing' },
          { line: 4, target: './nested.md', reason: 'missing' },
        ],
      )
    },
  )
})

test('adversarial unmatched delimiters remain within a bounded parsing budget', { timeout: 10_000 }, () => {
  const increasingBacktickRuns = Array.from(
    { length: 1_800 },
    (_, index) => '`'.repeat(index + 1),
  ).join(' ')
  const unmatchedLabels = '['.repeat(20_000)
  const markdown = `${increasingBacktickRuns}\n\n${unmatchedLabels}`

  const startedAt = performance.now()
  const targets = extractLocalTargets(markdown)
  const elapsedMs = performance.now() - startedAt

  assert.deepEqual(targets, [])
  assert.ok(
    elapsedMs < 5_000,
    `adversarial delimiter parsing took ${elapsedMs.toFixed(1)}ms; expected under 5000ms`,
  )
})
