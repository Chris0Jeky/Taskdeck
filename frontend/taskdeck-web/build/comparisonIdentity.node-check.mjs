import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { comparisonChecksumFallback } from '../src/utils/comparisonIdentity.ts'

test('portable comparison checksum matches platform SHA-256 across block boundaries and Unicode', () => {
  const texts = ['', 'abc', 'Uncertainty — 答案 🪴', '\ud800', ...Array.from({ length: 132 }, (_, length) => 'x'.repeat(length)), 'long input'.repeat(800), 'a'.repeat(1_000_000)]
  for (const text of texts) assert.equal(comparisonChecksumFallback(text), createHash('sha256').update(text).digest('hex'), `UTF-16 length ${text.length}`)
  assert.equal(comparisonChecksumFallback('abc'), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')
})
