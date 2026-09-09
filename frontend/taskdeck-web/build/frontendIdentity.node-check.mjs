import { afterEach, test } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import { fingerprintFrontend } from './frontendIdentity.ts'

const fixtures = []
afterEach(() => {
  for (const root of fixtures.splice(0)) {
    assert.equal(dirname(resolve(root)), resolve(tmpdir()))
    assert.ok(basename(root).startsWith('taskdeck-build-identity-'))
    rmSync(root, { recursive: true })
  }
})
test('frontend fingerprint is repeatable and changes with source, public assets and build configuration', () => {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-build-identity-'))
  fixtures.push(root)
  for (const folder of ['src', 'public', 'build']) mkdirSync(join(root, folder))
  for (const name of ['index.html', 'package.json', 'package-lock.json', 'vite.config.ts', 'tsconfig.app.json', 'tsconfig.node.json', 'tsconfig.json', 'postcss.config.js', 'tailwind.config.js']) writeFileSync(join(root, name), name)
  writeFileSync(join(root, 'src/main.ts'), 'initial content')
  const baseline = fingerprintFrontend(root, 'production', { VITE_API_BASE_URL: '/api' })
  assert.match(baseline, /^sha256:[a-f0-9]{64}$/)
  assert.equal(fingerprintFrontend(root, 'production', { VITE_API_BASE_URL: '/api' }), baseline)
  assert.notEqual(fingerprintFrontend(root, 'production', { VITE_API_BASE_URL: '/other-api' }), baseline)
  assert.notEqual(fingerprintFrontend(root, 'production', { VITE_API_BASE_URL: '/api' }, { base: '/Taskdeck/' }), baseline)
  assert.notEqual(fingerprintFrontend(root, 'production', { VITE_API_BASE_URL: '/api' }, { minify: false }), baseline)
  writeFileSync(join(root, 'src/main.ts'), 'changed content')
  const changedSource = fingerprintFrontend(root, 'production', { VITE_API_BASE_URL: '/api' })
  assert.notEqual(changedSource, baseline)
  writeFileSync(join(root, 'public/theme.css'), 'changed theme')
  assert.notEqual(fingerprintFrontend(root, 'production', { VITE_API_BASE_URL: '/api' }), changedSource)
})
