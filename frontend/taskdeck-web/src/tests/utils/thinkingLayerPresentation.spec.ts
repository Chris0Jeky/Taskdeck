import { describe, expect, it } from 'vitest'
import { isThinkingItemLayer, thinkingItemLabel, thinkingKinds } from '../../utils/thinkingLayerPresentation'
import type { ThinkingKind } from '../../types/thinking'

describe('thinking layer presentation', () => {
  it('keeps the add-layer catalogue aligned with the domain kinds', () => {
    expect(thinkingKinds).toEqual(['note', 'question', 'options', 'steps', 'thread'])
  })

  it.each([
    ['note', false, ''],
    ['question', false, ''],
    ['options', true, 'option'],
    ['steps', true, 'step'],
    ['thread', true, 'thought'],
  ] as const)('maps %s to its item presentation contract', (kind, hasItems, label) => {
    const typedKind: ThinkingKind = kind
    expect(isThinkingItemLayer(typedKind)).toBe(hasItems)
    expect(thinkingItemLabel(typedKind)).toBe(label)
  })
})
