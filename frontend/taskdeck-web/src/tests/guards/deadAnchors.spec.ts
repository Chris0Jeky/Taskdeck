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
 *  - A static `draggable="true"` is treated as native drag activation; the
 *    guard cannot prove which ancestor receives the drag event.
 *  - `v-on="{ click: fn }"` object syntax and `@[dynamicEvent]` are not read as
 *    click bindings, so a tag using one of those AND a bare `#` href would be
 *    reported. No such tag exists today; if one appears, teach CLICK_BINDING
 *    about it rather than deleting the finding.
 *  - An href assembled at runtime (`:href="somethingThatReturnsHash"`) is out
 *    of reach: only SFC source text is read.
 *  - An expression that concatenates onto the literal (`:href="'#' + id"`)
 *    resolves to a real fragment but IS reported. The guard prefers that false
 *    positive to missing the GH-1941 shape; no such expression exists today.
 *  - The four intentionally inert Paper style-guide specimens carry a narrow
 *    `data-dead-affordance-exempt="visual-specimen"` marker. A new product
 *    control must not copy that marker; the rest of the view is scanned.
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

/** Any click binding with a non-empty handler expression. Dynamic events are not matched. */
const CLICK_BINDING =
  /(?:@|v-on:)click(?:\.[\w-]+)*\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/gi

/** Opening/closing form and native-button tags, with quoted values consumed whole. */
const FORM_OR_BUTTON_TAG = /<\/?(?:form|button)(?=[\s>/])(?:"[^"]*"|'[^']*'|[^>'"])*>/gi

/** Static or bound `type`; a bound value is deliberately not resolved. */
const BUTTON_TYPE_ATTR = /\s(?::|v-bind:)?type\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/i

/** Form association may be static or dynamic; only a static ID can prove ownership. */
const BUTTON_FORM_ATTR =
  /\s(?:(:|v-bind:)?form)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+)))?(?=\s|\/?>)/i
const FORM_ID_ATTR = /\sid\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/i

/** Static disabled is always disabled; a bound value is exempt only when literally `true`. */
const STATIC_DISABLED_ATTR = /(?:^|\s)disabled(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?(?=\s|\/?>)/i
const BOUND_DISABLED_ATTR =
  /\s(?::disabled|v-bind:disabled)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/gi

/** Action events that can make a button live; submit is intentionally not an activation event here. */
const CONTROL_ACTION_BINDING =
  /(?:@|v-on:)(?:click|mousedown|mouseup|pointerdown|pointerup|keydown|keyup|touchstart|touchend|dblclick|contextmenu)(?:\.[\w-]+)*\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/gi

/** Native draggable controls have a browser activation even when the parent owns drag events. */
const NATIVE_DRAGGABLE = /\sdraggable\s*=\s*(?:"true"|'true'|true)(?=\s|\/?>)/i

/** Explicit, narrow exemption for static style-guide specimens only. */
const BUTTON_SPECIMEN_MARKER =
  /\sdata-dead-affordance-exempt\s*=\s*(?:"visual-specimen"|'visual-specimen')(?=\s|\/?>)/i

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
    if (hasNonEmptyBinding(tag, CLICK_BINDING)) continue
    dead.push(tag.replace(/\s+/g, ' '))
  }
  return dead
}

/** Return true when a binding regex finds a non-whitespace handler expression. */
function hasNonEmptyBinding(tag: string, binding: RegExp): boolean {
  for (const match of tag.matchAll(binding)) {
    const expression = (match[1] ?? match[2] ?? match[3] ?? '').trim()
    if (expression.length > 0) return true
  }
  return false
}

/** Static form IDs are the only form associations this source guard can prove. */
function formIds(markup: string): Set<string> {
  const ids = new Set<string>()
  for (const [tag] of markup.matchAll(FORM_OR_BUTTON_TAG)) {
    if (!/^<form\b/i.test(tag)) continue
    const id = FORM_ID_ATTR.exec(tag)
    const value = (id?.[1] ?? id?.[2] ?? id?.[3] ?? '').trim()
    if (value.length > 0) ids.add(value)
  }
  return ids
}

