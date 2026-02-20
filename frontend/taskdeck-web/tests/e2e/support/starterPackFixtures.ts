import type { APIRequestContext, Page } from '@playwright/test'
import { expect } from '@playwright/test'
import type { StarterPackApplyResult, StarterPackManifest } from '../../../src/types/starter-packs'

const API_BASE_URL = 'http://localhost:5000/api'

interface AuthUser {
  id: string
  username: string
  email: string
}

export interface AuthResult {
  token: string
  user: AuthUser
}

interface BoardDto {
  id: string
  name: string
}

export interface ColumnDto {
  id: string
  boardId: string
  name: string
  position: number
  wipLimit: number | null
}

export interface LabelDto {
  id: string
  boardId: string
  name: string
  colorHex: string
}

interface CardLabelDto {
  id: string
  name: string
  colorHex: string
}

export interface CardDto {
  id: string
  boardId: string
  columnId: string
  title: string
  description: string
  position: number
  labels: CardLabelDto[]
}

type FixtureScenarioKey = 'small' | 'medium' | 'edge'

interface FixtureScenario {
  boardName: string
  manifest: StarterPackManifest
  dryRun: boolean
  expectConflicts: boolean
  preseedColumns?: Array<{
    name: string
    position: number
    wipLimit: number | null
  }>
}

export interface StarterPackFixtureBootstrapOptions {
  fixture: FixtureScenarioKey
  page?: Page
}

export interface StarterPackFixtureBootstrapResult {
  auth: AuthResult
  boardId: string
  boardName: string
  manifest: StarterPackManifest
  applyResult: StarterPackApplyResult
  httpStatus: number
}

const FIXTURE_SCENARIOS: Record<FixtureScenarioKey, FixtureScenario> = {
  small: {
    boardName: 'E2E Fixture Small',
    dryRun: false,
    expectConflicts: false,
    manifest: {
      schemaVersion: '1.0',
      packId: 'qa-fixture-small',
      displayName: 'QA Fixture Small',
      description: 'Deterministic starter-pack fixture for smoke-sized test states.',
      compatibility: {
        minTaskdeckVersion: '1.0.0',
        requiredFeatures: ['boards', 'labels', 'cards'],
      },
      tags: ['starter', 'fixture', 'small'],
      labels: [
        {
          name: 'priority-high',
          color: '#E85D5D',
          description: 'High urgency item',
        },
      ],
      columns: [
        {
          name: 'Backlog',
          position: 0,
        },
        {
          name: 'Done',
          position: 1,
        },
      ],
      templates: [],
      seedCards: [
        {
          title: 'Seed: triage first task',
          description: 'Deterministic small-fixture seed card.',
          columnName: 'Backlog',
          labels: ['priority-high'],
        },
      ],
    },
  },
  medium: {
    boardName: 'E2E Fixture Medium',
    dryRun: false,
    expectConflicts: false,
    manifest: {
      schemaVersion: '1.0',
      packId: 'qa-fixture-medium',
      displayName: 'QA Fixture Medium',
      description: 'Deterministic starter-pack fixture for medium complexity E2E flows.',
      compatibility: {
        minTaskdeckVersion: '1.0.0',
        requiredFeatures: ['boards', 'labels', 'cards'],
      },
      tags: ['starter', 'fixture', 'medium'],
      labels: [
        {
          name: 'priority-high',
          color: '#E85D5D',
          description: 'High urgency item',
        },
        {
          name: 'bug',
          color: '#DC2626',
          description: 'Defect tracking',
        },
        {
          name: 'needs-review',
          color: '#2563EB',
          description: 'Requires review',
        },
      ],
      columns: [
        {
          name: 'Backlog',
          position: 0,
        },
        {
          name: 'In Progress',
          position: 1,
          wipLimit: 4,
        },
        {
          name: 'Review',
          position: 2,
          wipLimit: 2,
        },
        {
          name: 'Done',
          position: 3,
        },
      ],
      templates: [
        {
          templateId: 'bug-report',
          title: 'Bug Report',
          description: 'Template metadata for deterministic fixture coverage.',
          checklist: ['Steps', 'Expected', 'Actual'],
        },
      ],
      seedCards: [
        {
          title: 'Seed: investigate flaky test',
          description: 'Track a known flaky path.',
          columnName: 'Backlog',
          templateId: 'bug-report',
          labels: ['bug', 'priority-high'],
        },
        {
          title: 'Seed: review release notes',
          description: 'Pre-release verification notes.',
          columnName: 'Review',
          labels: ['needs-review'],
        },
        {
          title: 'Seed: harden auth pipeline',
          description: 'High-priority hardening card.',
          columnName: 'In Progress',
          labels: ['priority-high'],
        },
      ],
    },
  },
  edge: {
    boardName: 'E2E Fixture Edge',
    dryRun: true,
    expectConflicts: true,
    preseedColumns: [
      {
        name: 'Occupied Lane',
        position: 0,
        wipLimit: null,
      },
    ],
    manifest: {
      schemaVersion: '1.0',
      packId: 'qa-fixture-edge-conflict',
      displayName: 'QA Fixture Edge Conflict',
      description: 'Deterministic conflict fixture pack for dry-run edge-case assertions.',
      compatibility: {
        minTaskdeckVersion: '1.0.0',
        requiredFeatures: ['boards'],
      },
      tags: ['starter', 'fixture', 'edge'],
      labels: [],
      columns: [
        {
          name: 'Backlog',
          position: 0,
        },
      ],
      templates: [],
      seedCards: [],
    },
  },
}

