import assert from 'node:assert/strict'
import test from 'node:test'

import { extractLocalTargets } from './check-doc-links.mjs'

test('HTML link extraction does not stop at > inside an earlier quoted attribute', () => {
  assert.deepEqual(
    extractLocalTargets('<a title="1 > 0" href="./guide.md">Guide</a>')
      .map(({ pathPart }) => pathPart),
    ['./guide.md'],
  )
})

test('HTML image extraction does not stop at > inside an earlier single-quoted attribute', () => {
  assert.deepEqual(
    extractLocalTargets("<img title='1 > 0' src='assets/diagram.svg'>")
      .map(({ pathPart }) => pathPart),
    ['assets/diagram.svg'],
  )
})
