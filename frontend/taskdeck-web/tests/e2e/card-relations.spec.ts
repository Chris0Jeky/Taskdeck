import { expect, test, type APIRequestContext, type APIResponse, type Page } from '@playwright/test'
import { API_BASE_URL, attachSessionToPage, registerAndAttachSession, registerUserSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { expectApplyConfirmDialog } from './support/applyConfirm'
import { assertOk } from './support/httpAsserts'

interface Card {
  id: string
  title: string
  updatedAt: string
}

interface Relation {
  sourceCardId: string
  targetCardId: string
  relationType: 'blocks' | 'relates-to' | 'duplicates' | 'spawned-from'
}

interface RelationGraph {
  revision: number
  relations: Relation[]
  canWrite: boolean
}

interface Proposal {
  id: string
}

function headers(auth: AuthResult) {
  return { Authorization: `Bearer ${auth.token}` }
}

async function responseJson<T>(response: APIResponse, operation: string): Promise<T> {
  await assertOk(response, operation)
  return response.json() as Promise<T>
}

async function createCard(request: APIRequestContext, auth: AuthResult, boardId: string, columnId: string, title: string): Promise<Card> {
  return responseJson<Card>(await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
    headers: headers(auth),
    data: { boardId, columnId, title },
  }), `Create '${title}'`)
}

async function getRelations(request: APIRequestContext, auth: AuthResult, boardId: string): Promise<RelationGraph> {
  return responseJson<RelationGraph>(await request.get(`${API_BASE_URL}/boards/${boardId}/relations`, {
    headers: headers(auth),
  }), 'Read canonical card relations')
}

async function openRelations(page: Page, boardId: string, cardId: string) {
  await page.goto(`/workspace/boards/${boardId}/cards/${cardId}/thinking`)
  const region = page.getByRole('region', { name: 'Typed card relations', exact: true })
  await region.getByRole('button', { name: 'Explore relations', exact: true }).click()
  await expect(region.getByRole('heading', { name: 'Card relations', exact: true })).toBeVisible()
  return region
}

async function proposeFromForm(page: Page, relationType: string, relatedCardId: string): Promise<Proposal> {
  const region = page.getByRole('region', { name: 'Typed card relations', exact: true })
  await region.getByLabel('Relation type', { exact: true }).selectOption(relationType)
  await region.getByLabel('Other card', { exact: true }).selectOption(relatedCardId)
  const proposed = page.waitForResponse(response =>
    response.url().endsWith('/api/automation/proposals') && response.request().method() === 'POST',
  )
  await region.getByRole('button', { name: 'Propose relation', exact: true }).click()
  return responseJson<Proposal>(await proposed, 'Create relation proposal')
}

async function approveAndApply(page: Page, proposalId: string, theme: 'paper' | 'legacy'): Promise<void> {
  await page.goto(`/workspace/review#proposal-${proposalId}`)
  if (theme === 'paper') {
    const paperDecision = page.getByTestId('decision-apply')
    await expect(paperDecision).toBeVisible()
    const approved = page.waitForResponse(response =>
      response.url().endsWith(`/api/automation/proposals/${proposalId}/approve`) && response.request().method() === 'POST',
    )
    await paperDecision.click()
    await assertOk(await approved, 'Approve relation proposal')
    await expect(paperDecision).toHaveAttribute('data-apply-phase', 'execute')
    const applied = page.waitForResponse(response =>
      response.url().endsWith(`/api/automation/proposals/${proposalId}/execute`) && response.request().method() === 'POST',
    )
    await expectApplyConfirmDialog(page, () => paperDecision.click())
    await assertOk(await applied, 'Apply relation proposal')
    return
  }

  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()
  const proposal = page.locator(`#proposal-${proposalId}`)
  await expect(proposal).toBeVisible()
  const approved = page.waitForResponse(response =>
    response.url().endsWith(`/api/automation/proposals/${proposalId}/approve`) && response.request().method() === 'POST',
  )
  await proposal.getByRole('button', { name: 'Approve for board', exact: true }).click()
  await assertOk(await approved, 'Approve relation proposal')
  await expect(proposal.getByText('Approved, ready to apply', { exact: true })).toBeVisible()
  const applied = page.waitForResponse(response =>
    response.url().endsWith(`/api/automation/proposals/${proposalId}/execute`) && response.request().method() === 'POST',
  )
  await expectApplyConfirmDialog(page, () => proposal.getByRole('button', { name: 'Apply to board', exact: true }).click())
  await assertOk(await applied, 'Apply relation proposal')
}

