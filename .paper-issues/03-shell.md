# PAPER-03 · Shell — Sidebar + TopBar in Paper

Part of the Paper overhaul (master tracker: PAPER-00). **Blocked by:** PAPER-01, PAPER-02.

## Goal

Reskin the persistent shell (sidebar + top bar) to match the Paper specification, behind the Paper feature flag.

## Spec

### Sidebar (220–232px)
- Background `--paper-2`, right border `1px solid var(--line)`.
- Header: serif "Taskdeck" 18px medium + tk-eyebrow ("Precision Mode · active" — "active" word in `--ember`).
- Workspace switcher: paper-card chip (22px serif italic letter glyph) + workspace name + ChevronD.
- Section groups (each preceded by `tk-eyebrow` label):
  - Primary loop: Home, Today, Review (badge), Boards, Inbox (badge)
  - Workbench tools: Views, Notifications, Chat, Calendar, Metrics, Integrations, Activity, Ops
  - Meta (no eyebrow, muted): Settings, API Keys, Preferences, Shortcuts, Logout
- Items: 36px height, 6px 20px padding, mono single-letter glyph + 12.5px Inter label, optional `· N` mono badge.
- Active: 2px left border `--ember`, gradient background, glyph ember, label weight 600.
- Footer: live status pill + version mono.

### TopBar (48–56px)
- Background `--paper`, border-bottom `1px solid var(--line)`.
- Left: breadcrumb sans 12.5px, `/` separator in `--whisper`.
- Right: ⌘K trigger (paper-card chip), `SYNCED · LOCAL-FIRST` live pill, vertical hairline, Bell + Settings ghost buttons, 26px circular avatar serif italic.

## Implementation

- Create `PaperSidebar.vue` and `PaperTopBar.vue`.
- Modify `AppShell.vue` to render Paper variants when `paperThemeStore.mode !== 'off'`.
- Wire breadcrumb from `route.meta.breadcrumb`.
- ⌘K trigger emits to existing palette composable.
- Theme toggle (☼ / ☾) in sidebar footer.

## Tests
- vitest: sidebar groups; active item ember class; badge.
- vitest: topbar emits `palette:open` on ⌘K.
- Playwright: smoke load under both themes.

## Adversarial review

- [ ] No double sidebar when toggling.
- [ ] Active route detection survives nested routes.
- [ ] Theme toggle persists across reload.
- [ ] Sidebar collapses cleanly to icon-only at narrow viewports (soft contract for PAPER-11).
- [ ] No hard-coded color literals in SFC `<style>`.
- [ ] Breadcrumb truncates with ellipsis at narrow widths.
