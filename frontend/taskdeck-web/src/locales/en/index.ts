/**
 * English message catalog — the SOURCE of truth for every key.
 *
 * `en` is both the default locale and the fallback locale (ADR-0054 §5). A key
 * that exists here and nowhere else renders its English text silently in every
 * locale; a key that exists in `it`/`es` but NOT here is a stale key and fails
 * the catalog guard (`src/tests/i18n/catalogs.spec.ts`).
 *
 * Add a surface by adding `<surface>.ts` next to this file and one namespace
 * line below — then the same file in `../it` and `../es`. The guard picks it up
 * automatically; no registration list to update.
 */
import boardDetail from './boardDetail'
import boards from './boards'
import home from './home'
import inbox from './inbox'
import review from './review'
import settings from './settings'

export default {
  home,
  inbox,
  boards,
  boardDetail,
  review,
  settings,
}
