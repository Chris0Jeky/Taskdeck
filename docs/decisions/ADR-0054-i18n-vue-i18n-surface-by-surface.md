# ADR-0054: Internationalization — `vue-i18n` in Composition Mode, Per-Surface Catalogs, Surface-by-Surface Rollout

- **Status**: Accepted
- **Date**: 2026-08-19
- **Deciders**: Implementing agent under maintainer-delegated authority (ADR-0051 autonomous-admission lane), 2026-08-19
- **Related**: `#1770` (this decision + the seed slice), `#1767` (Paper copy defects — same strings, same surfaces), ADR-0038 (Paper UI is canonical), ADR-0008 (novice-first product legibility), ADR-0011/ADR-0053 (token substrates — the *visual* half of the same "copy and chrome are the product" argument)

## Context

Every user-visible string in `frontend/taskdeck-web` is hardcoded English, inline in the SFC that
renders it. The maintainer wants Italian and Spanish selectable in the product.

Two things make this harder than a normal i18n retrofit:

1. **The copy is the product.** Taskdeck's Paper skin does not write neutral UI English. It writes
   `Nothing waiting. Good.`, `What's on your mind, quickly?`, `Drop the thought. It will sit here,
   untouched, until you triage it.` That register — short sentences, warm, lowercase-leaning,
   occasionally a sentence fragment used as a full stop — is a deliberate design property, and a
   literal machine translation destroys it while remaining "correct". Translation of this product
   is a *writing* task with a translation memory attached, not a string-substitution task.
2. **The surface area is large and moving.** The frontend has 33 top-level route views plus the
   Paper/Legacy dual shells (ADR-0038 froze Legacy but did not delete it). A big-bang extraction
   would touch essentially every SFC and every view spec at once, would collide with whatever else
   is in flight on those surfaces, and would be unreviewable.

There is no existing i18n machinery: no `vue-i18n`, no message catalogs, no locale in the
preferences surface, no `lang` management on `<html>`.

## Decision

### 1. Library: `vue-i18n@^11`, Composition API mode (`legacy: false`)

Adopt `vue-i18n` (v11, the current major) as the single message-resolution runtime, created once in
`src/i18n/index.ts` and installed in `src/main.ts`. Configuration:

| Option | Value | Why |
| --- | --- | --- |
| `legacy` | `false` | Composition API mode. The codebase is 100% `<script setup>`; the Options-API `$t`/`this.$t` surface would be a foreign idiom and its reactivity model (per-component `VueI18n` instances) is the one being deprecated upstream. |
| `globalInjection` | `true` | `$t` usable directly in templates without a `useI18n()` line in every SFC. Keeps the extraction diff to the template. |
| `locale` | `'en'` | English is the source and the default. |
| `fallbackLocale` | `'en'` | See §5. |
| `missingWarn` / `fallbackWarn` | `false` | See §5 — silent fallback is the specified behavior, and warnings would flood the console during a multi-release rollout. |

This is the only new runtime dependency this decision introduces.

### 2. Catalog layout: `src/locales/{en,it,es}/<surface>.ts`, one file per surface

```
src/locales/
  en/  index.ts  home.ts  inbox.ts  boards.ts  settings.ts
  it/  index.ts  home.ts  inbox.ts  boards.ts  settings.ts
  es/  index.ts  home.ts  inbox.ts  boards.ts  settings.ts
```

Each `<surface>.ts` default-exports a plain nested object; each `index.ts` composes them under a
namespace matching the file name (`home.*`, `inbox.*`, `boards.*`, `settings.*`, `common.*`). Keys
are `camelCase` and describe the *slot*, not the English text (`home.empty.body`, not
`home.nothingWaitingGood`) — otherwise renaming the English copy forces a key rename in three
catalogs.

TypeScript modules, not JSON: the guard spec and the type-checker both read them, we get free
syntax validation at build time, and per-surface files mean two agents extracting two different
surfaces do not conflict in the same file.

