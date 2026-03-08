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
]
