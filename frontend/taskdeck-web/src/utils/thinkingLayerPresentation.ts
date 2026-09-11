import type { ThinkingKind } from '../types/thinking'

export const thinkingKinds = ['note', 'question', 'options', 'steps', 'thread'] as const satisfies readonly ThinkingKind[]

const thinkingItemLabels = {
  options: 'option',
  steps: 'step',
  thread: 'thought',
} as const

export type ThinkingItemLayerKind = keyof typeof thinkingItemLabels

export function isThinkingItemLayer(kind: ThinkingKind): kind is ThinkingItemLayerKind {
  return Object.prototype.hasOwnProperty.call(thinkingItemLabels, kind)
}

export function thinkingItemLabel(kind: ThinkingKind): string {
  return isThinkingItemLayer(kind) ? thinkingItemLabels[kind] : ''
}