**There is no `common.ts` yet, on purpose.** Sharing a key across surfaces is a translation hazard —
"Create" is `Crea` as a button and `Creare` as a heading in Italian, and a shared key forces one of
the two to be wrong. The default is therefore to keep a string in its own surface catalog even when
it looks duplicated, and to introduce `common.ts` only when the *same sentence in the same
grammatical role* has appeared on three or more surfaces. At the seed, nothing has; a `common.ts`
created now would be speculative scaffolding that invites exactly the wrong kind of key reuse.

Language display names are the one deliberate exception to "all user-visible text is a catalog key":
they live as endonyms in a `LOCALE_LABELS` constant in `src/i18n/index.ts` (`English`, `Italiano`,
`Español`), because a language picker should name each language in its own language regardless of
the active locale — translating them would be the bug, not the feature.

### 3. Paper tone is a translation constraint, recorded in the catalogs

Each locale's `index.ts` carries a translator's note at the top stating the register contract:

> Match Taskdeck's Paper voice: short, warm, concrete. Prefer a fragment over a formal clause.
> Do not add exclamation marks, do not add politeness scaffolding the English does not have
> ("Please", "Kindly"), do not expand a three-word English line into a full sentence. Where the
> English is lowercase-leaning, stay lowercase-leaning in the target language *as far as that
> language's orthography permits* — Italian and Spanish capitalize far less than English in
> headings and labels, so this usually means writing them *more* naturally, not less.

The concrete consequence for `it`/`es` in this repo: sentence case everywhere (not English Title
Case), `Nothing waiting. Good.` becomes `Niente in attesa. Bene.` / `Nada pendiente. Bien.` — a
fragment answered by a fragment — and product nouns that are Taskdeck concepts with a Paper meaning
(**Inbox**, **Board**, **Paper**, **Nib**, **Composer**) are translated where a natural everyday
word exists in the target language (`bacheca` / `tablero` for board) and left in English where the
word is the product's own coinage (`Nib`, `Composer`, `Paper`).

This note is guidance, not machinery. Nothing enforces tone; the guard in §6 enforces only
structure. Tone is a review responsibility.

### 4. Pluralization and date/number formatting via `Intl`, not per-locale plumbing

- **Plurals** use `vue-i18n`'s pipe syntax (`no captures | one capture | {count} captures`) resolved
  by the library's per-locale plural rules. Italian and Spanish share English's two-form
  (one/other) cardinal system, so the seed locales need exactly the same segment count as `en` —
  which is what the guard checks. A locale with a different cardinal system (Polish, Russian,
  Arabic) would need its own `pluralRules` entry; that is out of scope until such a locale exists,
  and the guard's segment-parity rule must be relaxed to a per-locale expectation at that point,
  not before.
- **Dates and numbers** are formatted with the platform `Intl` API
  (`toLocaleDateString(locale)`, `Intl.NumberFormat`) driven by the active locale, **not** by
  `vue-i18n`'s `datetimeFormats`/`numberFormats` catalogs. Rationale: `Intl` already has the CLDR
  data, needs no per-locale catalog maintenance, and is what the codebase already calls
  (`new Date(board.createdAt).toLocaleDateString()`). Adding a second formatting authority would
  mean two places to keep in sync for zero gain. Note the residual: the active *app* locale is not
  the browser locale. `BoardsListView` preserves the browser's *region* only when the browser's
  primary language subtag matches the active app locale (an `en-GB` browser on app-locale `en` still
  formats `19/08/2026`). But the app locale defaults to `en` with no `navigator.language` detection,
  so a user on a `de-DE`/`fr-FR`/`pt-BR` browser who never opens the language switcher moves from
  their own date format to US format (`19.8.2026` → `8/19/2026`). Seeding the default locale from
  `navigator.language` is a product decision, out of scope for this seed and tracked as follow-up.

### 5. `en` is the fallback, and missing keys fall back *silently*

`fallbackLocale: 'en'` with `missingWarn: false` / `fallbackWarn: false`. During a rollout that is
explicitly incremental, a partially-translated surface is the *expected* state, not an error state:
a user on `it` visiting a not-yet-extracted surface sees English, which is exactly right, and must
not see a console full of warnings or a raw key path.

