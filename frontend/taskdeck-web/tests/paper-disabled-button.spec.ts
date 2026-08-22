import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'

import PaperHLBtn from '../src/components/paper/PaperHLBtn.vue'

/**
 * Guard: a disabled Paper button must not look like a live one (GH-1953).
 *
 * `.pbtn` shipped with no `:disabled` rule at all, so a disabled Paper button
 * kept its full fill, its `cursor: pointer` and its hover response — CSS
 * `:hover` matches disabled buttons perfectly happily. It looked live, ate the
 * click and said nothing, which is exactly how GH-1944 presented.
 *
 * happy-dom resolves neither `var(--*)` nor a real cascade, so
 * `getComputedStyle` cannot answer "is this button visibly off?". This guard
 * instead resolves the cascade out of the token source itself and asserts the
 * OUTCOME rather than the presence of any one line: a token refactor that
 * renames, reorders or merges rules stays green, while one that drops the
 * treatment — or adds an unguarded `:hover` — goes red.
 *
 * It lives in the Node-flavoured frontend-root `tests/` lane, beside
 * `paper-ink-ladder-contrast.spec.ts` and for the same reason: it reads the
 * token sheet off disk. `tsconfig.vitest.json` type-checks `src/tests/**`
 * without node types and its quarantine list may only shrink, so a new
 * `node:fs` spec cannot go there. Vite's `?raw` is no escape hatch either —
 * Vitest stubs CSS modules to an empty string, `?raw` included (measured).
 * This lane is not type-checked (#1607).
 */

const webRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
const tokensCss = readFileSync(resolve(webRoot, 'src/paper-tokens.css'), 'utf8')

/** Declared property -> value, as written in the sheet. */
type Declarations = Record<string, string>

interface CssRule {
  selector: string
  /**
   * Class-level weight only. The Paper button cascade uses classes and
   * pseudo-classes exclusively (asserted below), so no id, attribute or
   * element component can move the winner.
   */
  specificity: number
  /** Source position — the cascade tie-break at equal specificity. */
  order: number
  declarations: Declarations
}

/** A hypothetical button: the classes it carries, the states it is in. */
interface ElementState {
  classes: string[]
  states: string[]
}

interface Compound {
  classes: string[]
  states: string[]
  excludedStates: string[]
}

function parseDeclarations(body: string): Declarations {
  const declarations: Declarations = {}
  for (const part of body.split(';')) {
    const separator = part.indexOf(':')
    if (separator === -1) continue
    const name = part.slice(0, separator).trim()
    const value = part.slice(separator + 1).trim()
    if (name && value) declarations[name] = value
  }
  return declarations
}

