import type { StarterPackCatalogEntry } from '../types/starter-packs'

export const starterPackCatalog: StarterPackCatalogEntry[] = [
  {
    id: 'engineering-onboarding',
    title: 'Engineering Onboarding',
    summary: 'Kick-start a new engineering board with triage labels and delivery columns.',
    highlights: ['Backlog/In Progress/Done workflow', 'Bug template with checklist', 'Starter seed card'],
    manifest: {
      schemaVersion: '1.0',
      packId: 'engineering-onboarding',
      displayName: 'Engineering Onboarding',
      description: 'Baseline workflow for engineering kickoff and bug triage.',
      compatibility: {
        minTaskdeckVersion: '1.0.0',
        requiredFeatures: ['boards', 'labels', 'cards'],
      },
      tags: ['starter', 'engineering'],
      labels: [
        { name: 'priority-high', color: '#E85D5D', description: 'High urgency work' },
        { name: 'blocked', color: '#6B7280', description: 'Blocked until dependency clears' },
      ],
      columns: [
        { name: 'Backlog', position: 0 },
        { name: 'In Progress', position: 1, wipLimit: 3 },
        { name: 'Done', position: 2 },
      ],
      templates: [
        {
          templateId: 'bug-report',
          title: 'Bug Report',
          description: 'Standard bug triage template.',
          checklist: ['Reproduction steps', 'Expected behavior', 'Actual behavior'],
        },
      ],
      seedCards: [
        {
          title: 'Set up sprint board',
          description: 'Create initial sprint scope and assign owners.',
          columnName: 'Backlog',
          templateId: 'bug-report',
          labels: ['priority-high'],
        },
      ],
    },
  },
  {
    id: 'project-kickoff-lite',
    title: 'Project Kickoff Lite',
    summary: 'Simple project setup focused on planning, execution, and review.',
    highlights: ['Three-step delivery flow', 'Planning template', 'Kickoff seed tasks'],
    manifest: {
      schemaVersion: '1.0',
      packId: 'project-kickoff-lite',
      displayName: 'Project Kickoff Lite',
      description: 'Lean setup for small project delivery teams.',
      compatibility: {
        minTaskdeckVersion: '1.0.0',
        requiredFeatures: ['boards', 'cards'],
      },
      tags: ['starter', 'project'],
      labels: [
        { name: 'owner-needed', color: '#2563EB', description: 'Needs an assignee' },
        { name: 'customer-visible', color: '#059669', description: 'Visible to external stakeholders' },
      ],
      columns: [
        { name: 'Plan', position: 0 },
        { name: 'Build', position: 1 },
        { name: 'Review', position: 2 },
      ],
      templates: [
        {
          templateId: 'planning-card',
          title: 'Planning Card',
          description: 'Capture scope and milestones before work starts.',
          checklist: ['Objective', 'Milestones', 'Owner'],
        },
      ],
      seedCards: [
        {
          title: 'Define project goals',
          description: 'Document target outcomes and guardrails.',
          columnName: 'Plan',
          templateId: 'planning-card',
          labels: ['customer-visible'],
        },
      ],
    },
  },
  {
    id: 'content-ops-starter',
    title: 'Content Ops Starter',
    summary: 'Editorial workflow for planning, drafting, and publishing content.',
    highlights: ['Editorial board columns', 'Content brief template', 'Publishing seed card'],
    manifest: {
      schemaVersion: '1.0',
      packId: 'content-ops-starter',
      displayName: 'Content Ops Starter',
      description: 'Starter workflow for content planning and publishing cadence.',
      compatibility: {
        minTaskdeckVersion: '1.0.0',
        requiredFeatures: ['boards', 'labels', 'cards'],
      },
      tags: ['starter', 'content'],
      labels: [
        { name: 'needs-review', color: '#D97706', description: 'Awaiting editorial review' },
        { name: 'publish-week', color: '#7C3AED', description: 'Targeted for this week' },
      ],
      columns: [
        { name: 'Ideas', position: 0 },
        { name: 'Drafting', position: 1, wipLimit: 4 },
        { name: 'Scheduled', position: 2 },
      ],
      templates: [
        {
          templateId: 'content-brief',
          title: 'Content Brief',
          description: 'Brief template for each planned piece.',
          checklist: ['Audience', 'Angle', 'Call to action'],
        },
      ],
      seedCards: [
        {
          title: 'Plan weekly editorial slate',
          description: 'Define top three pieces and owners for the week.',
          columnName: 'Ideas',
          templateId: 'content-brief',
          labels: ['publish-week'],
        },
      ],
    },
  },
]
