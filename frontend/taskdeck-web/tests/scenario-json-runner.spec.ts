import { describe, expect, it } from 'vitest'

import {
  listJsonScenarioIds,
  loadJsonScenario,
  runJsonScenario,
  validateScenarioJson,
} from '../scripts/scenario-json-runner.mjs'

function createFakeApi({
  columns = [{ id: 'column-backlog', name: 'Backlog' }],
  labels = [{ id: 'label-bug', name: 'bug' }],
}: {
  columns?: Array<{ id: string; name: string }>
  labels?: Array<{ id: string; name: string }>
} = {}) {
  const boards = new Map<string, { id: string; name: string }>()
  const cards: Array<{ id: string; columnId: string; title: string }> = []

  return {
    async get(path: string) {
      if (path === '/boards/board-1') {
        return boards.get('board-1') ?? { id: 'board-1', name: 'Demo Board' }
      }

      if (path === '/boards/board-1/columns') {
        return columns
      }

      if (path === '/boards/board-1/labels') {
        return labels
      }

      if (path === '/boards/board-1/cards') {
        return cards
      }

      throw new Error(`Unexpected GET ${path}`)
    },

    async post(path: string, { body }: { body?: Record<string, unknown> } = {}) {
      if (path === '/boards') {
        const board = {
          id: 'board-1',
          name: String(body?.name ?? 'Demo Board'),
        }
        boards.set(board.id, board)
        return board
      }

      if (path === '/boards/board-1/cards') {
        const card = {
          id: `card-${cards.length + 1}`,
          columnId: String(body?.columnId ?? ''),
          title: String(body?.title ?? ''),
        }
        cards.push(card)
        return card
      }

      if (path === '/capture/items') {
        return {
          id: 'capture-1',
          boardId: String(body?.boardId ?? ''),
          text: String(body?.text ?? ''),
          status: 'Pending',
        }
      }

      throw new Error(`Unexpected POST ${path}`)
    },
  }
}