The cost is that a genuinely missing key is invisible at runtime: it renders as its raw key path
with no console warning (`missingWarn: false`). The §6 guard does **not** catch this. That guard
proves the three catalogs are *structurally parallel to each other* — same keys, matching
interpolation placeholders, no blanks — so a key that a view references but that is absent from
**all three** catalogs (a typo, or an `en` string never added to any catalog) passes the guard,
passes `vue-tsc` (message keys are untyped), emits no warning, and reaches the UI as the literal key
path. The guard also does not flag an *unreferenced* catalog key. The real net for a
missing-from-`en` key today is the per-surface view specs that assert the English copy — and those
exist only for surfaces already extracted. A key-usage lint that cross-checks every referenced key
against the catalogs is the proper guard for this gap, and is tracked as follow-up.

### 6. CI guard: structural parity across catalogs, enforced by a vitest spec

`src/tests/i18n/catalogs.spec.ts` flattens all three catalogs and fails on:

1. **Missing keys** — every `en` key must exist in `it` and `es`.
2. **Extra keys** — `it`/`es` must not contain keys `en` lacks (a stale key after an `en` rename).
3. **Empty or whitespace-only values** in any locale.
4. **Interpolation-placeholder mismatch** — the *set* of `{placeholders}` must be identical across
   locales for a given key. Order may differ (word order changes between languages); membership may
   not.
5. **Plural-segment mismatch** — a key whose `en` value uses `|` must have the same segment count in
   `it`/`es` (see the §4 caveat).

The guard covers whatever is in the catalogs — it grows automatically with each extracted surface
and needs no per-surface registration.

### 7. Language preference lives in the existing preferences mechanism

The appearance/preferences surface (`AppearanceSettingsView.vue`) already owns theme selection, and
that preference is held by a Pinia store persisting to `localStorage` (`paperThemeStore`,
`td.paper.mode.v2`). Language follows the identical pattern: `store/localeStore.ts`, key
`td.locale.v1`, with the same validate-on-read / default-on-garbage discipline, and the same
"apply" action that pushes the value into the runtime (here: `i18n.global.locale` plus
`<html lang>`, mirroring how `paperThemeStore` pushes a class onto `<body>`).

Language is a *client display* preference, not account data: it is not sent to or stored by the
backend, and there is no server-side user-preference table for it to live in. If a future
requirement makes the preference follow the account across devices, it moves to the backend
profile — that is a separate decision, and this one does not block it.

### 8. Rollout order: surface by surface, core loop first

1. **Step 1 — SHIPPED** (PR `#1841`, `#1770`): Home, Inbox, Boards, plus the Preferences language
   switcher itself.
2. **Step 2 — SHIPPED** (PR `#1852`, hardened by PR `#1869`/`#1857`): **Review**. Deliberately
   deferred out of the seed because three Review-surface PRs (`#1830`, `#1825`, `#1835`) landed or
   were landing the same day and a parallel copy extraction there would have collided line-for-line
   for no gain; those settled and Review shipped later the same day — 258 keys per locale, and it
   completes the core loop. `#1869` fixed the two strings that were resolved *once* into a plain
   `ref` at fetch/error time and therefore froze whichever locale produced them: per decision 2,
   the fetch path stores the raw wire key and the relabel happens inside the exposed `computed`.
3. **Step 3 — BLOCKED on `#1858`**: the secondary surfaces (Today, Calendar, Activity, Archive,
   Settings sub-views, Notifications, Agents, `CohortDashboard`, and the shared
   `components/ui/Td*` primitives). The blocker is budget, not design. Step 2 alone took total JS
   from 1163.18 KB to 1201.15 KB, and the CI gate in `scripts/ci/check-bundle-size.mjs` was
   **deliberately raised 1200 → 1250 KB** to admit it — a recorded budget decision with its
   rationale in a dated comment on the `MAX_TOTAL_JS_KB` constant, not a threshold quietly moved to
   make a build pass. That headroom is not expected to survive the remaining surfaces, so how the
   rollout is budgeted against this gate — including whether the gate should measure the *eager*
   graph instead, which would make lazy loading a real lever — is decided in `#1858` **before**
   step 3 starts.
