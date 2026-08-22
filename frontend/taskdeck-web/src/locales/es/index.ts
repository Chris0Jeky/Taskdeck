/**
 * Spanish message catalog.
 *
 * Register (ADR-0054 §3) — match Taskdeck's Paper voice: short, warm,
 * concrete. Prefer a fragment over a formal clause. Do not add exclamation
 * marks, do not add politeness scaffolding the English does not have ("Por
 * favor"), do not expand a three-word English line into a full sentence. Use
 * sentence case, never English Title Case — Spanish capitalizes far less in
 * headings and labels, so following Spanish orthography is also what keeps the
 * lowercase-leaning Paper feel. Address the user as "tú", never "usted": the
 * English is warm and direct and "usted" would make it formal.
 *
 * Surfaces not present here fall back to English silently, by design.
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
