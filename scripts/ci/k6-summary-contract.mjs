const HARD_GATE_METRICS = [
  {
    name: "http_req_failed",
    values: ["rate"],
    valueDomains: { rate: { minimum: 0, maximum: 1 } },
    thresholds: ["rate<0.01"],
    thresholdChecks: { "rate<0.01": { valueName: "rate", operator: "<", limit: 0.01 } },
  },
  {
    name: "checks",
    values: ["rate"],
    valueDomains: { rate: { minimum: 0, maximum: 1 } },
    thresholds: ["rate>0.99"],
    thresholdChecks: { "rate>0.99": { valueName: "rate", operator: ">", limit: 0.99 } },
  },
  {
    name: "http_req_duration",
    values: ["p(95)", "p(99)"],
    valueDomains: {
      "p(95)": { minimum: 0 },
      "p(99)": { minimum: 0 },
    },
    thresholds: ["p(95)<2000", "p(99)<2500"],
    thresholdChecks: {
      "p(95)<2000": { valueName: "p(95)", operator: "<", limit: 2000 },
      "p(99)<2500": { valueName: "p(99)", operator: "<", limit: 2500 },
    },
  },
  {
    name: "http_req_duration{workload:board-read}",
    values: ["p(95)"],
    valueDomains: { "p(95)": { minimum: 0 } },
    thresholds: ["p(95)<900"],
    thresholdChecks: { "p(95)<900": { valueName: "p(95)", operator: "<", limit: 900 } },
  },
  {
    name: "http_req_duration{workload:board-write}",
    values: ["p(95)"],
    valueDomains: { "p(95)": { minimum: 0 } },
    thresholds: ["p(95)<2200"],
    thresholdChecks: { "p(95)<2200": { valueName: "p(95)", operator: "<", limit: 2200 } },
  },
];

export const K6_HARD_GATE_METRICS = Object.freeze(HARD_GATE_METRICS);

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function readK6MetricValueEvidence(metric, valueName) {
  if (!isRecord(metric)) return { value: undefined };

  const hasNestedValue = isRecord(metric.values) && Object.hasOwn(metric.values, valueName);

  // k6 0.49 --summary-export flattens trend values and calls rate values "value".
  const exportedName = valueName === "rate" ? "value" : valueName;
  const hasExportedValue = Object.hasOwn(metric, exportedName);
  const nestedValue = hasNestedValue ? metric.values[valueName] : undefined;
  const exportedValue = metric[exportedName];

  if (hasNestedValue && hasExportedValue) {
    if (!Number.isFinite(nestedValue) || !Number.isFinite(exportedValue)) {
      return { error: "has duplicate nested/flattened evidence that is not finite" };
    }

    if (nestedValue !== exportedValue) {
      return { error: `has conflicting nested (${nestedValue}) and flattened (${exportedValue}) evidence` };
    }

    return { value: nestedValue };
  }

  const value = hasNestedValue ? nestedValue : exportedValue;
  return { value: Number.isFinite(value) ? value : undefined };
}

export function readK6MetricValue(metric, valueName) {
  return readK6MetricValueEvidence(metric, valueName).value;
}

export function readK6ThresholdOk(result) {
  // k6 0.49 --summary-export writes a breach flag: false means the threshold passed.
  if (typeof result === "boolean") return !result;

  // Keep compatibility with handleSummary/analyzer-shaped threshold results.
  if (isRecord(result) && typeof result.ok === "boolean") return result.ok;

  return undefined;
}

function thresholdPasses(value, check) {
  if (check.operator === "<") return value < check.limit;
  if (check.operator === ">") return value > check.limit;
  throw new Error(`Unsupported k6 hard-gate operator: ${check.operator}`);
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
      const evidence = readK6MetricValueEvidence(metric, valueName);
      if (evidence.error) {
        return `metric "${requirement.name}" value "${valueName}" ${evidence.error}`;
      }

      const value = evidence.value;
      if (value === undefined) {
        return `metric "${requirement.name}" must contain finite numeric value "${valueName}"`;
      }

      const domain = requirement.valueDomains[valueName];
      if (value < domain.minimum || (domain.maximum !== undefined && value > domain.maximum)) {
        const upperBound = domain.maximum === undefined ? "" : ` and at most ${domain.maximum}`;
        return `metric "${requirement.name}" value "${valueName}" must be at least ${domain.minimum}${upperBound}`;
      }
    }

    if (!isRecord(metric.thresholds)) {
      return `metric "${requirement.name}" must contain a thresholds object`;
    }

    for (const thresholdName of requirement.thresholds) {
      if (!Object.hasOwn(metric.thresholds, thresholdName)) {
        return `metric "${requirement.name}" must contain threshold "${thresholdName}"`;
      }

      const observedOk = readK6ThresholdOk(metric.thresholds[thresholdName]);
      if (observedOk === undefined) {
        return `metric "${requirement.name}" threshold "${thresholdName}" must contain boolean result evidence`;
      }

      const check = requirement.thresholdChecks[thresholdName];
      const value = readK6MetricValue(metric, check.valueName);
      const expectedOk = thresholdPasses(value, check);
      if (observedOk !== expectedOk) {
        return `metric "${requirement.name}" threshold "${thresholdName}" contradicts value "${check.valueName}"=${value}; numeric evidence implies ${expectedOk ? "pass" : "breach"}`;
      }
    }
  }

  const aggregateDuration = summary.metrics.http_req_duration;
  const aggregateP95 = readK6MetricValue(aggregateDuration, "p(95)");
  const aggregateP99 = readK6MetricValue(aggregateDuration, "p(99)");
  if (aggregateP95 > aggregateP99) {
    return `metric "http_req_duration" value "p(95)"=${aggregateP95} must not exceed "p(99)"=${aggregateP99}`;
  }

  return null;
}
