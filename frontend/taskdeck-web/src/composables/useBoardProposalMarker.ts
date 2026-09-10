import { computed, inject, type InjectionKey, type Ref } from 'vue'

export type BoardProposalMarkers = Readonly<Record<string, string>>
export const BOARD_PROPOSAL_MARKERS: InjectionKey<Readonly<Ref<BoardProposalMarkers>>> = Symbol('board-proposal-markers')

/** Presentation only: a missing provider is the ordinary, unmodified board. */
export function useBoardProposalMarker(kind: 'card' | 'column', id: () => string) {
  const markers = inject(BOARD_PROPOSAL_MARKERS, undefined)
  return computed(() => markers?.value[`${kind}:${id().toLowerCase()}`])
}
