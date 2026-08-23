import { describe, expect, it } from 'vitest'

/**
 * Dead-affordance source guard.
 *
 * WHAT THIS MECHANIZES: two source shapes that look interactive and do nothing:
 * an `<a>` tag whose href is the bare `#` placeholder and an enabled-looking
 * native `<button>` with no detectable action binding or native form semantics.
 * The GH-1941 defect was the BOUND anchor form, `:href="tuneHref ?? '#'"`, so
 * both static and bound placeholder hrefs are detected.
 *
 * WHAT THIS DOES NOT MECHANIZE. The dogfooding pass that produced GH-1932 and
 * GH-1934 found dead affordances of several other shapes; this guard does not
 * cover them, and passing it is not evidence that they are gone:
 *  - `href="javascript:void(0)"` and `href=""` — not detected.
 *  - Runtime-assembled hrefs and dynamic event names are out of reach: this
 *    guard reads SFC source, not Vue's rendered event table.
 *  - Button action hidden behind `v-on="{ click: fn }"`, `@[dynamicEvent]`, or
 *    a runtime-bound `:type` is not inferred. Those forms are reported by the
 *    synthetic canaries to make the boundary explicit.
 *  - `v-on="{ click: fn }"` object syntax and `@[dynamicEvent]` are not read as
 *    click bindings, so a tag using one of those AND a bare `#` href would be
 *    reported. No such tag exists today; if one appears, teach CLICK_BINDING
 *    about it rather than deleting the finding.
 *  - An href assembled at runtime (`:href="somethingThatReturnsHash"`) is out
 *    of reach: only SFC source text is read.
 *  - An expression that concatenates onto the literal (`:href="'#' + id"`)
 *    resolves to a real fragment but IS reported. The guard prefers that false
 *    positive to missing the GH-1941 shape; no such expression exists today.
 *  - `PaperStyleGuideView.vue` is intentionally excluded from the button scan:
 *    it renders static visual specimens, not product affordances. A new
 *    product control belongs outside that view and is therefore not covered by
 *    this exception.
 *
 * It proves a click binding EXISTS, not that the handler calls
 * `preventDefault()` — the `#` navigation itself is a component test's job.
 *
 * The fix for a violation is almost never "add a handler to satisfy the guard".
 * It is: give the control a real destination, or render it as non-interactive
 * text (GH-1949).
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

/**
 * Each href attribute on a tag: the optional binding prefix, then the quoted
 * value. The leading `\s` keeps `:capture-href="…"` and friends out — only an
 * attribute actually named `href` counts. The value is delimited by a
 * backreference to its own opening quote, so a bound expression may contain the
 * other quote style (`:href="dead ?? '#'"`).
 */
