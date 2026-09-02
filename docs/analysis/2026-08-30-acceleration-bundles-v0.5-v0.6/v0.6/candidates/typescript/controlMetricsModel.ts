
export type RateMetric = {
  name: string
  numerator: number
  denominator: number
  unknown: number
  value: number | null
  minimumCohortMet: boolean
}

export type MetricDisplay = {
  label: string
  value: string
  detail: string
  tone: 'normal' | 'muted' | 'warning'
}

export function displayRate(metric: RateMetric): MetricDisplay {
  if (!metric.minimumCohortMet) {
    return {
      label: metric.name,
      value: 'Insufficient data',
      detail: `${metric.denominator} labelled observations, ${metric.unknown} unknown`,
      tone: 'muted',
    }
  }

  if (metric.value === null) {
    return {
      label: metric.name,
      value: 'Unknown',
      detail: `No valid denominator; ${metric.unknown} unknown`,
      tone: 'warning',
    }
  }

  return {
    label: metric.name,
    value: `${(metric.value * 100).toFixed(1)}%`,
    detail: `${metric.numerator}/${metric.denominator}; ${metric.unknown} unknown`,
    tone: 'normal',
  }
}
