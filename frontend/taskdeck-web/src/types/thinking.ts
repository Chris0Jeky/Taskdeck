export type ThinkingKind = 'note' | 'question' | 'options' | 'steps' | 'thread'
export interface ThinkingItem { id: string; text: string; completed: boolean; linkedCardId?: string | null }
export interface ThinkingLayer {
  id: string
  kind: ThinkingKind
  title: string
  body: string
  items: ThinkingItem[]
  selectedOptionId: string | null
}
export interface ThinkingDeck { cardId: string; revision: number; schemaVersion: number; layers: ThinkingLayer[]; canWrite: boolean }