describe('scenario json runner determinism', () => {
  it('fails fast on unresolved template references instead of silently blanking them', async () => {
    await expect(
      runJsonScenario({
        api: createFakeApi(),
        config: { uiBaseUrl: 'http://localhost:5173' },
        scenario: {
          version: 1,
          id: 'template-failure',
          title: 'Template Failure',
          steps: [
            {
              type: 'createBoard',
              alias: 'board',
              name: 'DEMO: Template Failure',
            },
            {
              type: 'createCard',
              board: 'board',
              column: 'Backlog',
              title: 'Broken ${cards.missing.id}',
            },
          ],
        },
      }),
    ).rejects.toThrow('Unresolved scenario template expression "cards.missing.id"')
  })

  it('fails when a scenario references labels that the target board does not expose', async () => {
    await expect(
      runJsonScenario({
        api: createFakeApi(),
        config: { uiBaseUrl: 'http://localhost:5173' },
        scenario: {
          version: 1,
          id: 'missing-label',
          title: 'Missing Label',
          steps: [
            {
              type: 'createBoard',
              alias: 'board',
              name: 'DEMO: Missing Label',
            },
            {
              type: 'createCard',
              board: 'board',
              column: 'Backlog',
              title: 'Needs a missing label',
              labels: ['priority-high'],
            },
          ],
        },
      }),
    ).rejects.toThrow('Labels not found on board board-1: "priority-high"')
  })

  it('fails when a scenario references a label name that is ambiguous on the target board', async () => {
    await expect(
      runJsonScenario({
        api: createFakeApi({
          labels: [
            { id: 'label-bug-1', name: 'bug' },
            { id: 'label-bug-2', name: 'bug' },
          ],
        }),
        config: { uiBaseUrl: 'http://localhost:5173' },
        scenario: {
          version: 1,
          id: 'duplicate-label',
          title: 'Duplicate Label',
          steps: [
            {
              type: 'createBoard',
              alias: 'board',
              name: 'DEMO: Duplicate Label',
            },
            {
              type: 'createCard',
              board: 'board',
              column: 'Backlog',
              title: 'Ambiguous label',
              labels: ['bug'],
            },
          ],
        },
      }),
    ).rejects.toThrow('Label names are ambiguous on board board-1: "bug"')
  })

  it('fails when a scenario references a column name that is ambiguous on the target board', async () => {
    await expect(
      runJsonScenario({
        api: createFakeApi({
          columns: [
            { id: 'column-backlog-1', name: 'Backlog' },
            { id: 'column-backlog-2', name: 'Backlog' },
          ],
        }),
        config: { uiBaseUrl: 'http://localhost:5173' },
        scenario: {
          version: 1,
          id: 'duplicate-column',
          title: 'Duplicate Column',
          steps: [
            {
              type: 'createBoard',
              alias: 'board',
              name: 'DEMO: Duplicate Column',
            },
            {
              type: 'createCard',
              board: 'board',
              column: 'Backlog',
              title: 'Ambiguous column',
            },
          ],
        },
      }),
    ).rejects.toThrow('Column name "Backlog" is ambiguous on board board-1: found 2 matches')
  })

  it('rejects duplicate aliases in the same namespace', () => {
    expect(() =>
      validateScenarioJson({
        version: 1,
        id: 'duplicate-alias',
        title: 'Duplicate Alias',
        steps: [
          {
            type: 'createCard',
            alias: 'shared-card',
            board: 'board-1',
            column: 'Backlog',
            title: 'One',
          },
          {
            type: 'createCard',
            alias: 'shared-card',
            board: 'board-1',
            column: 'Backlog',
            title: 'Two',
          },
        ],
      }),
    ).toThrow('duplicates Step[0] (createCard) in cards')
  })

  it('rejects duplicate card aliases introduced by updateCard steps', () => {
    expect(() =>
      validateScenarioJson({
        version: 1,
        id: 'duplicate-update-alias',
        title: 'Duplicate Update Alias',
        steps: [
          {
            type: 'createCard',
            alias: 'shared-card',
            board: 'board-1',
            column: 'Backlog',
            title: 'One',
          },
          {
            type: 'updateCard',
            alias: 'shared-card',
            board: 'board-1',
            card: 'card-1',
            patch: { title: 'Updated' },
          },
        ],
      }),
    ).toThrow('duplicates Step[0] (createCard) in cards')
  })

  it('rejects duplicate card aliases introduced by moveCard steps', () => {
    expect(() =>
      validateScenarioJson({
        version: 1,
        id: 'duplicate-move-alias',
        title: 'Duplicate Move Alias',
        steps: [
          {
            type: 'createCard',
            alias: 'shared-card',
            board: 'board-1',
            column: 'Backlog',
            title: 'One',
          },
          {
            type: 'moveCard',
            alias: 'shared-card',
            board: 'board-1',
            card: 'card-1',
            toColumn: 'Done',
          },
        ],
      }),
    ).toThrow('duplicates Step[0] (createCard) in cards')
  })

  it('treats waitForCaptureOutcome as llm-dependent by default so --skip-llm remains deterministic', async () => {
    const summary = await runJsonScenario({
      api: createFakeApi(),
      config: { uiBaseUrl: 'http://localhost:5173' },
      scenario: {
        version: 1,
        id: 'skip-capture-outcome',
        title: 'Skip Capture Outcome',
        steps: [
          {
            type: 'createBoard',
            alias: 'board',
            name: 'DEMO: Skip Capture Outcome',
          },
          {
            type: 'createCapture',
            alias: 'capture',
            board: 'board',
            text: 'Customer says checkout fails.',
          },
          {
            type: 'waitForCaptureOutcome',
            capture: 'capture',
          },
        ],
      },
      options: {
        skipLlm: true,
      },
    })

    expect(summary.results.steps).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          step: '3:waitForCaptureOutcome',
          status: 'skipped',
          reason: '--skip-llm',
        }),
      ]),
    )
  })
})