test('paper: depends-on is inverted, review-gated, removable, and rejects a stale graph version', async ({ page, request }, testInfo) => {
  test.setTimeout(120_000)
  const owner = await registerAndAttachSession(page, request, 'relations-paper', { theme: 'paper' })
  const boardId = await createBoardWithColumn(request, owner, `${Date.now()}-paper`, {
    boardNamePrefix: 'Typed relation browser proof',
    columnNamePrefix: 'Next',
    description: 'Isolated real API and SQLite relation QA',
  })
  const board = await responseJson<{ columns: Array<{ id: string }> }>(await request.get(`${API_BASE_URL}/boards/${boardId}`, {
    headers: headers(owner),
  }), 'Read relation board')
  const columnId = board.columns[0]!.id
  const delivery = await createCard(request, owner, boardId, columnId, 'Deliver the release')
  const preparation = await createCard(request, owner, boardId, columnId, 'Prepare the release')
  const validation = await createCard(request, owner, boardId, columnId, 'Validate the release')

  const deliveryRelations = await openRelations(page, boardId, delivery.id)
  const add = await proposeFromForm(page, 'depends-on', preparation.id)
  await expect(deliveryRelations.getByText('Proposal created.', { exact: false })).toBeVisible()
  expect((await getRelations(request, owner, boardId)).relations).toEqual([])
  await approveAndApply(page, add.id, 'paper')
  expect((await getRelations(request, owner, boardId)).relations).toEqual([
    { sourceCardId: preparation.id, targetCardId: delivery.id, relationType: 'blocks' },
  ])

  const reloadedDeliveryRelations = await openRelations(page, boardId, delivery.id)
  await expect(reloadedDeliveryRelations).toContainText('Prepare the release blocks this card')
  const preparationRelations = await openRelations(page, boardId, preparation.id)
  await expect(preparationRelations).toContainText('This card blocks Deliver the release')

  await page.setViewportSize({ width: 390, height: 844 })
  await preparationRelations.getByLabel('Relation type', { exact: true }).focus()
  await expect(preparationRelations.getByLabel('Relation type', { exact: true })).toBeFocused()
  expect(await preparationRelations.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true)
  expect(await preparationRelations.evaluate(element => {
    const bounds = element.getBoundingClientRect()
    return bounds.left >= 0 && bounds.right <= window.innerWidth + 1
  })).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('paper-relations-phone.png'), fullPage: true, animations: 'disabled' })
  await page.setViewportSize({ width: 1280, height: 844 })

  const removal = page.waitForResponse(response =>
    response.url().endsWith('/api/automation/proposals') && response.request().method() === 'POST',
  )
  await preparationRelations.getByRole('button', { name: 'Propose removing This card blocks Deliver the release', exact: true }).click()
  const removeProposal = await responseJson<Proposal>(await removal, 'Create relation removal proposal')
  expect((await getRelations(request, owner, boardId)).relations).toHaveLength(1)
  await approveAndApply(page, removeProposal.id, 'paper')
  expect((await getRelations(request, owner, boardId)).relations).toEqual([])

  const staleRelations = await openRelations(page, boardId, delivery.id)
  const observed = await getRelations(request, owner, boardId)
  await assertOk(await request.put(`${API_BASE_URL}/boards/${boardId}/dependencies`, {
    headers: headers(owner),
    data: { expectedRevision: observed.revision, edges: [{ cardId: delivery.id, dependsOnCardId: validation.id }] },
  }), 'Create competing legacy dependency')
  await staleRelations.getByLabel('Relation type', { exact: true }).selectOption('relates-to')
  await staleRelations.getByLabel('Other card', { exact: true }).selectOption(preparation.id)
  const staleDraftResponse = page.waitForResponse(response =>
    response.url().endsWith('/api/automation/proposals') && response.request().method() === 'POST',
  )
  await staleRelations.getByRole('button', { name: 'Propose relation', exact: true }).click()
  const staleProposal = await responseJson<Proposal>(await staleDraftResponse, 'Create stale relation draft')
  // Generic proposal creation stores a reviewable draft; the current graph is
  // intentionally revalidated at Review so a stale draft never reaches Apply.
  expect((await getRelations(request, owner, boardId)).relations).toEqual([
    { sourceCardId: validation.id, targetCardId: delivery.id, relationType: 'blocks' },
  ])
  await page.goto(`/workspace/review#proposal-${staleProposal.id}`)
  const rejectedApproval = page.waitForResponse(response =>
    response.url().endsWith(`/api/automation/proposals/${staleProposal.id}/approve`) && response.request().method() === 'POST',
  )
  const paperDecision = page.getByTestId('decision-apply')
  await expect(paperDecision).toBeVisible()
  await paperDecision.click()
  expect((await rejectedApproval).status()).toBe(409)
  expect((await getRelations(request, owner, boardId)).relations).toEqual([
    { sourceCardId: validation.id, targetCardId: delivery.id, relationType: 'blocks' },
  ])
})

