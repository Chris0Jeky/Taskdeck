import { describe, expect, it } from 'vitest'
import { hoistWorkerImportScripts } from '../../pwa/hoistWorkerImportScripts'

const SPECIFIERS = ['api-cache-cleanup.js', 'share-target-handler.js']

/**
 * The two shapes vite-plugin-pwa actually emits. The minified one is what a plain `vite build`
 * produces; the unminified one is what workbox emits when it does not run terser (for instance
 * when the build is spawned with `NODE_ENV=test`, which is how `test:pwa-generated-worker` builds).
 * Both put the call inside the asynchronous AMD factory, which is the defect (#2639).
 */
const MINIFIED_WORKER =
  'if(!self.define){let s,e={};const i=(i,l)=>{importScripts(i)};self.define=(l,n)=>{}}' +
  'define(["./workbox-1c53b24d"],function(s){"use strict";' +
  'importScripts("api-cache-cleanup.js","share-target-handler.js"),' +
  'self.addEventListener("message",s=>{}),s.precacheAndRoute([])});'

const UNMINIFIED_WORKER = [
  'if (!self.define) {',
  '  const single = (uri) => { importScripts(uri) };',
  '}',
  "define(['./workbox-f5db42f4'], (function (workbox) { 'use strict';",
  '',
  '  importScripts(',
  '    "api-cache-cleanup.js",',
  '    "share-target-handler.js"',
  '  );',
  '',
  '  self.addEventListener("message", () => {});',
  '}));',
].join('\n')

describe('hoistWorkerImportScripts', () => {
  it('moves the minified call to the very top of the worker', () => {
    const hoisted = hoistWorkerImportScripts(MINIFIED_WORKER, SPECIFIERS)

    expect(hoisted.startsWith('importScripts("api-cache-cleanup.js","share-target-handler.js");\n'))
      .toBe(true)
    // Exactly one call survives: the cleanup script must not be evaluated twice.
    expect(hoisted.match(/importScripts\("api-cache-cleanup\.js"/g)).toHaveLength(1)
    // The shim's own dependency loader is untouched.
    expect(hoisted).toContain('importScripts(i)')
    expect(hoisted.indexOf('define([')).toBeGreaterThan(0)
  })

  it('moves the unminified call to the very top of the worker', () => {
    const hoisted = hoistWorkerImportScripts(UNMINIFIED_WORKER, SPECIFIERS)

    expect(hoisted.startsWith('importScripts("api-cache-cleanup.js","share-target-handler.js");\n'))
      .toBe(true)
    expect(hoisted.match(/api-cache-cleanup\.js/g)).toHaveLength(1)
  })

  it('leaves the rest of the worker parseable after the call is cut out', () => {
    for (const worker of [MINIFIED_WORKER, UNMINIFIED_WORKER]) {
      const hoisted = hoistWorkerImportScripts(worker, SPECIFIERS)
      // A removal that left a dangling comma operand would throw here.
      expect(() => new Function(hoisted)).not.toThrow()
    }
  })

  it('is a no-op once the call already sits at the top', () => {
    const hoisted = hoistWorkerImportScripts(MINIFIED_WORKER, SPECIFIERS)

    expect(hoistWorkerImportScripts(hoisted, SPECIFIERS)).toBe(hoisted)
  })

  it('fails the build when the emitted worker no longer contains the call', () => {
    expect(() => hoistWorkerImportScripts('define([],function(){});', SPECIFIERS))
      .toThrow(/found 0/)
  })

  it('fails the build when the call is emitted more than once', () => {
    expect(() => hoistWorkerImportScripts(MINIFIED_WORKER + MINIFIED_WORKER, SPECIFIERS))
      .toThrow(/found 2/)
  })

  it('fails the build rather than cutting the call out of an expression position', () => {
    const inExpression =
      'const ready = (importScripts("api-cache-cleanup.js","share-target-handler.js"), true);'

    expect(() => hoistWorkerImportScripts(inExpression, SPECIFIERS))
      .toThrow(/not in statement position/)
  })

  it('rejects an empty specifier list instead of rewriting nothing', () => {
    expect(() => hoistWorkerImportScripts(MINIFIED_WORKER, []))
      .toThrow(/no importScripts specifiers/)
  })
})
