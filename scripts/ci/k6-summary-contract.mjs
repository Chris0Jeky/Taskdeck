const HARD_GATE_METRICS = [
  {
    name: "http_req_failed",
    values: ["rate"],
    thresholds: ["rate<0.01"],
  },
  {
    name: "checks",
    values: ["rate"],
    thresholds: ["rate>0.99"],
  },
  {
    name: "http_req_duration",
    values: ["p(95)", "p(99)"],
    thresholds: ["p(95)<2000", "p(99)<2500"],
  },
  {
    name: "http_req_duration{workload:board-read}",
    values: ["p(95)"],
    thresholds: ["p(95)<900"],
  },
  {
    name: "http_req_duration{workload:board-write}",
    values: ["p(95)"],
    thresholds: ["p(95)<2200"],
  },
];

export const K6_HARD_GATE_METRICS = Object.freeze(HARD_GATE_METRICS);

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

export function readK6MetricValue(metric, valueName) {
  if (!isRecord(metric)) return undefined;

  const nestedValue = isRecord(metric.values) ? metric.values[valueName] : undefined;
  if (Number.isFinite(nestedValue)) return nestedValue;

  // k6 0.49 --summary-export flattens trend values and calls rate values "value".
  const exportedName = valueName === "rate" ? "value" : valueName;
  const exportedValue = metric[exportedName];
  return Number.isFinite(exportedValue) ? exportedValue : undefined;
}

export function readK6ThresholdOk(result) {
  // k6 0.49 --summary-export writes a breach flag: false means the threshold passed.
  if (typeof result === "boolean") return !result;

  // Keep compatibility with handleSummary/analyzer-shaped threshold results.
  if (isRecord(result) && typeof result.ok === "boolean") return result.ok;

  return undefined;
}

export function validateK6HardGateSummary(summary) {
  if (!isRecord(summary)) {
    return "must be a JSON object";
  }

  if (!isRecord(summary.metrics)) {
    return "must contain a metrics object";
  }

  if (Object.keys(summary.metrics).length === 0) {
    return "metrics object must not be empty";
  }

  for (const requirement of K6_HARD_GATE_METRICS) {
    const metric = summary.metrics[requirement.name];
    if (!isRecord(metric)) {
      return `must contain required metric "${requirement.name}" as an object`;
    }

    for (const valueName of requirement.values) {
      if (readK6MetricValue(metric, valueName) === undefined) {
        return `metric "${requirement.name}" must contain finite numeric value "${valueName}"`;
      }
    }

    if (!isRecord(metric.thresholds)) {
      return `metric "${requirement.name}" must contain a thresholds object`;
    }

    for (const thresholdName of requirement.thresholds) {
      if (!Object.hasOwn(metric.thresholds, thresholdName)) {
        return `metric "${requirement.name}" must contain threshold "${thresholdName}"`;
      }

      if (readK6ThresholdOk(metric.thresholds[thresholdName]) === undefined) {
        return `metric "${requirement.name}" threshold "${thresholdName}" must contain boolean result evidence`;
      }
    }
  }

  return null;
}