function deepCloneManifest(manifest: StarterPackManifest): StarterPackManifest {
  return JSON.parse(JSON.stringify(manifest)) as StarterPackManifest
}

function hasConflicts(result: StarterPackApplyResult): boolean {
  if (typeof result.hasConflicts === 'boolean') {
    return result.hasConflicts
  }

  return result.conflicts.length > 0
}

export async function registerUserSession(
  request: APIRequestContext,
  scope: string,
): Promise<AuthResult> {
  const unique = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const username = `e2e-${scope}-${unique}`
  const email = `${username}@taskdeck.local`
  const password = 'E2ePassword123!'

  const response = await request.post(`${API_BASE_URL}/auth/register`, {
    data: { username, email, password },
  })

  expect(response.ok()).toBeTruthy()
  return await response.json() as AuthResult
}

export async function attachSessionToPage(page: Page, auth: AuthResult): Promise<void> {
  await page.addInitScript((payload: { token: string; session: { userId: string; username: string; email: string } }) => {
    localStorage.setItem('taskdeck_token', payload.token)
    localStorage.setItem('taskdeck_session', JSON.stringify(payload.session))
  }, {
    token: auth.token,
    session: {
      userId: auth.user.id,
      username: auth.user.username,
      email: auth.user.email,
    },
  })
}

async function createBoard(
  request: APIRequestContext,
  token: string,
  boardName: string,
): Promise<string> {
  const response = await request.post(`${API_BASE_URL}/boards`, {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name: boardName,
      description: 'Deterministic starter-pack fixture board',
    },
  })

  expect(response.ok()).toBeTruthy()
  const payload = await response.json() as BoardDto
  expect(payload.id).toBeTruthy()
  return payload.id
}

async function createColumn(
  request: APIRequestContext,
  token: string,
  boardId: string,
  name: string,
  position: number,
  wipLimit: number | null,
): Promise<void> {
  const response = await request.post(`${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/columns`, {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      boardId,
      name,
      position,
      wipLimit,
    },
  })

  expect(response.status()).toBe(201)
}

export async function bootstrapStarterPackFixture(
  request: APIRequestContext,
  options: StarterPackFixtureBootstrapOptions,
): Promise<StarterPackFixtureBootstrapResult> {
  const scenario = FIXTURE_SCENARIOS[options.fixture]
  const manifest = deepCloneManifest(scenario.manifest)
  const auth = await registerUserSession(request, `fixture-${options.fixture}`)

  if (options.page) {
    await attachSessionToPage(options.page, auth)
  }

  const boardId = await createBoard(request, auth.token, scenario.boardName)
  if (scenario.preseedColumns) {
    for (const column of scenario.preseedColumns) {
      await createColumn(request, auth.token, boardId, column.name, column.position, column.wipLimit)
    }
  }

  const applyResponse = await request.post(`${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/starter-packs/apply`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: {
      manifest,
      dryRun: scenario.dryRun,
    },
  })

  expect(applyResponse.status()).toBe(200)
  const applyResult = await applyResponse.json() as StarterPackApplyResult
  expect(hasConflicts(applyResult)).toBe(scenario.expectConflicts)

  if (!scenario.dryRun && !scenario.expectConflicts) {
    expect(applyResult.applied).toBeTruthy()
  } else {
    expect(applyResult.applied).toBeFalsy()
  }

  return {
    auth,
    boardId,
    boardName: scenario.boardName,
    manifest,
    applyResult,
    httpStatus: applyResponse.status(),
  }
}

export async function getBoardColumns(
  request: APIRequestContext,
  token: string,
  boardId: string,
): Promise<ColumnDto[]> {
  const response = await request.get(`${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/columns`, {
    headers: { Authorization: `Bearer ${token}` },
  })

  expect(response.ok()).toBeTruthy()
  return await response.json() as ColumnDto[]
}

export async function getBoardLabels(
  request: APIRequestContext,
  token: string,
  boardId: string,
): Promise<LabelDto[]> {
  const response = await request.get(`${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/labels`, {
    headers: { Authorization: `Bearer ${token}` },
  })

  expect(response.ok()).toBeTruthy()
  return await response.json() as LabelDto[]
}

export async function getBoardCards(
  request: APIRequestContext,
  token: string,
  boardId: string,
): Promise<CardDto[]> {
  const response = await request.get(`${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`, {
    headers: { Authorization: `Bearer ${token}` },
  })

  expect(response.ok()).toBeTruthy()
  return await response.json() as CardDto[]
}
