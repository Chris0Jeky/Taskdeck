
export type RegisterKind = 'action' | 'decision' | 'question' | 'risk'

export type MeetingRegisterEntry = {
  candidateId: string
  kind: RegisterKind
  speakerLabel?: string | null
  participantUserId?: string | null
  speakerResolutionCode: string
  evidenceAnchorIds: string[]
  state: string
}

export type MeetingRegisterGroup = {
  kind: RegisterKind
  entries: MeetingRegisterEntry[]
}

const order: RegisterKind[] = ['action', 'decision', 'question', 'risk']

export function groupMeetingEntries(
  entries: readonly MeetingRegisterEntry[],
): MeetingRegisterGroup[] {
  return order.map((kind) => ({
    kind,
    entries: entries.filter((entry) => entry.kind === kind),
  }))
}

export function hasUnresolvedSpeaker(entry: MeetingRegisterEntry): boolean {
  return Boolean(entry.speakerLabel) && !entry.participantUserId
}
