export type PaperViewRoot = {
  readonly view: string
  readonly selector: string
  /** Explicitly undefined when a Paper root has no page-header eyebrow. */
  readonly eyebrow: string | undefined
}

/**
 * Paper-idiom top-level view roots protected by the static substrate and eyebrow
 * guards. `paperViewLegacySubstrate.spec.ts` independently scans
 * `src/views/*.vue` for roots that opt into Paper ink, so adding a qualifying
 * view without registering it here fails rather than silently shrinking guard
 * coverage. Keeping the eyebrow selector beside its view prevents the two
 * guards from drifting; requiring the property makes "no eyebrow" deliberate.
 */
export const PAPER_VIEW_ROOTS = [
  // #1780 / PR #1807 — the four high-traffic roots the substrate guard began with.
  { view: 'MetricsView.vue', selector: '.paper-metrics', eyebrow: '.paper-metrics__eyebrow' },
  { view: 'ActivityView.vue', selector: '.paper-activity', eyebrow: '.paper-activity__eyebrow' },
  { view: 'CalendarView.vue', selector: '.paper-calendar', eyebrow: '.paper-calendar__eyebrow' },
  { view: 'BoardsListView.vue', selector: '.paper-boards', eyebrow: '.paper-boards__eyebrow' },

  // #1775 / #1813 — the Saved Views restyle.
  { view: 'SavedViewsView.vue', selector: '.paper-views', eyebrow: '.paper-views__eyebrow' },

  // PR #1808 — the six Settings roots.
  { view: 'ApiKeySettingsView.vue', selector: '.paper-api-keys', eyebrow: '.paper-api-keys__eyebrow' },
  { view: 'AppearanceSettingsView.vue', selector: '.paper-appearance', eyebrow: '.paper-appearance__eyebrow' },
  { view: 'BoardAccessView.vue', selector: '.paper-access', eyebrow: '.paper-access__eyebrow' },
  { view: 'ExportImportView.vue', selector: '.paper-portability', eyebrow: '.paper-portability__eyebrow' },
  { view: 'NotificationPreferencesView.vue', selector: '.paper-prefs', eyebrow: '.paper-prefs__eyebrow' },
  { view: 'ProfileSettingsView.vue', selector: '.paper-profile', eyebrow: '.paper-profile__eyebrow' },

  // PR #1810 — the secondary Paper-idiom views.
  { view: 'AgentRunDetailView.vue', selector: '.paper-run-detail', eyebrow: '.paper-run-detail__eyebrow' },
  { view: 'AgentRunsView.vue', selector: '.paper-agent-runs', eyebrow: '.paper-agent-runs__eyebrow' },
  { view: 'AgentsView.vue', selector: '.paper-agents', eyebrow: '.paper-agents__eyebrow' },
  { view: 'ArchiveView.vue', selector: '.paper-archive', eyebrow: '.paper-archive__eyebrow' },
  { view: 'AutomationChatView.vue', selector: '.paper-chat', eyebrow: undefined },
  { view: 'AutomationQueueView.vue', selector: '.paper-queue', eyebrow: '.paper-queue__eyebrow' },
  { view: 'DevToolsView.vue', selector: '.paper-devtools', eyebrow: undefined },
  { view: 'IntegrationsView.vue', selector: '.paper-int', eyebrow: '.paper-int__eyebrow' },
  { view: 'NotFoundView.vue', selector: '.paper-not-found', eyebrow: '.paper-not-found__eyebrow' },
  { view: 'NotificationInboxView.vue', selector: '.paper-notifications', eyebrow: '.paper-notifications__eyebrow' },
  { view: 'OpsConsoleView.vue', selector: '.paper-ops', eyebrow: '.paper-ops__eyebrow' },
] as const satisfies ReadonlyArray<PaperViewRoot>