function specificity(selector: string): number {
  // `:not()` itself contributes nothing — its argument does — so unwrap it
  // before counting. Pseudo-elements are element-level and never decide these.
  const flattened = selector.replace(/::[a-z-]+/g, '').replace(/:not\(/g, '(')
  return (flattened.match(/\./g) ?? []).length + (flattened.match(/:/g) ?? []).length
}

function parseRules(css: string): CssRule[] {
  const source = css.replace(/\/\*[\s\S]*?\*\//g, '')
  const rules: CssRule[] = []
  const preludes: string[] = []
  let buffer = ''
  let order = 0
  for (const character of source) {
    if (character === '{') {
      preludes.push(buffer.trim())
      buffer = ''
      continue
    }
    if (character === '}') {
      const prelude = preludes.pop() ?? ''
      // At-rule preludes (`@media`, `@keyframes`) only wrap nested rules, and
      // those were already collected when their own brace closed.
      if (prelude && !prelude.startsWith('@')) {
        const declarations = parseDeclarations(buffer)
        for (const selector of prelude.split(',')) {
          rules.push({
            selector: selector.trim(),
            specificity: specificity(selector),
            order: order++,
            declarations,
          })
        }
      }
      buffer = ''
      continue
    }
    buffer += character
  }
  return rules
}

/**
 * Read one `<scope> <compound>` selector, or null when it cannot describe the
 * button element itself: a different theme scope, a deeper descendant, or a
 * pseudo-element.
 */
function parseCompound(selector: string, scope: string): Compound | null {
  const trimmed = selector.trim()
  const prefix = `${scope} `
  if (!trimmed.startsWith(prefix)) return null
  let rest = trimmed.slice(prefix.length).trim()
  if (/[\s>+~]/.test(rest)) return null
  if (rest.includes('::')) return null

  const excludedStates: string[] = []
  rest = rest.replace(/:not\(([^)]*)\)/g, (_match: string, argument: string) => {
    for (const part of argument.split(',')) {
      const token = part.trim()
      if (token.startsWith(':')) excludedStates.push(token.slice(1))
    }
    return ''
  })

  const classes: string[] = []
  const states: string[] = []
  for (const token of rest.split(/(?=[.:])/)) {
    const part = token.trim()
    if (part.startsWith('.')) classes.push(part.slice(1))
    else if (part.startsWith(':')) states.push(part.slice(1))
    else if (part.length > 0) return null
  }
  return { classes, states, excludedStates }
}

function matches(compound: Compound, element: ElementState): boolean {
  return (
    compound.classes.every((className) => element.classes.includes(className)) &&
    compound.states.every((state) => element.states.includes(state)) &&
    compound.excludedStates.every((state) => !element.states.includes(state))
  )
}

const rules = parseRules(tokensCss)

/** The declarations that actually win for `element` under `scope`. */
function resolveStyle(scope: string, element: ElementState): Declarations {
  const winners = rules
    .map((rule) => ({ rule, compound: parseCompound(rule.selector, scope) }))
    .filter(
      (entry): entry is { rule: CssRule; compound: Compound } => entry.compound !== null,
    )
    .filter((entry) => matches(entry.compound, element))
    .sort((a, b) => a.rule.specificity - b.rule.specificity || a.rule.order - b.rule.order)

  const resolved: Declarations = {}
  for (const entry of winners) Object.assign(resolved, entry.rule.declarations)
  return resolved
}

const SCOPES = ['.paper', '.paper-night'] as const

const VARIANTS = [
  { name: 'default', prop: 'default', classes: ['pbtn'] },
  { name: 'primary', prop: 'primary', classes: ['pbtn', 'pbtn-primary'] },
  { name: 'ember', prop: 'ember', classes: ['pbtn', 'pbtn-ember'] },
  { name: 'ghost', prop: 'ghost', classes: ['pbtn', 'pbtn-ghost'] },
] as const

/** The properties that carry a button's emphasis. */
const EMPHASIS = ['background', 'color', 'border-color', 'box-shadow'] as const

describe('paper-tokens.css cascade model', () => {
  it('finds the Paper button rules', () => {
    // Without this, every guard below could pass by matching nothing at all.
    expect(rules.filter((rule) => rule.selector.includes('.pbtn')).length).toBeGreaterThan(0)
  })

  it('styles Paper buttons with classes and pseudo-classes only', () => {
    // An id or attribute component would outrank every class-level rule and
    // invalidate the simple weighting this guard resolves the cascade with.
    for (const rule of rules.filter((entry) => entry.selector.includes('.pbtn'))) {
      expect(rule.selector).not.toMatch(/[#[]/)
    }
  })
})

describe.each(SCOPES)('Paper button disabled treatment — %s', (scope) => {
  it('resolves an enabled button to a live affordance', () => {
    // Anchors the model: if this drifts, every distinctness check below is
    // measuring against the wrong baseline.
    const resting = resolveStyle(scope, { classes: ['pbtn'], states: [] })
    expect(resting.cursor).toBe('pointer')
    expect(resting.background).toBeDefined()
  })

  describe.each(VARIANTS)('$name variant', (variant) => {
    const classes = [...variant.classes]
    const resting = () => resolveStyle(scope, { classes, states: [] })
    const off = () => resolveStyle(scope, { classes, states: ['disabled'] })

    it('is visually distinct from the enabled button', () => {
      const enabled = resting()
      const disabled = off()
      const changed = EMPHASIS.filter((property) => enabled[property] !== disabled[property])
      expect(changed.length).toBeGreaterThan(0)
      // Emphasis must actually drop — a border-only tweak is not a signal.
      expect(enabled.background !== disabled.background || enabled.color !== disabled.color).toBe(
        true,
      )
    })

    it('withdraws the pointer affordance', () => {
      expect(off().cursor).toBe('not-allowed')
    })

    it('does not light up on hover while disabled', () => {
      // The GH-1944 tell: CSS `:hover` matches a disabled button happily.
      expect(resolveStyle(scope, { classes, states: ['disabled', 'hover'] })).toEqual(off())
    })

    it('does not depress on :active while disabled', () => {
      expect(resolveStyle(scope, { classes, states: ['disabled', 'active'] })).toEqual(off())
    })

    it('still lights up on hover when enabled', () => {
      // Closes the cheap way to satisfy the hover guard above: deleting the
      // hover rules outright would also make a disabled button hover-inert.
      expect(resolveStyle(scope, { classes, states: ['hover'] })).not.toEqual(resting())
    })
  })
})

describe('PaperHLBtn renders what the cascade guard assumes', () => {
  it.each(VARIANTS)('$name variant carries its classes and the disabled state', (variant) => {
    const wrapper = mount(PaperHLBtn, {
      props: { label: 'Accept on board', variant: variant.prop, disabled: true },
    })
    const button = wrapper.get('button')
    // The rules above only bite if the DOM really carries `disabled` and the
    // variant classes those selectors are written against.
    expect(button.attributes('disabled')).toBeDefined()
    for (const className of variant.classes) expect(button.classes()).toContain(className)
  })
})
