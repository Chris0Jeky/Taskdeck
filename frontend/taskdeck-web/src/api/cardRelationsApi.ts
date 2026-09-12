import http from './http'
import type { Proposal } from '../types/automation'
import type { BoardCardRelations, CreateRelationProposalInput, RelationProposalParameters } from '../types/cardRelations'

const proposalRoute = '/automation/proposals'

function relationSummary(action: 'add-relation' | 'remove-relation', relationType: string): string {
  const verb = action === 'add-relation' ? 'Add' : 'Remove'
  return `${verb} ${relationType} card relation`
}

function makeOperation(
  actionType: 'add-relation' | 'remove-relation',
  input: CreateRelationProposalInput,
) {
  const parameters: RelationProposalParameters = {
    boardId: input.boardId,
    cardId: input.cardId,
    relatedCardId: input.relatedCardId,
    relationType: input.relationType,
    expectedRevision: input.expectedRevision,
  }

  return {
    sequence: 0,
    actionType,
    targetType: 'card',
    targetId: input.cardId,
    parameters: JSON.stringify(parameters),
    idempotencyKey: crypto.randomUUID(),
  }
}

async function createProposal(
  actionType: 'add-relation' | 'remove-relation',
  input: CreateRelationProposalInput,
): Promise<Proposal> {
  const { data } = await http.post<Proposal>(proposalRoute, {
    sourceType: 'Manual',
    summary: relationSummary(actionType, input.relationType),
    riskLevel: 'Low',
    correlationId: crypto.randomUUID(),
    boardId: input.boardId,
    operations: [makeOperation(actionType, input)],
  })
  return data
}

export const cardRelationsApi = {
  async get(boardId: string): Promise<BoardCardRelations> {
    return (await http.get<BoardCardRelations>(`/boards/${encodeURIComponent(boardId)}/relations`)).data
  },

  addProposal(input: CreateRelationProposalInput): Promise<Proposal> {
    return createProposal('add-relation', input)
  },

  removeProposal(input: CreateRelationProposalInput): Promise<Proposal> {
    return createProposal('remove-relation', input)
  },
}
