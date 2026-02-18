export interface InputAssistOption {
  value: string
  label: string
  helperText?: string
  keywords?: string[]
}

interface InputAssistSeed {
  value: string
  label?: string
  helperText?: string
  keywords?: string[]
}

function normalize(value: string): string {
  return value.trim().toLowerCase()
}

function compactKeywordList(keywords?: string[]): string[] {
  if (!keywords) {
    return []
  }

  return keywords
    .map((keyword) => keyword.trim())
    .filter((keyword) => keyword.length > 0)
}

export function buildInputAssistOptions(seeds: InputAssistSeed[]): InputAssistOption[] {
  const byValue = new Map<string, InputAssistOption>()

  for (const seed of seeds) {
    const normalizedValue = normalize(seed.value)
    if (!normalizedValue) {
      continue
    }

    const existing = byValue.get(normalizedValue)
    if (!existing) {
      byValue.set(normalizedValue, {
        value: seed.value.trim(),
        label: seed.label?.trim() || seed.value.trim(),
        helperText: seed.helperText?.trim() || undefined,
        keywords: compactKeywordList(seed.keywords),
      })
      continue
    }

    if (!existing.helperText && seed.helperText?.trim()) {
      existing.helperText = seed.helperText.trim()
    }

    const mergedKeywords = new Set([
      ...(existing.keywords ?? []),
      ...compactKeywordList(seed.keywords),
    ])
    existing.keywords = [...mergedKeywords]
  }

  return [...byValue.values()]
}

export function filterInputAssistOptions(options: InputAssistOption[], query: string): InputAssistOption[] {
  const normalizedQuery = normalize(query)
  if (!normalizedQuery) {
    return options
  }

  return options.filter((option) => {
    const haystack = [
      option.value,
      option.label,
      option.helperText ?? '',
      ...(option.keywords ?? []),
    ].join(' ').toLowerCase()

    return haystack.includes(normalizedQuery)
  })
}
