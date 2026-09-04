import assert from 'node:assert/strict'
import test from 'node:test'

import { findHexLiterals, stripComments } from './check-paper-color-components.mjs'

test('removes JavaScript, CSS, and HTML comments without losing line positions', () => {
  const source = `// tracking issue #1932
/* another issue #1948 */
<!-- browser note #1955 -->
const paperColor = 'var(--paper)'
`

  assert.equal(findHexLiterals(source).length, 0)
  assert.equal(stripComments(source).split('\n').length, source.split('\n').length)
})

test('keeps real literals in code and markup while ignoring inline comments', () => {
  const source = `// issue #1932
<div style="color: #abc"></div>
const paperColor = '#aabbcc' // issue #1948
`

  assert.deepEqual(
    findHexLiterals(source).map(({ literal, line }) => ({ literal, line })),
    [
      { literal: '#abc', line: 2 },
      { literal: '#aabbcc', line: 3 },
    ],
  )
})

test('does not treat URL-like text as a line-comment delimiter', () => {
  const source = '<img src=https://example.test/palette#abcdef alt="palette">'

  assert.deepEqual(
    findHexLiterals(source).map(({ literal }) => literal),
    ['#abcdef'],
  )
})

test('keeps literals after apostrophes in Vue template prose', () => {
  const source = `<template>
  <p>Don't replace real colors with issue references.</p>
  <div style="color: #abc"></div>
  <!-- tracking issue #1932 -->
</template>
<script setup lang="ts">
// tracking issue #1948
const paperColor = '#aabbcc' // tracking issue #1955
</script>`

  assert.deepEqual(
    findHexLiterals(source, 'frontend/taskdeck-web/src/components/paper/Example.vue')
      .map(({ literal, line }) => ({ literal, line })),
    [
      { literal: '#abc', line: 3 },
      { literal: '#aabbcc', line: 8 },
    ],
  )
})