describe('scenario json runner shipped scenario contracts', () => {
  const starterPackContracts = {
    'board-blueprint-engineering-sprint': {
      columns: new Set(['Backlog', 'In Progress', 'Review', 'Done']),
      labels: new Set(['priority-high', 'bug', 'tech-debt']),
    },
    'board-blueprint-support-triage': {
      columns: new Set(['Inbox', 'Triage', 'In Progress', 'Resolved']),
      labels: new Set(['customer-impact', 'sla-risk', 'waiting-on-customer']),
    },
    'board-blueprint-content-calendar': {
      columns: new Set(['Ideas', 'Drafting', 'Review', 'Scheduled']),
      labels: new Set(['needs-draft', 'needs-review', 'publish-week']),
    },
    'board-blueprint-client-onboarding': {
      columns: new Set(['New Intake', 'Waiting on Client', 'Ready for Review', 'In Progress', 'Completed']),
      labels: new Set(['client-action', 'internal-review', 'waiting-on-client']),
    },
  } as const

  it('rejects scenario paths that escape the scenarios-json directory', async () => {
    await expect(loadJsonScenario('../package')).rejects.toThrow('Scenario path resolves outside scenarios-json')
  })

  it('keeps each shipped JSON scenario structurally valid', async () => {
    const scenarioIds = await listJsonScenarioIds()

    for (const scenarioId of scenarioIds) {
      const scenario = await loadJsonScenario(scenarioId)
      expect(validateScenarioJson(scenario)).toBe(true)
    }
  })

  it('keeps starter-pack-backed scenarios aligned with starter-pack columns and labels', async () => {
    const scenarioIds = await listJsonScenarioIds()

    for (const scenarioId of scenarioIds) {
      const scenario = await loadJsonScenario(scenarioId)
      const starterPackStep = scenario.steps.find((step) => step.type === 'applyStarterPack')
      const starterPackStepIndex = scenario.steps.findIndex((step) => step.type === 'applyStarterPack')
      if (!starterPackStep) {
        continue
      }

      const contract =
        starterPackContracts[starterPackStep.starterPackId as keyof typeof starterPackContracts] ?? null
      expect(contract, `${scenario.id} should declare a known starter-pack contract`).not.toBeNull()
      expect(starterPackStepIndex).toBeGreaterThanOrEqual(0)

      const createCardSteps = scenario.steps.filter((step) => step.type === 'createCard')
      for (const step of createCardSteps) {
        const stepIndex = scenario.steps.indexOf(step)
        expect(
          stepIndex,
          `${scenario.id} should apply ${starterPackStep.starterPackId} before creating "${step.alias ?? step.title}"`,
        ).toBeGreaterThan(starterPackStepIndex)
        expect(
          contract!.columns.has(step.column),
          `${scenario.id} step "${step.alias ?? step.title}" references unknown column "${step.column}"`,
        ).toBe(true)

        for (const labelName of step.labels ?? []) {
          expect(
            contract!.labels.has(labelName),
            `${scenario.id} step "${step.alias ?? step.title}" references unknown label "${labelName}"`,
          ).toBe(true)
        }
      }

      const moveCardSteps = scenario.steps.filter((step) => step.type === 'moveCard')
      for (const step of moveCardSteps) {
        const stepIndex = scenario.steps.indexOf(step)
        expect(
          stepIndex,
          `${scenario.id} should apply ${starterPackStep.starterPackId} before move step "${step.alias ?? step.toColumn}"`,
        ).toBeGreaterThan(starterPackStepIndex)
        expect(
          contract!.columns.has(step.toColumn),
          `${scenario.id} move step references unknown column "${step.toColumn}"`,
        ).toBe(true)
      }
    }
  })
})
