/** The canonical stored direction supplied by the board relation API. */
export type CardRelationType = 'blocks' | 'relates-to' | 'duplicates' | 'spawned-from'

/** `depends-on` is accepted for proposals and stored as the inverse `blocks` edge. */
export type CardRelationInputType = CardRelationType | 'depends-on'

export interface CardRelation {
  sourceCardId: string
  targetCardId: string
  relationType: CardRelationType
}

export interface BoardCardRelations {
  boardId: string
  revision: number
  relations: CardRelation[]
  /** Server-computed board capability. This never replaces endpoint validation. */
  canWrite: boolean
}

export interface RelationProposalParameters {
  boardId: string
  cardId: string
  relatedCardId: string
  relationType: CardRelationInputType
  expectedRevision: number
}

export interface CreateRelationProposalInput {
  boardId: string
  cardId: string
  relatedCardId: string
  relationType: CardRelationInputType
  expectedRevision: number
}
