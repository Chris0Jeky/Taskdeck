import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'

const requiredWorkflowUrl = new URL('../../../.github/workflows/ci-required.yml', import.meta.url)

// An in-progress push run on the default branch must not be cancelled by the next merge:
// the run in flight completes and the newest tip runs next (GitHub keeps one in-progress
// plus one pending run per group, so intermediate pending main runs are still superseded).
// The tip's own required run is the landed-commit evidence (#2582).
const EXPECTED_GROUP = '${{ github.workflow }}-${{ github.ref }}'
const EXPECTED_CANCEL_IN_PROGRESS = "${{ github.ref != 'refs/heads/main' }}"

function readLines(workflow) {
  return workflow.replaceAll('\r\n', '\n').split('\n')
}

function indentOf(line) {
  return line.search(/\S/)
}

// Line scan: every `concurrency:` mapping key, with its indentation and the
// scalar entries nested directly beneath it.
function collectConcurrencyBlocks(lines) {
  const blocks = []
  lines.forEach((line, index) => {
    const header = line.match(/^(\s*)concurrency:\s*$/)
    if (!header) return

    const indent = header[1].length
    const entries = new Map()
    for (const next of lines.slice(index + 1)) {
      if (next.trim() === '') continue
      if (indentOf(next) <= indent) break
      if (next.trimStart().startsWith('#')) continue
      const entry = next.match(/^\s*([A-Za-z0-9_-]+):\s*(.*?)\s*$/)
      if (entry) entries.set(entry[1], entry[2])
    }
    blocks.push({ lineNumber: index + 1, indent, entries })
  })
  return blocks
}

test('ci-required declares exactly one top-level concurrency block', async () => {
  const lines = readLines(await readFile(requiredWorkflowUrl, 'utf8'))
  const topLevel = collectConcurrencyBlocks(lines).filter((block) => block.indent === 0)

  assert.equal(
    topLevel.length,
    1,
    `expected one top-level concurrency block in ci-required.yml, found ${topLevel.length}`,
  )
})

test('the top-level concurrency group stays per-workflow-and-ref', async () => {
  const lines = readLines(await readFile(requiredWorkflowUrl, 'utf8'))
  const [topLevel] = collectConcurrencyBlocks(lines).filter((block) => block.indent === 0)

  assert.equal(
    topLevel.entries.get('group'),
    EXPECTED_GROUP,
    'ci-required.yml must keep one concurrency group per ref so at most one main run is in flight at a time (#2582)',
  )
})

test('in-progress cancellation is disabled on the default branch', async () => {
  const lines = readLines(await readFile(requiredWorkflowUrl, 'utf8'))
  const [topLevel] = collectConcurrencyBlocks(lines).filter((block) => block.indent === 0)

  assert.equal(
    topLevel.entries.get('cancel-in-progress'),
    EXPECTED_CANCEL_IN_PROGRESS,
    'ci-required.yml must not cancel an in-progress run on refs/heads/main (#2582)',
  )
})

test('no job-level concurrency block re-enables cancellation on main', async () => {
  const lines = readLines(await readFile(requiredWorkflowUrl, 'utf8'))
  const nested = collectConcurrencyBlocks(lines).filter((block) => block.indent > 0)

  for (const block of nested) {
    const cancel = block.entries.get('cancel-in-progress')
    if (cancel === undefined) continue
    assert.ok(
      cancel === 'false' || cancel === EXPECTED_CANCEL_IN_PROGRESS,
      `job-level concurrency at ci-required.yml line ${block.lineNumber} sets cancel-in-progress: ${cancel}, which can cancel a main run (#2582)`,
    )
  }
})