4. **Legacy (Obsidian) shells are not extracted.** ADR-0038 froze them; spending translation budget
   on a frozen skin is waste. If Legacy is ever unfrozen, it inherits the catalogs the Paper
   surfaces already built. Note that three *shared* components extracted in step 2
   (`ApplyToBoardDialog.vue`, `useReviewActions.ts`, `useReviewProposals.ts`) are mounted by the
   Legacy Review shell too, so Legacy inherits translated copy at those seams already; `en` is
   byte-identical, so the default-locale rendering is unchanged.

Within a surface, extraction is all-or-nothing for that surface's *own* copy: half-extracted
surfaces are how catalogs rot.

**Still open regardless of rollout position:** translation *quality*. Per Consequences below, the
catalog guard proves structural parity only — nothing mechanical proves the Italian and Spanish are
good. Native review of `src/locales/{it,es}/*` remains a human item tracked on `#1770`, and the
locales should not be advertised as finished until it happens.

## Alternatives Considered

**A custom `useTranslations()` composable over plain objects.** Zero dependency, ~40 lines. Rejected:
we would immediately reimplement plural selection, interpolation, fallback chains and reactive
locale switching — the parts that are actually hard — and get a bespoke API that no contributor and
no tooling recognizes. `vue-i18n` is the ecosystem default for exactly this shape of problem.

**`vue-i18n` in legacy (Options API) mode.** Rejected: contradicts a `<script setup>`-only codebase
and is the deprecated path upstream.

**`@intlify/unplugin-vue-i18n` with SFC `<i18n>` blocks (co-located per-component messages).**
Attractive for locality, rejected on two counts: a translator would have to open 30 SFCs to
translate a surface, and the guard in §6 could no longer read the catalogs as data — it would need
to parse SFC custom blocks. Central per-surface catalogs keep translation and validation as
file-level operations.

**Big-bang extraction of all 33 views in one PR.** Rejected: unreviewable, guaranteed to conflict
with concurrent surface work (which it in fact would have on Review this very day), and it front-
loads the entire translation cost before a single locale has been validated in a real UI.

**JSON catalogs.** Rejected: no type-checking, no comments — and the translator's note in §3 is a
comment. TypeScript modules cost nothing here because the catalogs are pure data.

**Server-persisted language preference.** Rejected for the seed: no user-preference table exists for
it, appearance already sets the local-preference precedent, and it would add a backend migration to
a frontend-only slice. Explicitly reversible (§7).

## Consequences

**Positive**
- Italian and Spanish are real, selectable, live-switching languages on the core surfaces.
- A structural guard makes "someone added an English string and forgot the other two catalogs" a red
  CI check rather than a silent English leak a Spanish user discovers.
- The per-surface catalog layout means future surface extraction is additive and conflict-free
  between parallel agents.
- Extraction pulls Paper's tone-heavy copy out of the templates and into one reviewable place per
  surface — which also gives `#1767` (copy defects) a single file to fix instead of a template hunt.

**Negative**
- Mixed-language UI is now a normal, expected state until the rollout finishes. A user on `it` will
  see Italian Home and English Calendar. This is the deliberate cost of not doing a big bang.
- Every future user-visible string on an extracted surface is a three-catalog edit. That is real
  friction, and the guard makes it non-optional.
- One new runtime dependency in the bundle.
- Translation quality has no mechanical gate. The guard proves the catalogs are *structurally*
  parallel; only review proves they are *good Italian and Spanish*.

**Neutral**
- The unit-test setup installs the i18n plugin globally, so existing specs asserting literal English
  copy keep passing unchanged (`en` is default) — the assertions stay exactly as meaningful as they
  were.
- `<html lang>` is now managed, which is an accessibility improvement independent of translation.

## References

- Issue `#1770` — Seed an i18n translation layer (vue-i18n) with Italian and Spanish locales
- Issue `#1767` — Paper copy defects on the same surfaces
- ADR-0038 — Paper UI canonical, Legacy frozen
- ADR-0051 — Autonomous backlog admission and merge authority
- `frontend/taskdeck-web/src/i18n/index.ts`, `src/locales/`, `src/store/localeStore.ts`
- `frontend/taskdeck-web/src/tests/i18n/catalogs.spec.ts` — the §6 guard
- [vue-i18n Composition API guide](https://vue-i18n.intlify.dev/guide/advanced/composition.html)
