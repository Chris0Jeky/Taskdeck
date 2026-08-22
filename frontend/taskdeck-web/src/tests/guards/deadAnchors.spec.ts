import { describe, expect, it } from 'vitest'

/**
 * Dead-anchor guard (#1941, dead-affordance class #1932 / #1934 / #1941).
 *
 * `<a href="#">` with nothing bound to it is a control that looks interactive
 * and does nothing. It shipped three times in the Paper skin before the
 * 2026-08-22 dogfooding pass caught it by hand, so the rule is mechanical from
 * here: an anchor whose href is the bare `#` placeholder must carry a click
 * binding, otherwise it must not be an anchor at all.
 *
 * Scope and limits, stated honestly:
 *  - It reads SFC source, so it sees the placeholder `#` that is written into
 *    the template. A dead href assembled at runtime is out of reach here.
 *  - It proves a click binding EXISTS, not that the handler calls
 *    `preventDefault()` — the `#` navigation itself is a component test's job.
 *  - The fix for a violation is almost never "add a handler to satisfy the
 *    guard". It is: give the control a real destination, or render it as
 *    non-interactive text (#1949).
 *
 * Sources come from `import.meta.glob`, not `node:fs`, on purpose:
 * `tsconfig.vitest.json` deliberately keeps node types out of the spec
 * type-check project, and its quarantine list may only shrink.
 */

/** Every SFC under `src/`, as raw source keyed by path relative to this file. */
const VUE_SOURCES = import.meta.glob('../../**/*.vue', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

/** Opening `<a …>` tags. Quoted attribute values are consumed whole so a `>` inside one cannot end the tag early. */
const ANCHOR_TAG = /<a(?=[\s>/])(?:"[^"]*"|'[^']*'|[^>'"])*>/g

/** The bare `#` placeholder href, static or bound, in either quote style. */
const PLACEHOLDER_HREF = /(?::|v-bind:)?href\s*=\s*(?:"#"|'#'|"'#'"|'"#"')/

/** Any click binding: `@click`, `@click.prevent`, `v-on:click`, `@[dynamic]` is not matched on purpose. */
const CLICK_BINDING = /(?:@|v-on:)click(?:\.[\w.]+)?\s*=/

/**
 * Only markup is scanned. `<script>` and `<style>` blocks and HTML comments are
 * dropped first so that PROSE about a dead anchor — the doc comment in
 * `ReviewWhyNow.vue` explaining why the link was removed — is not itself
 * reported as one.
 */
function markupOnly(source: string): string {
  return source
    .replace(/<script[\s\S]*?<\/script>/gi, '')
    .replace(/<style[\s\S]*?<\/style>/gi, '')
    .replace(/<!--[\s\S]*?-->/g, '')
}

/** Opening anchor tags in `source` whose href is the bare `#` and which bind no click. */
function findDeadAnchors(source: string): string[] {
  const dead: string[] = []
  for (const [tag] of markupOnly(source).matchAll(ANCHOR_TAG)) {
    if (!PLACEHOLDER_HREF.test(tag)) continue
    if (CLICK_BINDING.test(tag)) continue
    dead.push(tag.replace(/\s+/g, ' '))
  }
  return dead
}

describe('dead anchors', () => {
  const vueFiles = Object.keys(VUE_SOURCES)

  it('finds Vue components to scan', () => {
    // A guard that silently scans nothing passes forever. This is the canary:
    // real files, with real source in them, including the one component known
    // to use a placeholder href legitimately (#1941 AC 2 — the sidebar's
    // Shortcuts/Logout anchors are `href="#"` but ARE handler-bound).
    expect(vueFiles.length).toBeGreaterThan(50)

    const sidebar = VUE_SOURCES['../../components/paper/PaperSidebar.vue']
    expect(sidebar).toContain('<template>')
    expect(sidebar).toContain('href="#"')
    expect(findDeadAnchors(sidebar)).toEqual([])
  })

  // Self-test: proves the detector can still see the defect it exists to
  // catch. Without it, a regex typo would turn the repo-wide assertion below
  // into a permanently green no-op.
  it('detects the shapes it claims to detect', () => {
    expect(findDeadAnchors('<template><a href="#">Tune heuristics</a></template>')).toEqual([
      '<a href="#">',
    ])
    expect(findDeadAnchors(`<template><a :href="'#'" class="x">Dead</a></template>`)).toHaveLength(1)
    // A multi-line tag is still one tag.
    expect(findDeadAnchors('<template>\n<a\n  href="#"\n  class="x"\n>Dead</a>\n</template>')).toHaveLength(1)
  })

  it('does not flag anchors that are bound, real, or only talked about', () => {
    expect(findDeadAnchors('<template><a href="#" @click.prevent="open">Live</a></template>')).toEqual([])
    expect(findDeadAnchors('<template><a href="#" v-on:click="open">Live</a></template>')).toEqual([])
    expect(findDeadAnchors('<template><a href="#section">In-page</a></template>')).toEqual([])
    expect(findDeadAnchors('<template><a href="/workspace/home">Real route</a></template>')).toEqual([])
    // Sibling elements whose names start with "a" are not anchors.
    expect(findDeadAnchors('<template><aside href="#">Not an anchor</aside></template>')).toEqual([])
    // Prose explaining a removed dead anchor is prose, not a dead anchor.
    expect(findDeadAnchors('<script>/* was <a href="#">x</a> */</script><template><p>ok</p></template>')).toEqual([])
    expect(findDeadAnchors('<template><!-- was <a href="#">x</a> --><p>ok</p></template>')).toEqual([])
  })

  it('never ships an anchor with a placeholder href and no click binding', () => {
    const offenders: string[] = []

    for (const [file, source] of Object.entries(VUE_SOURCES)) {
      for (const tag of findDeadAnchors(source)) {
        offenders.push(`${file.replace('../../', 'src/')}: ${tag}`)
      }
    }

    expect(offenders).toEqual([])
  })
})
