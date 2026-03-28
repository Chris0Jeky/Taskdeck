export interface WorkspaceSetupOption {
  id: string
  title: string
  summary: string
  helper: string
  starterPackId: string | null
}

export const workspaceSetupOptions: WorkspaceSetupOption[] = [
  {
    id: 'blank-board',
    title: 'Blank board',
    summary: 'Start from a clean board and shape the workflow yourself.',
    helper: 'Best when you already know the column flow you want.',
    starterPackId: null,
  },
  {
    id: 'engineering-sprint',
    title: 'Engineering sprint',
    summary: 'Backlog, in-progress, review, and done with sprint-ready labels.',
    helper: 'A fast path for delivery teams working in short cycles.',
    starterPackId: 'board-blueprint-engineering-sprint',
  },
  {
    id: 'client-onboarding',
    title: 'Client onboarding',
    summary: 'New intake through completion with clear client follow-up checkpoints.',
    helper: 'A business-facing flow for onboarding and document-collection work.',
    starterPackId: 'board-blueprint-client-onboarding',
  },
  {
    id: 'support-triage',
    title: 'Support triage',
    summary: 'Inbox, triage, in-progress, and resolved with SLA-aware cues.',
    helper: 'Useful when incoming work needs rapid sorting and ownership.',
    starterPackId: 'board-blueprint-support-triage',
  },
  {
    id: 'content-calendar',
    title: 'Content calendar',
    summary: 'Ideas, drafting, review, and scheduled work for publishing cadence.',
    helper: 'A structured path for editorial or campaign planning.',
    starterPackId: 'board-blueprint-content-calendar',
  },
  {
    id: 'release-checklist',
    title: 'Release checklist',
    summary: 'Planning through production with staged gates and risk labels.',
    helper: 'Useful when shipping requires QA, staging, and sign-off gates.',
    starterPackId: 'board-blueprint-release-checklist',
  },
  {
    id: 'bug-tracker',
    title: 'Bug tracker',
    summary: 'Reported, confirmed, fixing, testing, and closed with severity labels.',
    helper: 'A structured defect lifecycle for teams tracking bugs.',
    starterPackId: 'board-blueprint-bug-tracker',
  },
  {
    id: 'personal-kanban',
    title: 'Personal kanban',
    summary: 'Simple To Do, Doing, Done board with minimal labels.',
    helper: 'The fastest start for individual task tracking.',
    starterPackId: 'board-blueprint-personal-kanban',
  },
  {
    id: 'onboarding-plan',
    title: 'Onboarding plan',
    summary: 'Week-by-week progression for new team members or hires.',
    helper: 'Structure the first weeks with clear milestones and categories.',
    starterPackId: 'board-blueprint-onboarding-plan',
  },
  {
    id: 'research-project',
    title: 'Research project',
    summary: 'Explore, hypothesize, experiment, analyze, and document.',
    helper: 'A discovery-to-documentation flow for research work.',
    starterPackId: 'board-blueprint-research-project',
  },
]
