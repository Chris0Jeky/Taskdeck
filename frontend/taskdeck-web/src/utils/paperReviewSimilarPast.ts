import type { SimilarPastResultDto } from '../api/proposalDeepReviewApi'

export interface SimilarPastRow {
  serial: string
  title: string
  verdict: 'applied' | 'rejected'
  date: string
}

/**
 * Map the similar-past wire result into the small row shape rendered by the
 * review rail. The API's verdict is open-ended, so only an explicit
 * case-insensitive `applied` value is treated as applied; every other value
 * stays on the conservative rejected side of the UI contract.
 */
export function mapSimilarPast(dto: SimilarPastResultDto): SimilarPastRow[] {
  return dto.decisions.map((decision) => ({
    serial: decision.serial,
    title: decision.title,
    verdict: decision.verdict.toLowerCase() === 'applied' ? 'applied' : 'rejected',
    date: decision.date,
  }))
}
