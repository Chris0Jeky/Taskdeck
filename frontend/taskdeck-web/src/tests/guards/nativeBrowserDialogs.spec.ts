import { describe, expect, it } from 'vitest'

/**
 * Native-browser-dialog guard (GH-1969, extending #1818 from a one-off fix to a
 * rule).
 *
 * WHY A RULE. `window.prompt` / `confirm` / `alert` are the one class of UI the
 * product cannot style, cannot translate, cannot exercise from a component spec,
 * and cannot rely on at all — several embedded and automation contexts suppress
 * them outright, so whatever they were collecting is silently lost. #1818
 * removed a `confirm()` from the apply path; the prompt collecting the REJECTION
 * REASON survived that pass unnoticed for exactly the reason this guard exists:
 * the specs had to stub `window.prompt` to test it, so nothing looked wrong.
 *
 * SCOPE. Every `.vue` and `.ts` file under `src/views/paper/`, `src/composables/`
 * and `src/components/review/` — the review and Paper surfaces. It is not a
 * whole-repo rule; widening it means clearing the quarantine below first.
 *
 * WHAT IT DOES NOT MECHANIZE. It reads source text, so it sees a literal call
 * and nothing else. `globalThis['prom' + 'pt']()`, an aliased
 * `const ask = window.prompt`, and a native dialog reached through an
 * intermediary module outside the scanned directories are all invisible to it.
 * Passing this guard is evidence that the obvious form is absent, not that the
 * surfaces are free of browser dialogs.
 *
 * Sources come from `import.meta.glob`, not `node:fs`: `tsconfig.vitest.json`
 * deliberately keeps node types out of the spec type-check project.
 */

/** Raw source for every scanned file, keyed by path relative to this file. */
const SOURCES: Record<string, string> = {
  ...(import.meta.glob('../../views/paper/**/*.{vue,ts}', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>),
  ...(import.meta.glob('../../composables/**/*.ts', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>),
  ...(import.meta.glob('../../components/review/**/*.{vue,ts}', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>),
}

/**
 * Files with a KNOWN remaining native dialog, tracked separately. This list may
 * only ever shrink: adding to it is how a rule stops being one.
 *
 * `useCardModal.ts` — `confirm('Delete this comment?')` on the card-comment
 * delete path. Same class as the reject prompt, different surface (board/card
 * modal, not review), so GH-1969 did not touch it. Tracked on GH-1997.
 */
const QUARANTINE = ['../../composables/useCardModal.ts']

/**
 * Strip comments so PROSE about a native dialog is not reported as one — several
 * of these files carry doc comments explaining what #1818 and GH-1969 removed,
 * and a guard that punished the explanation would get the explanation deleted.
 *
 * A `//` inside a string literal (a URL) truncates that line early. That can
 * only cause a MISSED call, never a false one, and no scanned file puts a
 * dialog call after a URL on the same line.
 */
function withoutComments(source: string): string {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/(^|\n)([^\n]*?)\/\/[^\n]*/g, '$1$2')
}

/**
 * A call to one of the three natives. Two forms:
 *  - explicitly on the global object: `window.prompt(`, `globalThis.confirm(`
 *  - bare, resolving to the global: `prompt(`
 * The lookbehind on the bare form keeps property and method access out
 * (`toast.confirm(`, `this.alert(`), and the names are case-sensitive so
 * `onConfirm(` and `showAlert(` are not matched.
 */
const QUALIFIED_CALL = /\b(?:window|globalThis|self)\s*\.\s*(prompt|confirm|alert)\s*\(/
const BARE_CALL = /(?<![.\w$])(prompt|confirm|alert)\s*\(/

function nativeDialogLines(source: string): string[] {
  return withoutComments(source)
    .split('\n')
    .filter((line) => QUALIFIED_CALL.test(line) || BARE_CALL.test(line))
    .map((line) => line.trim())
}

describe('native browser dialogs', () => {
  it('scans the review and Paper surfaces', () => {
    // A glob that silently matched nothing would make every assertion below
    // vacuous — the classic way a source guard rots into decoration.
    expect(Object.keys(SOURCES).length).toBeGreaterThan(40)
    expect(SOURCES['../../composables/useReviewActions.ts']).toBeTruthy()
    expect(SOURCES['../../views/paper/PaperReviewView.vue']).toBeTruthy()
    expect(SOURCES['../../components/review/RejectProposalDialog.vue']).toBeTruthy()
  })

  it('never calls prompt(), confirm() or alert()', () => {
    const offenders: string[] = []
    for (const [path, source] of Object.entries(SOURCES)) {
      if (QUARANTINE.includes(path)) continue
      for (const line of nativeDialogLines(source)) {
        offenders.push(`${path}: ${line}`)
      }
    }

    expect(
      offenders,
      'Native browser dialogs cannot be styled, translated or tested, and are ' +
        'suppressed in some embedded contexts. Use TdDialog (see ' +
        'RejectProposalDialog.vue / ApplyToBoardDialog.vue) instead.',
    ).toEqual([])
  })

  it('still finds the quarantined call, so the list cannot rot into a no-op', () => {
    // If this fails because the call is gone, delete the entry — do not relax
    // the assertion.
    for (const path of QUARANTINE) {
      const source = SOURCES[path]
      expect(source, `quarantined file ${path} is no longer scanned`).toBeTruthy()
      expect(nativeDialogLines(source!).length).toBeGreaterThan(0)
    }
  })

  it('detects the shapes it claims to detect', () => {
    // The guard's own regression test: a guard nobody has seen fail is a guard
    // nobody knows works.
    expect(nativeDialogLines('const r = prompt("why?")')).toHaveLength(1)
    expect(nativeDialogLines('if (!confirm("sure?")) return')).toHaveLength(1)
    expect(nativeDialogLines('window.alert("hi")')).toHaveLength(1)
    expect(nativeDialogLines('globalThis.prompt("why?")')).toHaveLength(1)

    // …and leaves the near misses alone.
    expect(nativeDialogLines('// the native confirm() is gone')).toEqual([])
    expect(nativeDialogLines('/* replaced prompt("x") with a dialog */')).toEqual([])
    expect(nativeDialogLines('function onConfirm() {}')).toEqual([])
    expect(nativeDialogLines("emit('confirm', reason)")).toEqual([])
    expect(nativeDialogLines('toast.confirm(message)')).toEqual([])
  })
})
