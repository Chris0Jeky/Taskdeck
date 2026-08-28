import { describe, expect, it } from 'vitest'
import { baseParse, NodeTypes } from '@vue/compiler-dom'

/**
 * Dead-affordance source guard.
 *
 * WHAT THIS MECHANIZES: two source shapes that look interactive and do nothing:
 * an `<a>` tag whose href is the bare `#` placeholder and an enabled-looking
 * native `<button>` with no detectable action binding or native form semantics,
 * plus interactive `aria-label` semantics that are not native or keyboard
 * reachable. Labelled custom buttons must expose both Enter and Space
 * activation rather than merely listening for an arbitrary key.
 * The GH-1941 defect was the BOUND anchor form, `:href="tuneHref ?? '#'"`, so
 * both static and bound placeholder hrefs are detected.
 *
 * WHAT THIS DOES NOT MECHANIZE. The dogfooding pass that produced GH-1932 and
 * GH-1934 found dead affordances of several other shapes; this guard does not
 * cover them, and passing it is not evidence that they are gone:
 *  - `href="javascript:void(0)"` and `href=""` — not detected.
 *  - Runtime-assembled hrefs and dynamic event names are out of reach: this
 *    guard reads SFC source, not Vue's rendered event table.
 *  - Button action hidden behind a runtime-bound `:type` is not inferred.
 *    Dynamic event arguments and `v-on` object syntax are parsed by the
 *    compiler-backed guard below, but remain unproven action evidence.
 *  - A static `draggable="true"` is treated as native drag activation; the
 *    guard cannot prove which ancestor receives the drag event.
 *  - Component tags (for example `<PaperHLBtn>`) are not compiler-expanded;
 *    this guard only checks their explicit role/action source shape. Their
 *    rendered root still needs component tests for the final DOM contract.
 *  - `v-on="{ click: fn }"` object syntax and `@[dynamicEvent]` are reported
 *    on native controls by the compiler-backed guard below. No such tag exists
 *    today; do not delete that finding without proving the runtime event.
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

/** Keyboard activation bindings that make a custom labelled control operable. */
const KEYBOARD_ACTION_BINDING =
  /(?:@|v-on:)(?:keydown|keyup)(?:\.[\w-]+)*\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/gi

/** Keyboard bindings with their Vue modifier tokens captured for button-key checks. */
const MODIFIED_KEYBOARD_ACTION_BINDING =
  /(?:@|v-on:)(?:keydown|keyup)((?:\.[\w-]+)*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/gi

/** System modifiers mean the binding is not ordinary unmodified activation. */
const SYSTEM_KEY_MODIFIERS = new Set(['ctrl', 'alt', 'shift', 'meta'])

/** Any explicit keyboard/pointer action binding on a custom labelled control. */
const INTERACTIVE_ACTION_BINDING =
  /(?:@|v-on:)(?:click|mousedown|mouseup|pointerdown|pointerup|keydown|keyup|touchstart|touchend|dblclick|contextmenu)(?:\.[\w-]+)*\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/gi

/** Opening tags that carry an aria label. Bound labels are intentionally opaque. */
const ARIA_LABEL_ATTR =
  /\s(?::|v-bind:)?aria-label\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/i

/** Explicit ARIA roles whose label describes a control rather than a landmark. */
const INTERACTIVE_ARIA_ROLES = new Set([
  'button',
  'checkbox',
  'combobox',
  'link',
  'menuitem',
  'menuitemcheckbox',
  'menuitemradio',
  'radio',
  'scrollbar',
  'searchbox',
  'slider',
  'spinbutton',
  'switch',
  'tab',
  'textbox',
  'treeitem',
])

/** Roles that explicitly identify a non-interactive labelled container. */
const ROLE_ATTR = /\s(?::|v-bind:)?role\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/i

/** Static tabindex is required because a dynamic value cannot prove keyboard reachability. */
const TABINDEX_ATTR = /\s((?::|v-bind:)?tabindex)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/i

/** ARIA state/property hints identify custom controls even when the role is omitted. */
const INTERACTIVE_ARIA_STATE =
  /\s(?:aria-(?:expanded|haspopup|pressed|selected|controls|activedescendant)|:(?:aria-expanded|aria-haspopup|aria-pressed|aria-selected|aria-controls|aria-activedescendant)|v-bind:(?:aria-expanded|aria-haspopup|aria-pressed|aria-selected|aria-controls|aria-activedescendant))\s*=/i

/** Class/label hints retain the specific bare-avatar regression without scanning landmarks. */
const INTERACTIVE_LABEL_HINT =
  /\b(?:profile|settings?|notifications?|avatar|switch\s+workspace|open\b|close\b|back\b|delete\b|remove\b|edit\b|toggle\b|menu\b|button\b|control\b|filter\b|copy\b|clear\b|add\b)\b/i
const INTERACTIVE_CLASS_HINT =
  /\b(?:avatar|icon-btn|button|btn|control|action|trigger|toggle|menu-item|menuitem)\b/i

/** Components whose single root is a native control and which preserve fallthrough attrs. */
const NATIVE_INTERACTIVE_COMPONENTS = new Set([
  'inputassistfield',
  'paperhlbtn',
  'router-link',
  'routerlink',
  'tdbutton',
  'tdiconbutton',
])

/** Opening tags for native or custom elements. Quoted values are consumed whole. */
const OPENING_TAG = /<([A-Za-z][\w.-]*)(?=[\s>/])(?:"[^"]*"|'[^']*'|[^>'"])*>/g

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

