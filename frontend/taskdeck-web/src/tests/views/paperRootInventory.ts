export type PaperViewRoot = {
  readonly view: string
  readonly selector: string
  readonly eyebrow?: string
}

/**
 * The complete Paper-idiom view-root inventory used by the static substrate
 * guards. Keeping the eyebrow selector beside its view prevents the two guards
 * from silently drifting when a new Paper root is added.
 */
export const PAPER_VIEW_ROOTS = [
  { view: 'ActivityView.vue', selector: '.paper-activity', eyebrow: '.paper-activity__eyebrow' },
  { view: 'AgentRunDetailView.vue', selector: '.paper-run-detail', eyebrow: '.paper-run-detail__eyebrow' },
  { view: 'AgentRunsView.vue', selector: '.paper-agent-runs', eyebrow: '.paper-agent-runs__eyebrow' },
  { view: 'AgentsView.vue', selector: '.paper-agents', eyebrow: '.paper-agents__eyebrow' },
  { view: 'ApiKeySettingsView.vue', selector: '.paper-api-keys', eyebrow: '.paper-api-keys__eyebrow' },
  { view: 'AppearanceSettingsView.vue', selector: '.paper-appearance', eyebrow: '.paper-appearance__eyebrow' },
  { view: 'ArchiveView.vue', selector: '.paper-archive', eyebrow: '.paper-archive__eyebrow' },
  { view: 'AutomationChatView.vue', selector: '.paper-chat' },
  { view: 'AutomationQueueView.vue', selector: '.paper-queue', eyebrow: '.paper-queue__eyebrow' },
  { view: 'BoardAccessView.vue', selector: '.paper-access', eyebrow: '.paper-access__eyebrow' },
  { view: 'BoardsListView.vue', selector: '.paper-boards', eyebrow: '.paper-boards__eyebrow' },
  { view: 'CalendarView.vue', selector: '.paper-calendar', eyebrow: '.paper-calendar__eyebrow' },
  { view: 'DevToolsView.vue', selector: '.paper-devtools' },
  { view: 'ExportImportView.vue', selector: '.paper-portability', eyebrow: '.paper-portability__eyebrow' },
  { view: 'IntegrationsView.vue', selector: '.paper-int', eyebrow: '.paper-int__eyebrow' },
  { view: 'MetricsView.vue', selector: '.paper-metrics', eyebrow: '.paper-metrics__eyebrow' },
  { view: 'NotFoundView.vue', selector: '.paper-not-found', eyebrow: '.paper-not-found__eyebrow' },
  { view: 'NotificationInboxView.vue', selector: '.paper-notifications', eyebrow: '.paper-notifications__eyebrow' },
  { view: 'NotificationPreferencesView.vue', selector: '.paper-prefs', eyebrow: '.paper-prefs__eyebrow' },
  { view: 'OpsConsoleView.vue', selector: '.paper-ops', eyebrow: '.paper-ops__eyebrow' },
  { view: 'ProfileSettingsView.vue', selector: '.paper-profile', eyebrow: '.paper-profile__eyebrow' },
  { view: 'SavedViewsView.vue', selector: '.paper-views', eyebrow: '.paper-views__eyebrow' },
] as const satisfies ReadonlyArray<PaperViewRoot>
