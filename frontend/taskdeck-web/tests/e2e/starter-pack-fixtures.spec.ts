import { expect, test } from '@playwright/test'
import {
  bootstrapStarterPackFixture,
  getBoardCards,
  getBoardColumns,
  getBoardLabels,
} from './support/starterPackFixtures'

test('small fixture should bootstrap deterministic board state from manifest', async ({ request }) => {
  const seeded = await bootstrapStarterPackFixture(request, { fixture: 'small' })

  expect(seeded.httpStatus).toBe(200)
  expect(seeded.applyResult.packId).toBe('qa-fixture-small')
  expect(seeded.applyResult.dryRun).toBeFalsy()

  const columns = await getBoardColumns(request, seeded.auth.token, seeded.boardId)
  expect(columns.map((column) => `${column.position}:${column.name}`)).toEqual([
    '0:Backlog',
    '1:Done',
  ])

  const labels = await getBoardLabels(request, seeded.auth.token, seeded.boardId)
  expect(labels.map((label) => label.name)).toEqual(['priority-high'])

  const cards = await getBoardCards(request, seeded.auth.token, seeded.boardId)
  expect(cards.map((card) => card.title)).toEqual(['Seed: triage first task'])
  expect(cards[0].labels.map((label) => label.name)).toEqual(['priority-high'])
})

test('medium fixture should bootstrap richer deterministic board state from manifest', async ({ request }) => {
  const seeded = await bootstrapStarterPackFixture(request, { fixture: 'medium' })

  expect(seeded.httpStatus).toBe(200)
  expect(seeded.applyResult.packId).toBe('qa-fixture-medium')
  expect(seeded.applyResult.dryRun).toBeFalsy()

  const columns = await getBoardColumns(request, seeded.auth.token, seeded.boardId)
  expect(columns.map((column) => `${column.position}:${column.name}`)).toEqual([
    '0:Backlog',
    '1:In Progress',
    '2:Review',
    '3:Done',
  ])

  const labels = await getBoardLabels(request, seeded.auth.token, seeded.boardId)
  expect(labels.map((label) => label.name).sort()).toEqual([
    'bug',
    'needs-review',
    'priority-high',
  ])

  const cards = await getBoardCards(request, seeded.auth.token, seeded.boardId)
  const columnNameById = new Map(columns.map((column) => [column.id, column.name]))
  const cardPlacements = cards
    .map((card) => `${card.title}@${columnNameById.get(card.columnId)}`)
    .sort()

  expect(cardPlacements).toEqual([
    'Seed: harden auth pipeline@In Progress',
    'Seed: investigate flaky test@Backlog',
    'Seed: review release notes@Review',
  ])
})

test('edge fixture should provide deterministic dry-run conflict state from manifest', async ({ request }) => {
  const seeded = await bootstrapStarterPackFixture(request, { fixture: 'edge' })

  expect(seeded.httpStatus).toBe(200)
  expect(seeded.applyResult.packId).toBe('qa-fixture-edge-conflict')
  expect(seeded.applyResult.dryRun).toBeTruthy()
  expect(seeded.applyResult.applied).toBeFalsy()
  expect(seeded.applyResult.conflicts.some((conflict) => conflict.code === 'ColumnPositionConflict')).toBeTruthy()

  const columns = await getBoardColumns(request, seeded.auth.token, seeded.boardId)
  expect(columns.map((column) => `${column.position}:${column.name}`)).toEqual(['0:Occupied Lane'])

  const labels = await getBoardLabels(request, seeded.auth.token, seeded.boardId)
  expect(labels).toHaveLength(0)

  const cards = await getBoardCards(request, seeded.auth.token, seeded.boardId)
  expect(cards).toHaveLength(0)
})
