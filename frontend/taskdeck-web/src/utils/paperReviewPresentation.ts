export interface PaperReviewTitlePart {
  text: string
  emphasis?: boolean
}

/**
 * Split a proposal summary into ordinary and quoted title segments.
 *
 * Paper Review currently renders quoted phrases as the emphasized portion of
 * the title. Straight quotes are normalized to the same curly-quote pair used
 * by the Paper copy; unmatched quotes leave the remaining text ordinary rather
 * than dropping any text.
 */
export function splitQuotedSummary(summary: string): PaperReviewTitlePart[] {
  if (!summary) return [{ text: '' }]
  const parts: PaperReviewTitlePart[] = []
  let cursor = 0

  while (cursor < summary.length) {
    const straight = summary.indexOf('"', cursor)
    const curly = summary.indexOf('“', cursor)
    const startCandidates = [straight, curly].filter((index) => index >= 0)
    if (startCandidates.length === 0) break
    const start = Math.min(...startCandidates)
    const endQuote = summary[start] === '“' ? '”' : '"'
    const end = summary.indexOf(endQuote, start + 1)
    if (end < 0) break

    if (start > cursor) {
      parts.push({ text: summary.slice(cursor, start) })
    }
    parts.push({ text: `“${summary.slice(start + 1, end)}”`, emphasis: true })
    cursor = end + 1
  }

  if (cursor < summary.length) {
    parts.push({ text: summary.slice(cursor) })
  }
  return parts.length > 0 ? parts : [{ text: summary, emphasis: true }]
}
