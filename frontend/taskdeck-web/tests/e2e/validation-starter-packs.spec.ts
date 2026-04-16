/**
 * TST-11 Slice E: Starter pack lifecycle validation.
 *
 * Covers catalog browsing, dry-run preview, apply to empty board,
 * re-apply idempotency (conflict detection), conflict highlighting,
 * complex manifests with labels/columns/cards, multiple pack application,
 * and manifest validation.
 *
 * All scenarios use the API directly (no UI starter-pack surface yet).
 */

import { expect, test } from '@playwright/test'
import {
  API_BASE_URL,
  registerUserSession,
  type AuthResult,
} from './support/authSession'
import type { ColumnDto, LabelDto, CardDto } from './support/starterPackFixtures'

interface BoardDto {
  id: string
  name: string
}

let auth: AuthResult

test.beforeEach(async ({ request }) => {
  auth = await registerUserSession(request, 'starter-pack-val')
})

async function createBoard(
  request: import('@playwright/test').APIRequestContext,
  name: string,
): Promise<string> {
  const response = await request.post(`${API_BASE_URL}/boards`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { name, description: 'Starter pack validation board' },
  })

  expect(response.ok()).toBeTruthy()
  const board = (await response.json()) as BoardDto
  return board.id
}

async function getColumns(
  request: import('@playwright/test').APIRequestContext,
  boardId: string,
): Promise<ColumnDto[]> {
  const response = await request.get(
    `${API_BASE_URL}/boards/${boardId}/columns`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )

  expect(response.ok()).toBeTruthy()
  return (await response.json()) as ColumnDto[]
}

async function getLabels(
  request: import('@playwright/test').APIRequestContext,
  boardId: string,
): Promise<LabelDto[]> {
  const response = await request.get(
    `${API_BASE_URL}/boards/${boardId}/labels`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )

  expect(response.ok()).toBeTruthy()
  return (await response.json()) as LabelDto[]
}

async function getCards(
  request: import('@playwright/test').APIRequestContext,
  boardId: string,
): Promise<CardDto[]> {
  const response = await request.get(
    `${API_BASE_URL}/boards/${boardId}/cards`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )

  expect(response.ok()).toBeTruthy()
  return (await response.json()) as CardDto[]
}

const SMALL_MANIFEST = {
  schemaVersion: '1.0',
  packId: 'val-small',
  displayName: 'Validation Small',
  description: 'Small pack for validation tests.',
  compatibility: {
    minTaskdeckVersion: '1.0.0',
    requiredFeatures: ['boards', 'labels', 'cards'],
  },
  tags: ['validation'],
  labels: [
    { name: 'urgent', color: '#DC2626', description: 'Urgent items' },
  ],
  columns: [
    { name: 'Backlog', position: 0 },
    { name: 'Done', position: 1 },
  ],
  templates: [],
  seedCards: [
    {
      title: 'Validate: first task',
      description: 'Seed card for validation.',
      columnName: 'Backlog',
      labels: ['urgent'],
    },
  ],
}

const COMPLEX_MANIFEST = {
  schemaVersion: '1.0',
  packId: 'val-complex',
  displayName: 'Validation Complex',
  description: 'Complex pack for multi-entity validation.',
  compatibility: {
    minTaskdeckVersion: '1.0.0',
    requiredFeatures: ['boards', 'labels', 'cards'],
  },
  tags: ['validation', 'complex'],
  labels: [
    { name: 'priority-high', color: '#E85D5D', description: 'High priority' },
    { name: 'bug', color: '#DC2626', description: 'Bug tracking' },
    { name: 'needs-review', color: '#2563EB', description: 'Review needed' },
  ],
  columns: [
    { name: 'Backlog', position: 0 },
    { name: 'In Progress', position: 1, wipLimit: 4 },
    { name: 'Review', position: 2, wipLimit: 2 },
    { name: 'Done', position: 3 },
  ],
  templates: [
    {
      templateId: 'bug-template',
      title: 'Bug Report',
      description: 'Template for bug reports.',
      checklist: ['Reproduce', 'Root cause', 'Fix'],
    },
  ],
  seedCards: [
    {
      title: 'Val: investigate flaky test',
      description: 'Flaky test investigation.',
      columnName: 'Backlog',
      templateId: 'bug-template',
      labels: ['bug', 'priority-high'],
    },
    {
      title: 'Val: review release notes',
      description: 'Pre-release review.',
      columnName: 'Review',
      labels: ['needs-review'],
    },
    {
      title: 'Val: harden auth',
      description: 'Auth hardening task.',
      columnName: 'In Progress',
      labels: ['priority-high'],
    },
  ],
}

