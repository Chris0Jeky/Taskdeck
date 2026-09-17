import assert from 'node:assert/strict'
import test from 'node:test'
import { buildActorAssignments } from './actor-board-assignments.js'

test('assigns one isolated board to every VU while reusing the requested account pool', () => {
  const assignments = buildActorAssignments(20, 6)

  assert.equal(assignments.length, 20)
  assert.deepEqual(
    assignments.slice(0, 8).map(({ vuIndex, accountIndex, boardIndex }) => ({ vuIndex, accountIndex, boardIndex })),
    [
      { vuIndex: 1, accountIndex: 0, boardIndex: 0 },
      { vuIndex: 2, accountIndex: 1, boardIndex: 1 },
      { vuIndex: 3, accountIndex: 2, boardIndex: 2 },
      { vuIndex: 4, accountIndex: 3, boardIndex: 3 },
      { vuIndex: 5, accountIndex: 4, boardIndex: 4 },
      { vuIndex: 6, accountIndex: 5, boardIndex: 5 },
      { vuIndex: 7, accountIndex: 0, boardIndex: 6 },
      { vuIndex: 8, accountIndex: 1, boardIndex: 7 },
    ],
  )
  assert.deepEqual(
    [...new Set(assignments.map(({ accountIndex }) => accountIndex))],
    [0, 1, 2, 3, 4, 5],
  )
  assert.equal(new Set(assignments.map(({ boardIndex }) => boardIndex)).size, 20)
})

test('does not provision accounts that no VU can use', () => {
  assert.deepEqual(buildActorAssignments(2, 6).map(({ accountIndex }) => accountIndex), [0, 1])
})

test('rejects invalid load dimensions before setup can create data', () => {
  assert.throws(() => buildActorAssignments(0, 6), /vus must be a positive integer/)
  assert.throws(() => buildActorAssignments(20, 0), /userPool must be a positive integer/)
})
