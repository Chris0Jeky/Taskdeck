import test from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { performance } from 'node:perf_hooks'

import { findBrokenLinks, findMaskingDiagnostics } from './check-doc-links.mjs'

function withFixture(files, assertions) {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-doc-link-residuals-'))
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

function compact(findings) {
  return findings.map(({ file, line, target, reason }) => ({ file, line, target, reason }))
}

test('a literal less-than expression cannot hide a later HTML link', () => {
  withFixture(
    {
      'docs/index.md': 'The comparison is 2 < 3 <a href="./missing.md">broken</a>.\n',
    },
    (root) => {
      assert.deepEqual(compact(findBrokenLinks(root)), [
        {
          file: 'docs/index.md',
          line: 1,
          target: './missing.md',
          reason: 'missing',
        },
      ])
    },
  )
})

test('HTML attribute names are read only outside other quoted attribute values', () => {
  withFixture(
    {
      'docs/index.md': [
        '<a title="example href=\'./decoy.md\'" href="./actual.md">broken</a>',
        '<img alt="example src=\'./decoy.svg\'" src="./actual.svg">',
      ].join('\n'),
    },
    (root) => {
      assert.deepEqual(
        compact(findBrokenLinks(root)).map(({ line, target, reason }) => ({ line, target, reason })),
        [
          { line: 1, target: './actual.md', reason: 'missing' },
          { line: 2, target: './actual.svg', reason: 'missing' },
        ],
      )
    },
  )
})

test('an unterminated HTML comment warns once and cannot mask links after a blank line', () => {
  withFixture(
    {
      'docs/index.md': ['<!-- unfinished comment', '', '[visible](./missing.md)'].join('\n'),
    },
    (root) => {
      assert.deepEqual(compact(findMaskingDiagnostics(root)), [
        {
          file: 'docs/index.md',
          line: 1,
          target: '<!--',
          reason: 'unterminated HTML comment',
        },
      ])
      assert.deepEqual(compact(findBrokenLinks(root)), [
        {
          file: 'docs/index.md',
          line: 3,
          target: './missing.md',
          reason: 'missing',
        },
      ])
    },
  )
})

test('reference definitions inside blockquote and list containers are checked', () => {
  withFixture(
    {
      'docs/index.md': [
        '> [quoted]: ./quoted-missing.md',
        '- [bulleted]: ./bulleted-missing.md',
        '1. [numbered]: ./numbered-missing.md',
        '> - [nested]: ./nested-missing.md',
      ].join('\n'),
    },
    (root) => {
      assert.deepEqual(
        compact(findBrokenLinks(root)).map(({ line, target, reason }) => ({ line, target, reason })),
        [
          { line: 1, target: './quoted-missing.md', reason: 'missing' },
          { line: 2, target: './bulleted-missing.md', reason: 'missing' },
          { line: 3, target: './numbered-missing.md', reason: 'missing' },
          { line: 4, target: './nested-missing.md', reason: 'missing' },
        ],
      )
    },
  )
})

test(
  'adversarial unmatched delimiter and label input stays within a bounded scan time',
  { timeout: 20_000 },
  () => {
    const increasingBackticks = Array.from(
      { length: 2_000 },
      (_, index) => '`'.repeat(index + 1),
    ).join(' ')
    const unmatchedLabels = '['.repeat(100_000)

    withFixture(
      {
        'docs/index.md': [
          increasingBackticks,
          '',
          unmatchedLabels,
          '',
          '[visible](./missing.md)',
        ].join('\n'),
      },
      (root) => {
        const startedAt = performance.now()
        const broken = findBrokenLinks(root)
        const elapsedMs = performance.now() - startedAt

        assert.deepEqual(
          compact(broken).map(({ line, target, reason }) => ({ line, target, reason })),
          [{ line: 5, target: './missing.md', reason: 'missing' }],
        )
        assert.ok(
          elapsedMs < 4_000,
          `expected the adversarial scan to finish within 4000ms, took ${Math.round(elapsedMs)}ms`,
        )
      },
    )
  },
)