test.describe('TST11-SC-001: Browse starter pack catalog', () => {
  test('catalog returns available packs', async ({ request }) => {
    const boardId = await createBoard(request, `Catalog Browse ${Date.now()}`)

    const response = await request.get(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/catalog`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )

    expect(response.ok()).toBeTruthy()
    const catalog = await response.json()
    expect(Array.isArray(catalog)).toBeTruthy()
  })
})

test.describe('TST11-SC-002: Dry-run preview without mutation', () => {
  test('dry-run previews actions but does not apply', async ({ request }) => {
    const boardId = await createBoard(request, `DryRun Preview ${Date.now()}`)

    const response = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: SMALL_MANIFEST, dryRun: true },
      },
    )

    expect(response.ok()).toBeTruthy()
    const result = await response.json()
    expect(result.dryRun).toBeTruthy()
    expect(result.applied).toBeFalsy()
    expect(Array.isArray(result.actions)).toBeTruthy()
    expect(result.actions.length).toBeGreaterThan(0)

    // Board should be unchanged -- no columns, labels, or cards created
    const columns = await getColumns(request, boardId)
    expect(columns).toHaveLength(0)

    const labels = await getLabels(request, boardId)
    expect(labels).toHaveLength(0)

    const cards = await getCards(request, boardId)
    expect(cards).toHaveLength(0)
  })
})

test.describe('TST11-SC-003: Apply pack to empty board', () => {
  test('pack applies successfully with columns, labels, and seed cards', async ({ request }) => {
    const boardId = await createBoard(request, `Apply Empty ${Date.now()}`)

    const response = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: SMALL_MANIFEST, dryRun: false },
      },
    )

    expect(response.ok()).toBeTruthy()
    const result = await response.json()
    expect(result.applied).toBeTruthy()
    expect(result.dryRun).toBeFalsy()

    const columns = await getColumns(request, boardId)
    expect(columns.map((c) => `${c.position}:${c.name}`)).toEqual([
      '0:Backlog',
      '1:Done',
    ])

    const labels = await getLabels(request, boardId)
    expect(labels.map((l) => l.name)).toEqual(['urgent'])

    const cards = await getCards(request, boardId)
    expect(cards.map((c) => c.title)).toEqual(['Validate: first task'])
    expect(cards[0].labels.map((l) => l.name)).toEqual(['urgent'])
  })
})

test.describe('TST11-SC-004: Re-apply same pack (idempotency)', () => {
  test('re-apply detects conflicts without creating duplicates', async ({ request }) => {
    const boardId = await createBoard(request, `Reapply ${Date.now()}`)

    // First apply
    const firstResponse = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: SMALL_MANIFEST, dryRun: false },
      },
    )

    expect(firstResponse.ok()).toBeTruthy()
    const firstResult = await firstResponse.json()
    expect(firstResult.applied).toBeTruthy()

    const columnsAfterFirst = await getColumns(request, boardId)

    // Dry-run second apply
    const dryRunResponse = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: SMALL_MANIFEST, dryRun: true },
      },
    )

    expect(dryRunResponse.ok()).toBeTruthy()
    const dryRunResult = await dryRunResponse.json()
    expect(dryRunResult.dryRun).toBeTruthy()
    // Should detect conflicts (columns at same positions already exist)
    expect(dryRunResult.conflicts.length).toBeGreaterThan(0)

    // Board state unchanged — verify columns, labels, and cards
    const columnsAfterDryRun = await getColumns(request, boardId)
    expect(columnsAfterDryRun).toHaveLength(columnsAfterFirst.length)

    const labelsAfterDryRun = await getLabels(request, boardId)
    expect(labelsAfterDryRun).toHaveLength(1) // only 'urgent' from first apply

    const cardsAfterDryRun = await getCards(request, boardId)
    expect(cardsAfterDryRun).toHaveLength(1) // only seed card from first apply
  })
})

test.describe('TST11-SC-005: Conflict detection with existing content', () => {
  test('detects column position conflicts on occupied board', async ({ request }) => {
    const boardId = await createBoard(request, `Conflict ${Date.now()}`)

    // Pre-seed a column at position 0
    const colResponse = await request.post(
      `${API_BASE_URL}/boards/${boardId}/columns`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { boardId, name: 'Occupied Lane', position: 0, wipLimit: null },
      },
    )

    expect(colResponse.status()).toBe(201)

    // Dry-run with manifest that wants position 0
    const dryRunResponse = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: SMALL_MANIFEST, dryRun: true },
      },
    )

    expect(dryRunResponse.ok()).toBeTruthy()
    const dryRunResult = await dryRunResponse.json()
    expect(dryRunResult.conflicts.some((c: { code: string }) => c.code === 'ColumnPositionConflict')).toBeTruthy()

    // Non-dry-run should not apply due to blocking conflicts
    const applyResponse = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: SMALL_MANIFEST, dryRun: false },
      },
    )

    // Controller returns 409 Conflict when non-dry-run has blocking conflicts
    const applyStatus = applyResponse.status()
    expect([200, 409]).toContain(applyStatus)

    const applyResult = await applyResponse.json()
    expect(applyResult.applied).toBe(false)

    if (applyStatus === 200) {
      // If 200, the result must explicitly flag blocking conflicts
      expect(applyResult.hasBlockingConflicts).toBe(true)
    }
    // Either way, conflicts array must be populated
    expect(Array.isArray(applyResult.conflicts)).toBe(true)
    expect(applyResult.conflicts.length).toBeGreaterThan(0)
  })
})

test.describe('TST11-SC-006: Dry-run conflict severity', () => {
  test('conflict entries include severity annotation', async ({ request }) => {
    const boardId = await createBoard(request, `Severity ${Date.now()}`)

    // Pre-seed column at position 0
    await request.post(`${API_BASE_URL}/boards/${boardId}/columns`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { boardId, name: 'Existing', position: 0, wipLimit: null },
    })

    const response = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: SMALL_MANIFEST, dryRun: true },
      },
    )

    expect(response.ok()).toBeTruthy()
    const result = await response.json()

    expect(result.conflicts.length).toBeGreaterThan(0)

    for (const conflict of result.conflicts) {
      expect(conflict.code).toBeTruthy()
      expect(conflict.message).toBeTruthy()
      // Severity MUST be present on every conflict (blocking or warning)
      expect(conflict.severity).toBeTruthy()
      expect(['blocking', 'warning']).toContain(conflict.severity.toLowerCase())
    }
  })
})

test.describe('TST11-SC-007: Complex manifest with all entity types', () => {
  test('applies labels, columns with WIP limits, templates, and seed cards', async ({ request }) => {
    const boardId = await createBoard(request, `Complex ${Date.now()}`)

    const response = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: COMPLEX_MANIFEST, dryRun: false },
      },
    )

    expect(response.ok()).toBeTruthy()
    const result = await response.json()
    expect(result.applied).toBeTruthy()

    const columns = await getColumns(request, boardId)
    expect(columns.map((c) => `${c.position}:${c.name}`)).toEqual([
      '0:Backlog',
      '1:In Progress',
      '2:Review',
      '3:Done',
    ])

    // Verify WIP limits
    const inProgressCol = columns.find((c) => c.name === 'In Progress')
    expect(inProgressCol?.wipLimit).toBe(4)
    const reviewCol = columns.find((c) => c.name === 'Review')
    expect(reviewCol?.wipLimit).toBe(2)

    const labels = await getLabels(request, boardId)
    expect(labels.map((l) => l.name).sort()).toEqual([
      'bug',
      'needs-review',
      'priority-high',
    ])

    const cards = await getCards(request, boardId)
    expect(cards).toHaveLength(3)

    const columnNameById = new Map(columns.map((c) => [c.id, c.name]))
    const cardPlacements = cards
      .map((c) => `${c.title}@${columnNameById.get(c.columnId)}`)
      .sort()

    expect(cardPlacements).toEqual([
      'Val: harden auth@In Progress',
      'Val: investigate flaky test@Backlog',
      'Val: review release notes@Review',
    ])
  })
})

test.describe('TST11-SC-008: Multiple packs on same board', () => {
  test('sequential non-overlapping packs accumulate content', async ({ request }) => {
    const boardId = await createBoard(request, `MultiPack ${Date.now()}`)

    // Apply small manifest (positions 0, 1)
    const firstResponse = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: SMALL_MANIFEST, dryRun: false },
      },
    )

    expect(firstResponse.ok()).toBeTruthy()
    expect((await firstResponse.json()).applied).toBeTruthy()

    // Second manifest adds only labels (no columns) to avoid position
    // conflicts. Column positions must be contiguous starting at 0 per
    // schema validation, so a second pack cannot define non-overlapping
    // column positions. Labels accumulate without conflict.
    const secondManifest = {
      schemaVersion: '1.0',
      packId: 'val-second',
      displayName: 'Validation Second',
      description: 'Label-only second pack.',
      compatibility: {
        minTaskdeckVersion: '1.0.0',
        requiredFeatures: ['boards'],
      },
      tags: ['validation'],
      labels: [
        { name: 'feature', color: '#10B981', description: 'Feature work' },
      ],
      columns: [],
      templates: [],
      seedCards: [],
    }

    const secondResponse = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/apply`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifest: secondManifest, dryRun: false },
      },
    )

    expect(secondResponse.ok()).toBeTruthy()
    expect((await secondResponse.json()).applied).toBeTruthy()

    // First pack's columns are preserved
    const columns = await getColumns(request, boardId)
    expect(columns).toHaveLength(2)

    // Labels from both packs accumulated
    const labels = await getLabels(request, boardId)
    expect(labels.map((l) => l.name).sort()).toEqual(['feature', 'urgent'])
  })
})

test.describe('TST11-SC-009: Manifest validation endpoint', () => {
  test('validates correct manifest as valid', async ({ request }) => {
    const boardId = await createBoard(request, `ManifestVal ${Date.now()}`)

    const response = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/validate-manifest`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { manifestJson: JSON.stringify(SMALL_MANIFEST) },
      },
    )

    expect(response.ok()).toBeTruthy()
    const result = await response.json()
    expect(result.isValid).toBeTruthy()
  })

  test('rejects empty body with validation error', async ({ request }) => {
    const boardId = await createBoard(request, `ManifestEmpty ${Date.now()}`)

    const response = await request.post(
      `${API_BASE_URL}/boards/${boardId}/starter-packs/validate-manifest`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: {},
      },
    )

    expect(response.status()).toBe(400)
  })
})
