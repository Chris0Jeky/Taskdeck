import test from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { resolveTarget, findBrokenLinks, existsCaseExact } from './check-doc-links.mjs'

function fixture(run) {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-link-path-'))
  try {
    for (const name of ['Docs/Nested/Status.md', '..notes/Guide.md', '..notes.md']) {
      const target = join(root, name)
      mkdirSync(join(target, '..'), { recursive: true })
      writeFileSync(target, '# Fixture\n')
    }
    run(root, join(root, 'index.md'))
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
}

test('wrong file case is diagnosed without accepting the target', () => {
  fixture((root, source) => {
    assert.deepEqual(resolveTarget(source, 'Docs/Nested/STATUS.md', root), { reason: 'wrong case' })
    assert.equal(existsCaseExact(join(root, 'Docs/Nested/STATUS.md'), root), false)
    assert.equal(resolveTarget(source, 'Docs/Nested/Status.md', root), null)
  })
})

test('wrong directory case is diagnosed along the whole existing path', () => {
  fixture((root, source) => {
    assert.deepEqual(resolveTarget(source, 'docs/nested/status.md', root), { reason: 'wrong case' })
  })
})

test('a missing leaf below a differently cased directory remains missing', () => {
  fixture((root, source) => {
    assert.deepEqual(resolveTarget(source, 'docs/nested/absent.md', root), { reason: 'missing' })
  })
})

test('two-dot-prefixed filenames and directories are not parent traversal', () => {
  fixture((root, source) => {
    for (const target of ['..notes.md', '..notes/Guide.md', '/..notes/Guide.md', '/%2e%2enotes/Guide.md']) {
      assert.equal(resolveTarget(source, target, root), null, target)
    }
  })
})

test('a true parent traversal is rejected before any existence lookup', () => {
  fixture((root, source) => {
    for (const target of ['..', '../outside.md', '/%2e%2e/outside.md']) {
      assert.deepEqual(resolveTarget(source, target, root), { reason: 'outside the repository' })
    }
  })
})

test('a missing two-dot-prefixed entry is missing rather than outside', () => {
  fixture((root, source) => {
    assert.deepEqual(resolveTarget(source, '..missing.md', root), { reason: 'missing' })
  })
})

test('percent decoding preserves the distinction between case errors and traversal', () => {
  fixture((root, source) => {
    assert.deepEqual(resolveTarget(source, '/%2e%2enotes/guide.md', root), { reason: 'wrong case' })
  })
})

test('a complete scan reports case errors but accepts legitimate two-dot-prefixed names', () => {
  fixture((root, source) => {
    writeFileSync(source, '[case](docs/nested/status.md)\n[valid](..notes/Guide.md)\n[missing](Docs/gone.md)\n')
    const rows = findBrokenLinks(root)
    assert.deepEqual(rows.map(({ line, reason }) => ({ line, reason })), [
      { line: 1, reason: 'wrong case' }, { line: 3, reason: 'missing' },
    ])
  })
})
