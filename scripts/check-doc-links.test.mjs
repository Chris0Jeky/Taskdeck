import test from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

import {
  maskCode,
  isExternalTarget,
  extractLocalTargets,
  collectMarkdownFiles,
  findBrokenLinks,
  skippedDirectories,
} from './check-doc-links.mjs'

function withFixture(files, assertions) {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-doc-links-'))
  try {
    for (const [relativePath, contents] of Object.entries(files)) {
      const full = join(root, relativePath)
      mkdirSync(join(full, '..'), { recursive: true })
      writeFileSync(full, contents, 'utf8')
    }
    assertions(root)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
}

test('external and non-path targets are not treated as repository paths', () => {
  for (const target of [
    'https://github.com/Chris0Jeky/Taskdeck',
    'http://localhost:5173',
    'mailto:someone@example.com',
    '#a-heading-on-this-page',
    '//cdn.example.com/x.png',
    '',
  ]) {
    assert.equal(isExternalTarget(target), true, target)
  }
  for (const target of ['docs/STATUS.md', './sibling.md', '../up.md', '/docs/root-relative.md']) {
    assert.equal(isExternalTarget(target), false, target)
  }
})

test('code spans and fenced blocks are masked, and masking preserves offsets', () => {
  const markdown = ['Real [one](a.md).', '', '`![shape](../path/to/x.svg)`', ''].join('\n')
  const masked = maskCode(markdown)
  assert.equal(masked.length, markdown.length)
  assert.equal(masked.split('\n').length, markdown.split('\n').length)
  assert.match(masked, /Real \[one\]\(a\.md\)\./)
  assert.doesNotMatch(masked, /path\/to\/x\.svg/)
})

test('an illustrative link inside backticks is not reported', () => {
  const targets = extractLocalTargets('See `![…](../path/to/context-fabric-lifecycle.svg)` for the shape.')
  assert.deepEqual(targets, [])
})

test('a fenced block does not contribute targets', () => {
  const markdown = ['```md', '[nope](does-not-exist.md)', '```', '', '[yes](real.md)'].join('\n')
  const targets = extractLocalTargets(markdown)
  assert.deepEqual(
    targets.map((t) => t.target),
    ['real.md'],
  )
})

test('anchors and queries are stripped down to the file half', () => {
  const targets = extractLocalTargets('[x](docs/STATUS.md#current-implementation-snapshot)')
  assert.equal(targets.length, 1)
  assert.equal(targets[0].pathPart, 'docs/STATUS.md')
})

test('image links, angle-bracket targets and titles are all recognised', () => {
  const markdown = [
    '![img](assets/a.png)',
    '[spaced](<assets/b with space.png>)',
    '[titled](assets/c.png "A title")',
  ].join('\n')
  assert.deepEqual(
    extractLocalTargets(markdown).map((t) => t.pathPart),
    ['assets/a.png', 'assets/b with space.png', 'assets/c.png'],
  )
})

test('reported line numbers point at the line the link sits on', () => {
  const markdown = ['# Title', '', 'intro', '', '[late](gone.md)'].join('\n')
  const targets = extractLocalTargets(markdown)
  assert.equal(targets.length, 1)
  assert.equal(targets[0].line, 5)
})

test('a missing target is reported and an existing one is not', () => {
  withFixture(
    {
      'docs/index.md': '[good](./real.md) and [bad](./gone.md)\n',
      'docs/real.md': '# real\n',
    },
    (root) => {
      const broken = findBrokenLinks(root)
      assert.equal(broken.length, 1)
      assert.equal(broken[0].file, 'docs/index.md')
      assert.equal(broken[0].target, './gone.md')
      assert.equal(broken[0].reason, 'missing')
    },
  )
})

test('a leading slash resolves against the repository root, not the filesystem root', () => {
  withFixture(
    {
      'docs/deep/page.md': '[root-relative](/docs/target.md)\n',
      'docs/target.md': '# target\n',
    },
    (root) => {
      assert.deepEqual(findBrokenLinks(root), [])
    },
  )
})

test('a percent-encoded target resolves to its decoded path', () => {
  withFixture(
    {
      'docs/index.md': '[encoded](./a%20space.md)\n',
      'docs/a space.md': '# spaced\n',
    },
    (root) => {
      assert.deepEqual(findBrokenLinks(root), [])
    },
  )
})

test('a target that escapes the repository is reported even when it exists on this machine', () => {
  withFixture({ 'docs/index.md': '[escape](../../../../../../etc)\n' }, (root) => {
    const broken = findBrokenLinks(root)
    // Either it does not resolve at all, or it resolves outside the root; both are defects.
    assert.equal(broken.length, 1)
    assert.ok(['missing', 'outside the repository'].includes(broken[0].reason), broken[0].reason)
  })
})

test('skipped directories are not walked', () => {
  assert.ok(skippedDirectories.has('node_modules'))
  assert.ok(skippedDirectories.has('.worktrees'))
  withFixture(
    {
      'keep.md': '# keep\n',
      'node_modules/pkg/readme.md': '[broken](nope.md)\n',
      '.worktrees/parked/notes.md': '[broken](nope.md)\n',
    },
    (root) => {
      assert.deepEqual(
        collectMarkdownFiles(root).map((f) => f.replace(root, '').replace(/[\\/]/g, '/')),
        ['/keep.md'],
      )
      assert.deepEqual(findBrokenLinks(root), [])
    },
  )
})

test('the repository itself has no broken repository-relative links', () => {
  assert.deepEqual(findBrokenLinks(), [])
})
