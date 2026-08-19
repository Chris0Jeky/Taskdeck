/**
 * Italian message catalog.
 *
 * Register (ADR-0054 §3) — match Taskdeck's Paper voice: short, warm,
 * concrete. Prefer a fragment over a formal clause. Do not add exclamation
 * marks, do not add politeness scaffolding the English does not have ("Per
 * favore", "Gentilmente"), do not expand a three-word English line into a full
 * sentence. Use sentence case, never English Title Case — Italian capitalizes
 * far less in headings and labels, so following Italian orthography is also
 * what keeps the lowercase-leaning Paper feel.
 *
 * Surfaces not present here fall back to English silently, by design.
 */
import boards from './boards'
import home from './home'
import inbox from './inbox'
import review from './review'
import settings from './settings'

export default {
  home,
  inbox,
  boards,
  review,
  settings,
}