const HREF_ATTR = /\s(:|v-bind:)?href\s*=\s*(["'])((?:(?!\2)[\s\S])*)\2/g

/** A bare `#` string literal — `'#'`, `"#"`, `` `#` `` — nothing after the hash. `'#section'` does not match. */
const BARE_HASH_LITERAL = /(['"`])#\1/

/** Any click binding: `@click`, `@click.prevent`, `v-on:click`. `@[dynamic]` is not matched — see the header. */
const CLICK_BINDING = /(?:@|v-on:)click(?:\.[\w-]+)*(?=\s*(?:=|\/?>|\s))/

/** Native buttons need an action binding, form semantics, or an explicit disabled state. */
const FORM_OR_BUTTON_TAG = /<\/?(?:form|button)(?=[\s>/])(?:"[^"]*"|'[^']*'|[^>'"])*>/gi
const BUTTON_TYPE_ATTR = /\s(?::|v-bind:)?type\s*=\s*(["'])(.*?)\1/i
const BUTTON_FORM_ATTR = /\s(?::|v-bind:)?form(?:\s*=|\s|\/>)/i
const BUTTON_DISABLED_ATTR = /\s(?::disabled|v-bind:disabled|disabled)(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?(?=[\s/>])/i

/**
 * A button may intentionally use a pointer or keyboard event instead of
 * `@click` (for example, the combobox option list selects on mousedown). The
 * source guard treats those action events as wired controls too. `v-on="…"`
 * object syntax and `@[dynamicEvent]` remain deliberately outside the rule.
 */
const CONTROL_ACTION_BINDING =
  /(?:@|v-on:)(?:click|mousedown|pointerdown|keydown|keyup|submit)(?:\.[\w-]+)*(?=\s*(?:=|\/?>|\s))/

/** The style-guide view contains intentionally inert visual button specimens. */
const BUTTON_SOURCE_EXCLUSIONS = new Set(['../../views/PaperStyleGuideView.vue'])

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

/** True when any href on `tag` is the bare `#` placeholder, statically or inside a bound expression. */
function hasPlaceholderHref(tag: string): boolean {
  for (const [, binding, , value] of tag.matchAll(HREF_ATTR)) {
    // A bound href's value is a JS expression: a bare `#` literal anywhere in
    // it is the placeholder, however it is reached (`dead ?? '#'`).
    if (binding) {
      if (BARE_HASH_LITERAL.test(value)) return true
      continue
    }
    // A static href's value IS the URL. `#` alone is the placeholder;
    // `#section-id` is a real in-page target and must survive.
    if (value.trim() === '#') return true
    if (BARE_HASH_LITERAL.test(value)) return true
  }
  return false
}

/** Opening anchor tags in `source` whose href is the bare `#` and which bind no click. */
function findDeadAnchors(source: string): string[] {
  const dead: string[] = []
  for (const [tag] of markupOnly(source).matchAll(ANCHOR_TAG)) {
    if (!hasPlaceholderHref(tag)) continue
    if (CLICK_BINDING.test(tag)) continue
    dead.push(tag.replace(/\s+/g, ' '))
  }
  return dead
}

/** Opening native button tags that look enabled but have no detectable action. */
function findDeadButtons(source: string): string[] {
  const dead: string[] = []
  let formDepth = 0
  for (const [tag] of markupOnly(source).matchAll(FORM_OR_BUTTON_TAG)) {
    if (/^<form\b/i.test(tag)) {
      if (!/\/\s*>$/.test(tag)) formDepth += 1
      continue
    }
    if (/^<\/form\b/i.test(tag)) {
      formDepth = Math.max(0, formDepth - 1)
      continue
    }
    if (/^<\//.test(tag)) continue

    if (CONTROL_ACTION_BINDING.test(tag)) continue
    if (BUTTON_DISABLED_ATTR.test(tag)) continue

    const type = BUTTON_TYPE_ATTR.exec(tag)?.[2]?.trim().toLowerCase()
    if (type === 'submit' || type === 'reset') continue
    if (BUTTON_FORM_ATTR.test(tag) || formDepth > 0) continue

    dead.push(tag.replace(/\s+/g, ' '))
  }
  return dead
}

/**
 * The GH-1941 defect, verbatim in shape: a bound href falling back to a bare
 * `#` with nothing listening for the click.
 *
 * The canary below asserts against THIS, not against any shipped component. A
 * real file pinned as "known to contain `href="#"`" would punish the correct
 * fix — converting a pseudo-link to a button removes the string and reddens a
 * guard that has nothing to say about it.
 */
const DEAD_ANCHOR_FIXTURE = `<template><a :href="tuneHref ?? '#'" class="tune">Tune heuristics</a></template>`

describe('dead affordances', () => {
  const vueFiles = Object.keys(VUE_SOURCES)

  it('scans real component source, with a live detector', () => {
    // A guard that silently scans nothing passes forever. Two ways that
    // happens: the glob resolves to no files, or it resolves to empty strings.
    expect(vueFiles.length).toBeGreaterThan(50)
    expect(vueFiles.filter((file) => VUE_SOURCES[file]?.includes('<template')).length).toBeGreaterThan(50)

    // The third way: a regex typo turns the repo-wide assertion into a green
    // no-op. The fixture is synthetic on purpose — see DEAD_ANCHOR_FIXTURE.
    expect(findDeadAnchors(DEAD_ANCHOR_FIXTURE)).toHaveLength(1)
  })

  it('detects the shapes it claims to detect', () => {
    expect(findDeadAnchors('<template><a href="#">Tune heuristics</a></template>')).toEqual([
      '<a href="#">',
    ])
    expect(findDeadAnchors(`<template><a href='#'>Single quoted</a></template>`)).toHaveLength(1)
    expect(findDeadAnchors(`<template><a :href="'#'" class="x">Dead</a></template>`)).toHaveLength(1)
    // The GH-1941 shape: a bound href whose expression falls back to the bare `#`.
    expect(findDeadAnchors(DEAD_ANCHOR_FIXTURE)).toHaveLength(1)
    expect(findDeadAnchors(`<template><a v-bind:href="dead || '#'">Dead</a></template>`)).toHaveLength(1)
    expect(findDeadAnchors(`<template><a :href='dead ?? "#"'>Dead</a></template>`)).toHaveLength(1)
    // A multi-line tag is still one tag.
    expect(findDeadAnchors('<template>\n<a\n  href="#"\n  class="x"\n>Dead</a>\n</template>')).toHaveLength(1)
  })

  it('does not flag anchors that are bound, real, or only talked about', () => {
    expect(findDeadAnchors('<template><a href="#" @click.prevent="open">Live</a></template>')).toEqual([])
    expect(findDeadAnchors('<template><a href="#" v-on:click="open">Live</a></template>')).toEqual([])
    // The GH-1941 shape, but wired up.
    expect(
      findDeadAnchors(`<template><a :href="tuneHref ?? '#'" @click.prevent="tune">Live</a></template>`),
    ).toEqual([])
    // Real in-page fragment targets are not placeholders.
    expect(findDeadAnchors('<template><a href="#details">In-page</a></template>')).toEqual([])
    expect(findDeadAnchors('<template><a href="#td-main-content">Skip link</a></template>')).toEqual([])
    expect(findDeadAnchors(`<template><a :href="'#details'">Bound in-page</a></template>`)).toEqual([])
    expect(findDeadAnchors('<template><a href="/workspace/home">Real route</a></template>')).toEqual([])
    // Sibling elements whose names start with "a" are not anchors.
    expect(findDeadAnchors('<template><aside href="#">Not an anchor</aside></template>')).toEqual([])
    // An attribute merely ENDING in "href" is not an href.
    expect(findDeadAnchors('<template><a :capture-href="\'#\'" href="/x">Not an href</a></template>')).toEqual([])
    // Prose explaining a removed dead anchor is prose, not a dead anchor.
    expect(findDeadAnchors('<script>/* was <a href="#">x</a> */</script><template><p>ok</p></template>')).toEqual([])
    expect(findDeadAnchors('<template><!-- was <a href="#">x</a> --><p>ok</p></template>')).toEqual([])
  })

  it('detects enabled-looking buttons without an action binding', () => {
    expect(findDeadButtons('<template><button type="button">Do nothing</button></template>')).toEqual([
      '<button type="button">',
    ])
    expect(findDeadButtons('<template><button>Do nothing</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button class="x">\n  Do nothing\n</button></template>')).toHaveLength(1)
  })

  it('keeps wired, disabled, and native form buttons out of the findings', () => {
    expect(findDeadButtons('<template><button @click="open">Open</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button @click.stop>Stop propagation</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button @mousedown="select">Select</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button type="submit">Save</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button type="reset">Reset</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button form="settings">Submit elsewhere</button></template>')).toEqual([])
    expect(findDeadButtons('<template><form><button>Submit form</button></form></template>')).toEqual([])
    expect(findDeadButtons('<template><button disabled>Unavailable</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button :disabled="busy">Busy</button></template>')).toEqual([])
  })

  it('documents source-only limits for dynamic Vue bindings', () => {
    // These require template compilation/runtime knowledge and are deliberately
    // not guessed by a regex guard. A future guard can add compiler-backed
    // coverage without weakening this source-level rule.
    expect(findDeadButtons('<template><button @[eventName]="run">Dynamic event</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button v-on="{ click: run }">Object event</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button :type="buttonType">Dynamic type</button></template>')).toHaveLength(1)
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

  it('never ships an enabled-looking native button without an action', () => {
    const offenders: string[] = []

    for (const [file, source] of Object.entries(VUE_SOURCES)) {
      if (BUTTON_SOURCE_EXCLUSIONS.has(file)) continue
      for (const tag of findDeadButtons(source)) {
        offenders.push(`${file.replace('../../', 'src/')}: ${tag}`)
      }
    }

    expect(offenders).toEqual([])
  })
})
