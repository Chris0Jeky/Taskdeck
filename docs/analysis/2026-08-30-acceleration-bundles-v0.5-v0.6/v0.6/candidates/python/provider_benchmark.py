
from __future__ import annotations

from dataclasses import dataclass, asdict
from typing import Iterable, Optional
import json
import math


@dataclass(frozen=True)
class BenchmarkObservation:
    fixture_id: str
    processor_id: str
    wall_time_ms: int
    peak_ram_mb: Optional[int]
    estimated_cost: Optional[float]
    wer: Optional[float]
    der: Optional[float]
    alignment_error_ms: Optional[float]
    ocr_accuracy: Optional[float]
    outcome: str


def _mean(values: Iterable[Optional[float]]) -> Optional[float]:
    known = [float(value) for value in values if value is not None and math.isfinite(float(value))]
    return None if not known else sum(known) / len(known)


def summarize(observations: list[BenchmarkObservation], method_date: str) -> dict:
    by_processor: dict[str, list[BenchmarkObservation]] = {}
    for item in observations:
        by_processor.setdefault(item.processor_id, []).append(item)

    processors = []
    for processor_id, items in sorted(by_processor.items()):
        processors.append({
            "processorId": processor_id,
            "fixtureCount": len(items),
            "successCount": sum(1 for item in items if item.outcome == "completed"),
            "meanWallTimeMs": _mean(item.wall_time_ms for item in items),
            "meanPeakRamMb": _mean(item.peak_ram_mb for item in items),
            "meanEstimatedCost": _mean(item.estimated_cost for item in items),
            "meanWer": _mean(item.wer for item in items),
            "meanDer": _mean(item.der for item in items),
            "meanAlignmentErrorMs": _mean(item.alignment_error_ms for item in items),
            "meanOcrAccuracy": _mean(item.ocr_accuracy for item in items),
            "unknownCostCount": sum(1 for item in items if item.estimated_cost is None),
        })

    return {
        "schemaVersion": 1,
        "methodDate": method_date,
        "observationCount": len(observations),
        "processors": processors,
    }


def main() -> int:
    import argparse
    parser = argparse.ArgumentParser(description="Summarize processor benchmark observations")
    parser.add_argument("observations")
    parser.add_argument("--method-date", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()
    raw = json.loads(open(args.observations, encoding="utf-8").read())
    observations = [BenchmarkObservation(
        fixture_id=item["fixtureId"],
        processor_id=item["processorId"],
        wall_time_ms=int(item["wallTimeMs"]),
        peak_ram_mb=item.get("peakRamMb"),
        estimated_cost=item.get("estimatedCost"),
        wer=item.get("wer"),
        der=item.get("der"),
        alignment_error_ms=item.get("alignmentErrorMs"),
        ocr_accuracy=item.get("ocrAccuracy"),
        outcome=item["outcome"],
    ) for item in raw]
    result = summarize(observations, args.method_date)
    rendered = json.dumps(result, indent=2, sort_keys=True)
    if args.output:
        open(args.output, "w", encoding="utf-8").write(rendered + "\n")
    else:
        print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