/** A native form owner exists only for an ancestor form or a matching static `form` ID. */
function hasFormOwner(tag: string, formDepth: number, knownFormIds: Set<string>): boolean {
  const formAttribute = BUTTON_FORM_ATTR.exec(tag)
  if (formAttribute) {
    // A bound/empty/dynamic `form` value cannot prove ownership from source text.
    if (formAttribute[1]) return false
    const value = (formAttribute[2] ?? formAttribute[3] ?? formAttribute[4] ?? '').trim()
    return value.length > 0 && knownFormIds.has(value)
  }
  return formDepth > 0
}

/** Only static disabled or a literal `:disabled="true"` is a permanent exemption. */
function isPermanentlyDisabled(tag: string): boolean {
  const boundValues = [...tag.matchAll(BOUND_DISABLED_ATTR)].map(
    (match) => (match[1] ?? match[2] ?? match[3] ?? '').trim(),
  )
  if (boundValues.length > 0) return boundValues.every((value) => value === 'true')
  return STATIC_DISABLED_ATTR.test(tag)
}

/** Opening native button tags that look enabled but have no detectable action. */
function findDeadButtons(source: string): string[] {
  const dead: string[] = []
  const markup = markupOnly(source)
  const knownFormIds = formIds(markup)
  let formDepth = 0
  for (const [tag] of markup.matchAll(FORM_OR_BUTTON_TAG)) {
    if (/^<form\b/i.test(tag)) {
      if (!/\/\s*>$/.test(tag)) formDepth += 1
      continue
    }
    if (/^<\/form\b/i.test(tag)) {
      formDepth = Math.max(0, formDepth - 1)
      continue
    }
    if (/^<\//.test(tag)) continue

    if (BUTTON_SPECIMEN_MARKER.test(tag)) continue
    if (hasNonEmptyBinding(tag, CONTROL_ACTION_BINDING)) continue
    if (NATIVE_DRAGGABLE.test(tag)) continue
    if (isPermanentlyDisabled(tag)) continue

    const typeMatch = BUTTON_TYPE_ATTR.exec(tag)
    const type = (typeMatch?.[1] ?? typeMatch?.[2] ?? typeMatch?.[3] ?? '').trim().toLowerCase()
    const owner = hasFormOwner(tag, formDepth, knownFormIds)
    // `type="button"` is inert without an action even inside a form. Submit/reset
    // (and the missing type default) only have native behavior with a real owner.
    if (type !== 'button' && owner && (type.length === 0 || type === 'submit' || type === 'reset')) continue

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
    expect(findDeadAnchors('<template><a href="#" @click.stop>Only propagation</a></template>')).toHaveLength(1)
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
    expect(findDeadButtons('<template><button @click.stop>Only propagation</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button @click.stop="">Empty handler</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button @submit="save">Submit event is not activation</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button draggable="true" @click.stop>Drag handle</button></template>')).toEqual([])
  })

  it('requires an action for type=button and proves native form ownership', () => {
    expect(findDeadButtons('<template><button @click="open">Open</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button @click.stop="open">Stop propagation</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button @mousedown="select">Select</button></template>')).toEqual([])
    expect(findDeadButtons('<template><form><button type="button">No action</button></form></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button type="submit">No form</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button type="reset">No form</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><form id="settings"></form><button form="settings">Submit elsewhere</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button form="settings">Missing form</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><form><button>Submit form</button></form></template>')).toEqual([])
    expect(findDeadButtons('<template><form><button type="submit">Submit form</button></form></template>')).toEqual([])
    expect(findDeadButtons('<template><form><button type="reset">Reset form</button></form></template>')).toEqual([])
  })

  it('exempts only permanently disabled buttons', () => {
    expect(findDeadButtons('<template><button disabled>Unavailable</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button disabled="false">Still unavailable</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button :disabled="true">Constant disabled</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button v-bind:disabled="true">Constant disabled</button></template>')).toEqual([])
    expect(findDeadButtons('<template><button :disabled="busy">Conditionally disabled</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button :disabled="false">Enabled</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button disabled :disabled="busy">Can become enabled</button></template>')).toHaveLength(1)
  })

  it('accepts only the explicit visual-specimen marker', () => {
    expect(
      findDeadButtons('<template><button data-dead-affordance-exempt="visual-specimen">Visual only</button></template>'),
    ).toEqual([])
    expect(findDeadButtons('<template><button data-dead-affordance-exempt="other">Not exempt</button></template>')).toHaveLength(1)
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
      for (const tag of findDeadButtons(source)) {
        offenders.push(`${file.replace('../../', 'src/')}: ${tag}`)
      }
    }

    expect(offenders).toEqual([])
  })
})
