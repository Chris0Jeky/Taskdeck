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
  formatBrokenLinks,
  existsCaseExact,
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

test('a target that escapes the repository is reported as such on every platform', () => {
  // Containment is now decided before existence, so this reason no longer depends
  // on whether the escaped path happens to exist on the machine running the test.
  // The earlier assertion accepted either 'missing' or 'outside the repository'
  // and so pinned nothing: on Windows the fixture resolved to a non-existent
  // C:\Users\etc and reported 'missing', while on Linux it clamped at / and
  // reached a real /etc.
  withFixture({ 'docs/index.md': '[escape](../../../../../../etc)\n' }, (root) => {
    const broken = findBrokenLinks(root)
    assert.equal(broken.length, 1)
    assert.equal(broken[0].reason, 'outside the repository')
  })
})

test('a target whose case does not match the file on disk is reported', () => {
  // existsSync is case-insensitive on Windows and macOS but not on Linux, and
  // GitHub serves case-sensitively. Without this the check passes locally on a
  // link that returns 404 for every reader — the exact defect class this script
  // exists to catch.
  withFixture(
    {
      'docs/index.md': '[wrong case](./STATUS.md) and [right case](./Status.md)\n',
      'docs/Status.md': '# status\n',
    },
    (root) => {
      const broken = findBrokenLinks(root)
      assert.equal(broken.length, 1)
      assert.equal(broken[0].target, './STATUS.md')
      assert.equal(broken[0].reason, 'wrong case')
    },
  )
})

test('existsCaseExact accepts the real casing and rejects any other', () => {
  withFixture({ 'docs/Nested/Real.md': '# real\n' }, (root) => {
    assert.equal(existsCaseExact(join(root, 'docs', 'Nested', 'Real.md'), root), true)
    assert.equal(existsCaseExact(join(root, 'docs', 'nested', 'Real.md'), root), false)
    assert.equal(existsCaseExact(join(root, 'docs', 'Nested', 'real.md'), root), false)
    assert.equal(existsCaseExact(root, root), true)
  })
})

test('findings render as readable file:line -> target (reason) lines', () => {
  assert.deepEqual(
    formatBrokenLinks([{ file: 'docs/a.md', line: 12, target: './gone.md', reason: 'missing' }]),
    ['docs/a.md:12 -> ./gone.md (missing)'],
  )
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
  // Deliberately a repo-state assertion rather than a fixture: it is what keeps
  // the checker honest about the tree it ships with. It reports through
  // formatBrokenLinks so a failure reads like the CLI's own output instead of a
  // raw object diff. An untracked local .md can trip it — see the note on
  // collectMarkdownFiles.
  const broken = findBrokenLinks()
  const rendered = formatBrokenLinks(broken)
  assert.deepEqual(rendered, [], `broken repository-relative links:\n${rendered.join('\n')}`)
})