/** Opening-tag attributes, with quoted values consumed before the next name is read. */
const OPENING_ATTRIBUTE =
  /([@A-Za-z_][\w:.-]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?/g

/** Native controls for which a dynamic listener can change actionability. */
const COMPILER_ACTIONABLE_TAGS = new Set(['a', 'button'])

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

/** Keep only actual Vue event attributes; inert quoted text is not executable markup. */
function directiveOnlyMarkup(tag: string): string {
  const body = tag.replace(/^<[A-Za-z][\w.-]*/, '').replace(/\/?>(?:\s*)$/, '')
  const directives: string[] = []

  for (const match of body.matchAll(OPENING_ATTRIBUTE)) {
    const name = match[1]
    if (!name.startsWith('@') && !/^v-on:/i.test(name)) continue

    directives.push(match[0])
  }

  return directives.join(' ')
}

/**
 * Find dynamic/object `v-on` bindings on native controls using Vue's parser.
 *
 * The compiler exposes dynamic event arguments as `arg.isStatic === false`
 * and object syntax as a directive with no `arg`. Both are intentionally
 * reported rather than treated as a proven click handler: source text cannot
 * establish which runtime event an unknown argument or object key provides.
 */
function findCompilerDynamicListenerTags(source: string): string[] {
  const compilerErrors: unknown[] = []
  const root = baseParse(markupOnly(source), {
    onError: (error) => compilerErrors.push(error),
  })

  if (compilerErrors.length > 0) {
    throw new Error(`Vue template parse failed with ${compilerErrors.length} error(s)`)
  }

  const findings: string[] = []
  const visit = (node: unknown): void => {
    if (!node || typeof node !== 'object') return
    const candidate = node as {
      type?: unknown
      tag?: unknown
      props?: unknown
      children?: unknown
      branches?: unknown
      loc?: { source?: unknown }
    }

    if (candidate.type === NodeTypes.ELEMENT && typeof candidate.tag === 'string') {
      const hasDynamicListener = Array.isArray(candidate.props) && candidate.props.some((prop) => {
        if (!prop || typeof prop !== 'object') return false
        const directive = prop as {
          type?: unknown
          name?: unknown
          arg?: { isStatic?: unknown }
        }
        return (
          directive.type === NodeTypes.DIRECTIVE &&
          directive.name === 'on' &&
          (!directive.arg || directive.arg.isStatic === false)
        )
      })

      if (hasDynamicListener && COMPILER_ACTIONABLE_TAGS.has(candidate.tag.toLowerCase())) {
        findings.push(typeof candidate.loc?.source === 'string' ? candidate.loc.source : candidate.tag)
      }
    }

    if (Array.isArray(candidate.children)) {
      for (const child of candidate.children) visit(child)
    }
    if (Array.isArray(candidate.branches)) {
      for (const branch of candidate.branches) visit(branch)
    }
  }

  visit(root)
  return findings
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
  for (const match of directiveOnlyMarkup(tag).matchAll(binding)) {
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

/** Extract the opening tag name without depending on Vue compiler expansion. */
function openingTagName(tag: string): string {
  return /^<([A-Za-z][\w.-]*)/.exec(tag)?.[1]?.toLowerCase() ?? ''
}

/** Return the literal role when source text makes it knowable. */
function ariaRole(tag: string): string {
  const match = ROLE_ATTR.exec(tag)
  return (match?.[1] ?? match?.[2] ?? match?.[3] ?? '').trim().toLowerCase()
}

/** A native control is keyboard/focusable by its platform semantics. */
function isNativeInteractiveTag(tag: string): boolean {
  const name = openingTagName(tag)
  if (NATIVE_INTERACTIVE_COMPONENTS.has(name)) return true
  if (name === 'a') return /\s(?::|v-bind:)?href\s*=/.test(tag)
  if (name === 'input') {
    const type = BUTTON_TYPE_ATTR.exec(tag)
    return (type?.[1] ?? type?.[2] ?? type?.[3] ?? '').trim().toLowerCase() !== 'hidden'
  }
  return new Set(['button', 'select', 'textarea', 'summary']).has(name)
}

/** A custom button must explicitly handle both platform activation keys. */
function hasButtonKeyboardActivation(tag: string): boolean {
  let handlesEnter = false
  let handlesSpace = false

  for (const match of directiveOnlyMarkup(tag).matchAll(MODIFIED_KEYBOARD_ACTION_BINDING)) {
    const expression = (match[2] ?? match[3] ?? match[4] ?? '').trim()
    if (expression.length === 0) continue

    const modifiers = (match[1] ?? '')
      .split('.')
      .filter((modifier) => modifier.length > 0)
    if (modifiers.some((modifier) => SYSTEM_KEY_MODIFIERS.has(modifier.toLowerCase()))) continue
    handlesEnter ||= modifiers.includes('enter')
    handlesSpace ||= modifiers.includes('space')
  }

  return handlesEnter && handlesSpace
}

/** A custom control must prove both keyboard reachability and role-appropriate keyboard activation. */
function isFocusableAndKeyboardActionable(tag: string): boolean {
  const tabindex = TABINDEX_ATTR.exec(tag)
  if (!tabindex || tabindex[1].startsWith(':') || tabindex[1].startsWith('v-bind:')) return false
  const value = (tabindex[2] ?? tabindex[3] ?? tabindex[4] ?? '').trim()
  if (!/^(?:0|[1-9]\d*)$/.test(value)) return false

  // ARIA button parity is specific: both Enter and Space activate a custom
  // button. Other explicit roles have different keyboard contracts, so this
  // bounded hardening slice preserves their existing handler-evidence rule.
  if (ariaRole(tag) === 'button') return hasButtonKeyboardActivation(tag)
  return hasNonEmptyBinding(tag, KEYBOARD_ACTION_BINDING)
}

/** Whether a labelled element is intended to be a control rather than a landmark or description. */
function hasInteractiveAriaSemantics(tag: string): boolean {
  const role = ariaRole(tag)
  if (role.length > 0) return INTERACTIVE_ARIA_ROLES.has(role)
  if (isNativeInteractiveTag(tag)) return true
  if (hasNonEmptyBinding(tag, INTERACTIVE_ACTION_BINDING)) return true
  if (INTERACTIVE_ARIA_STATE.test(tag)) return true

  const label = ARIA_LABEL_ATTR.exec(tag)
  const labelValue = (label?.[1] ?? label?.[2] ?? label?.[3] ?? '').trim()
  return INTERACTIVE_LABEL_HINT.test(labelValue) || INTERACTIVE_CLASS_HINT.test(tag)
}

/** Opening labelled controls that are not native or demonstrably keyboard actionable. */
function findAriaLabelViolations(source: string): string[] {
  const violations: string[] = []
  for (const [tag] of markupOnly(source).matchAll(OPENING_TAG)) {
    if (!ARIA_LABEL_ATTR.test(tag) || !hasInteractiveAriaSemantics(tag)) continue
    if (isNativeInteractiveTag(tag)) continue
    if (isFocusableAndKeyboardActionable(tag)) continue
    violations.push(tag.replace(/\s+/g, ' '))
  }
  return violations
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

/** The GH-1932 avatar shape: a labelled generic element with no focus or action semantics. */
const DEAD_ARIA_LABEL_FIXTURE = `<template><div class="paper-topbar__avatar" aria-label="Profile: D">D</div></template>`
const COMPILER_DYNAMIC_LISTENER_FIXTURE =
  '<template><button @[eventName]="run">Dynamic</button><button v-on="{ click: run }">Object</button></template>'

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
    expect(findDeadButtons(`<template><button data-note='@click="run"'>Inert note</button></template>`)).toHaveLength(1)
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
    // Dynamic listeners remain unproven for the source-level action guard;
    // compiler-backed coverage below reports their exact AST shape.
    expect(findDeadButtons('<template><button @[eventName]="run">Dynamic event</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button v-on="{ click: run }">Object event</button></template>')).toHaveLength(1)
    expect(findDeadButtons('<template><button :type="buttonType">Dynamic type</button></template>')).toHaveLength(1)
  })

  it('detects dynamic and object listeners through the Vue compiler AST', () => {
    expect(findCompilerDynamicListenerTags(COMPILER_DYNAMIC_LISTENER_FIXTURE)).toHaveLength(2)
    expect(findCompilerDynamicListenerTags('<template><button @click="run">Static</button></template>')).toEqual([])
    expect(
      findCompilerDynamicListenerTags(
        `<template><button data-note='@[eventName]="run"'>Inert note</button></template>`,
      ),
    ).toEqual([])
    expect(
      findCompilerDynamicListenerTags(
        '<template><div v-if="ready"><button @[eventName]="run">Nested dynamic</button></div></template>',
      ),
    ).toHaveLength(1)
  })

  it('detects interactive aria labels that are not native or keyboard actionable', () => {
    expect(findAriaLabelViolations(DEAD_ARIA_LABEL_FIXTURE)).toEqual([
      '<div class="paper-topbar__avatar" aria-label="Profile: D">',
    ])
    expect(findAriaLabelViolations('<template><div role="button" tabindex="0" aria-label="Settings" @click="open">Open</div></template>')).toHaveLength(1)
    expect(findAriaLabelViolations('<template><div role="region" aria-label="Settings panel" @click.self="close">Panel</div></template>')).toEqual([])
    expect(findAriaLabelViolations('<template><button aria-label="Settings" @click="open">Open</button></template>')).toEqual([])
    expect(findAriaLabelViolations('<template><a href="/settings" aria-label="Settings">Settings</a></template>')).toEqual([])
    expect(findAriaLabelViolations('<template><a aria-label="Settings">Settings</a></template>')).toHaveLength(1)
  })

  it('requires explicit Enter and Space activation for labelled custom buttons', () => {
    expect(
      findAriaLabelViolations(
        `<template><div role="button" tabindex="0" aria-label="Settings" data-note='@keydown.enter="open" @keydown.space="open"'>Inert note</div></template>`,
      ),
    ).toHaveLength(1)
    expect(findAriaLabelViolations('<template><div role="button" tabindex="0" aria-label="Settings" @keydown.enter="open">Open</div></template>')).toHaveLength(1)
    expect(findAriaLabelViolations('<template><div role="button" tabindex="0" aria-label="Settings" @keydown.space="open">Open</div></template>')).toHaveLength(1)
    expect(findAriaLabelViolations('<template><div role="button" tabindex="0" aria-label="Settings" @keydown.escape="close">Open</div></template>')).toHaveLength(1)
    expect(findAriaLabelViolations('<template><div role="button" tabindex="0" aria-label="Settings" @keydown.Enter="open" @keydown.SPACE.prevent="open">Open</div></template>')).toHaveLength(1)
    expect(findAriaLabelViolations('<template><div role="button" tabindex="0" aria-label="Settings" @keydown.enter="open" @keydown.space.prevent="open">Open</div></template>')).toEqual([])
    expect(findAriaLabelViolations('<template><div role="button" tabindex="0" aria-label="Settings" @keydown.enter.stop="open" @keydown.space.self="open">Open</div></template>')).toEqual([])
    expect(findAriaLabelViolations('<template><div role="button" tabindex="0" aria-label="Settings" v-on:keyup.enter="open" v-on:keydown.space="open">Open</div></template>')).toEqual([])

    for (const modifier of ['ctrl', 'alt', 'shift', 'meta']) {
      expect(
        findAriaLabelViolations(
          `<template><div role="button" tabindex="0" aria-label="Settings" @keydown.${modifier}.enter="open" @keydown.space="open">Open</div></template>`,
        ),
      ).toHaveLength(1)
      expect(
        findAriaLabelViolations(
          `<template><div role="button" tabindex="0" aria-label="Settings" @keydown.enter="open" @keydown.${modifier}.space="open">Open</div></template>`,
        ),
      ).toHaveLength(1)
    }

    // A switch has a different ARIA keyboard contract; this slice must not
    // manufacture an Enter requirement for every role in the shared set.
    expect(findAriaLabelViolations('<template><div role="switch" tabindex="0" aria-label="Notifications" @keydown.space="toggle">Toggle</div></template>')).toEqual([])
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

  it('never ships native controls with unproven compiler-expanded listeners', () => {
    const offenders: string[] = []

    expect(vueFiles.length).toBeGreaterThan(50)
    for (const [file, source] of Object.entries(VUE_SOURCES)) {
      for (const tag of findCompilerDynamicListenerTags(source)) {
        offenders.push(`${file.replace('../../', 'src/')}: ${tag}`)
      }
    }

    expect(offenders).toEqual([])
  })

  it('never ships an interactive aria label without native or keyboard semantics', () => {
    const offenders: string[] = []

    for (const [file, source] of Object.entries(VUE_SOURCES)) {
      for (const tag of findAriaLabelViolations(source)) {
        offenders.push(`${file.replace('../../', 'src/')}: ${tag}`)
      }
    }

    expect(offenders).toEqual([])
  })
})