test('legacy: adapter preserves typed links and an archived Viewer can read but not propose', async ({ page, request, browser }) => {
  test.setTimeout(120_000)
  const owner = await registerAndAttachSession(page, request, 'relations-legacy', { theme: 'legacy' })
  const boardId = await createBoardWithColumn(request, owner, `${Date.now()}-legacy`, {
    boardNamePrefix: 'Typed relation compatibility proof',
    columnNamePrefix: 'Next',
    description: 'Isolated canonical relation and legacy adapter QA',
  })
  const board = await responseJson<{ columns: Array<{ id: string }> }>(await request.get(`${API_BASE_URL}/boards/${boardId}`, {
    headers: headers(owner),
  }), 'Read compatibility board')
  const columnId = board.columns[0]!.id
  const source = await createCard(request, owner, boardId, columnId, 'Plan the migration')
  const peer = await createCard(request, owner, boardId, columnId, 'Write the migration guide')
  const prerequisite = await createCard(request, owner, boardId, columnId, 'Back up the database')

  await openRelations(page, boardId, source.id)
  const addition = await proposeFromForm(page, 'relates-to', peer.id)
  expect((await getRelations(request, owner, boardId)).relations).toEqual([])
  await approveAndApply(page, addition.id, 'legacy')
  const typedOnly = await getRelations(request, owner, boardId)
  expect(typedOnly.relations).toHaveLength(1)
  expect(typedOnly.relations[0]).toMatchObject({ relationType: 'relates-to' })
  expect([typedOnly.relations[0]!.sourceCardId, typedOnly.relations[0]!.targetCardId])
    .toEqual(expect.arrayContaining([source.id, peer.id]))

  await assertOk(await request.put(`${API_BASE_URL}/boards/${boardId}/dependencies`, {
    headers: headers(owner),
    data: { expectedRevision: typedOnly.revision, edges: [{ cardId: source.id, dependsOnCardId: prerequisite.id }] },
  }), 'Use legacy dependency adapter beside typed relation')
  const mixedGraph = await getRelations(request, owner, boardId)
  expect(mixedGraph.relations).toHaveLength(2)
  expect(mixedGraph.relations.some(relation =>
    relation.relationType === 'relates-to'
    && [relation.sourceCardId, relation.targetCardId].includes(source.id)
    && [relation.sourceCardId, relation.targetCardId].includes(peer.id),
  )).toBe(true)
  expect(mixedGraph.relations).toContainEqual({
    sourceCardId: prerequisite.id, targetCardId: source.id, relationType: 'blocks',
  })
  const legacyProjection = await responseJson<{ edges: Array<{ cardId: string; dependsOnCardId: string }> }>(
    await request.get(`${API_BASE_URL}/boards/${boardId}/dependencies`, { headers: headers(owner) }),
    'Read legacy dependency projection',
  )
  expect(legacyProjection.edges).toEqual([{ cardId: source.id, dependsOnCardId: prerequisite.id }])

  const sourceRelations = await openRelations(page, boardId, source.id)
  await expect(sourceRelations).toContainText('relates to')
  await expect(sourceRelations.getByRole('link', { name: 'Write the migration guide', exact: true })).toBeVisible()
  await expect(sourceRelations).toContainText('Back up the database blocks this card')
  const removal = page.waitForResponse(response =>
    response.url().endsWith('/api/automation/proposals') && response.request().method() === 'POST',
  )
  await sourceRelations.getByRole('button', { name: /^Propose removing .*relates to/ }).click()
  const removeProposal = await responseJson<Proposal>(await removal, 'Create typed relation removal proposal')
  await approveAndApply(page, removeProposal.id, 'legacy')
  expect((await getRelations(request, owner, boardId)).relations).toEqual([
    { sourceCardId: prerequisite.id, targetCardId: source.id, relationType: 'blocks' },
  ])

  const viewer = await registerUserSession(request, 'relations-viewer')
  await assertOk(await request.post(`${API_BASE_URL}/boards/${boardId}/access`, {
    headers: headers(owner),
    data: { boardId, userId: viewer.user.id, role: 3 },
  }), 'Grant Viewer relation access')
  const currentSource = await responseJson<Card>(await request.get(`${API_BASE_URL}/boards/${boardId}/cards/${source.id}`, {
    headers: headers(owner),
  }), 'Read card before archive')
  await assertOk(await request.post(`${API_BASE_URL}/boards/${boardId}/cards/${source.id}/archive`, {
    headers: headers(owner),
    data: { expectedUpdatedAt: currentSource.updatedAt },
  }), 'Archive connected card')

  const viewerContext = await browser.newContext({ baseURL: new URL(page.url()).origin })
  try {
    const viewerPage = await viewerContext.newPage()
    await attachSessionToPage(viewerPage, viewer, { theme: 'legacy' })
    const viewerRelations = await openRelations(viewerPage, boardId, source.id)
    await expect(viewerRelations).toContainText('This card is archived. Relations remain readable and cannot be changed.')
    await expect(viewerRelations).toContainText('Back up the database blocks this card')
    await expect(viewerRelations.getByLabel('Relation type', { exact: true })).toHaveCount(0)
    await expect(viewerRelations.getByRole('button', { name: 'Propose relation', exact: true })).toHaveCount(0)
    await expect(viewerRelations.getByRole('button', { name: /^Propose removing/ })).toHaveCount(0)
  } finally {
    await viewerContext.close()
  }
})
