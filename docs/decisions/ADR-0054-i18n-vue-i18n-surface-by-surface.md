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
  en/  index.ts  common.ts  home.ts  inbox.ts  boards.ts  settings.ts
  it/  index.ts  common.ts  home.ts  inbox.ts  boards.ts  settings.ts
  es/  index.ts  common.ts  home.ts  inbox.ts  boards.ts  settings.ts
```

Each `<surface>.ts` default-exports a plain nested object; each `index.ts` composes them under a
namespace matching the file name (`home.*`, `inbox.*`, `boards.*`, `settings.*`, `common.*`). Keys
are `camelCase` and describe the *slot*, not the English text (`home.empty.body`, not
`home.nothingWaitingGood`) — otherwise renaming the English copy forces a key rename in three
catalogs.

TypeScript modules, not JSON: the guard spec and the type-checker both read them, we get free
syntax validation at build time, and per-surface files mean two agents extracting two different
surfaces do not conflict in the same file.

**`common.ts` is deliberately small** and holds only strings whose meaning is genuinely surface-
independent (`common.actions.cancel`, `common.actions.create`, `common.states.loading`). Sharing a
key across surfaces is a translation hazard — "Create" is `Crea` as a button and `Creare` as a
heading in Italian — so the default is to duplicate a key into its surface catalog and only promote
it to `common` when the *same sentence in the same grammatical role* appears on three or more
surfaces.

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
  mean two places to keep in sync for zero gain.

### 5. `en` is the fallback, and missing keys fall back *silently*

`fallbackLocale: 'en'` with `missingWarn: false` / `fallbackWarn: false`. During a rollout that is
explicitly incremental, a partially-translated surface is the *expected* state, not an error state:
a user on `it` visiting a not-yet-extracted surface sees English, which is exactly right, and must
not see a console full of warnings or a raw key path.

The cost is that a genuinely missing key is invisible at runtime. That cost is paid off by the CI
guard in §6, which makes it a build-time failure instead — the right place for it.

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

1. **Now (`#1770`)** — Home, Inbox, Boards, plus the Preferences language switcher itself.
2. **Next** — **Review**. Deliberately deferred out of the seed: three Review-surface PRs
   (`#1830`, `#1825`, `#1835`) landed or were landing the same day, and a parallel copy extraction
   there would have collided line-for-line for no gain. Review completes the core loop and is the
   named next slice.
3. **Then** — the secondary surfaces (Today, Calendar, Activity, Archive, Settings sub-views,
   Notifications, Agents, and the shared `components/ui/Td*` primitives).
4. **Legacy (Obsidian) shells are not extracted.** ADR-0038 froze them; spending translation budget
   on a frozen skin is waste. If Legacy is ever unfrozen, it inherits the catalogs the Paper
   surfaces already built.

Within a surface, extraction is all-or-nothing for that surface's *own* copy: half-extracted
surfaces are how catalogs rot.

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
