
from __future__ import annotations

from dataclasses import dataclass
from typing import Optional
import hashlib
import json


@dataclass(frozen=True)
class ShadowObservation:
    observation_id: str
    policy_version: str
    eligible: bool
    would_allow: bool
    target_correct: Optional[bool]
    permission_correct: Optional[bool]
    human_approved_unchanged: Optional[bool]
    false_action: Optional[bool]
    correct_no_action: Optional[bool]
    compensation_supported: bool
    compensation_simulated: bool
    boundary_violation: bool
    kill_switch_observed: bool


def stable_cohort(subject: str, policy_version: str, salt_version: str,
                  canary_basis_points: int, holdout_basis_points: int) -> str:
    if min(canary_basis_points, holdout_basis_points) < 0:
        raise ValueError("negative basis points")
    if canary_basis_points + holdout_basis_points > 10_000:
        raise ValueError("basis points exceed 10000")
    digest = hashlib.sha256(f"{salt_version}:{policy_version}:{subject}".encode()).digest()
    bucket = int.from_bytes(digest[:2], "big") % 10_000
    if bucket < canary_basis_points:
        return "canary"
    if bucket < canary_basis_points + holdout_basis_points:
        return "holdout"
    return "shadow"


def _labelled_rate(items: list[ShadowObservation], attr: str) -> dict:
    labelled = [item for item in items if getattr(item, attr) is not None]
    return {
        "numerator": sum(1 for item in labelled if getattr(item, attr) is True),
        "denominator": len(labelled),
        "unknown": len(items) - len(labelled),
    }


def build_report(items: list[ShadowObservation], method_date: str) -> dict:
    return {
        "schemaVersion": 1,
        "methodDate": method_date,
        "sampleSize": len(items),
        "eligibleCount": sum(item.eligible for item in items),
        "wouldAllowCount": sum(item.would_allow for item in items),
        "targetAccuracy": _labelled_rate(items, "target_correct"),
        "permissionAccuracy": _labelled_rate(items, "permission_correct"),
        "unchangedAcceptance": _labelled_rate(items, "human_approved_unchanged"),
        "falseActionAdjudication": _labelled_rate(items, "false_action"),
        "correctNoAction": _labelled_rate(items, "correct_no_action"),
        "compensationSimulationSuccess": sum(
            1 for item in items if item.compensation_supported and item.compensation_simulated
        ),
        "boundaryViolationCount": sum(item.boundary_violation for item in items),
        "killSwitchFailureCount": sum(not item.kill_switch_observed for item in items),
        "authorizationRecommendation": "human-decision-required",
    }


def main() -> int:
    import argparse
    parser = argparse.ArgumentParser(description="Build a shadow authority evidence report")
    parser.add_argument("observations")
    parser.add_argument("--method-date", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()
    raw = json.loads(open(args.observations, encoding="utf-8").read())
    items = [ShadowObservation(
        observation_id=item["observationId"],
        policy_version=item["policyVersion"],
        eligible=bool(item["eligible"]),
        would_allow=bool(item["wouldAllow"]),
        target_correct=item.get("targetCorrect"),
        permission_correct=item.get("permissionCorrect"),
        human_approved_unchanged=item.get("humanApprovedUnchanged"),
        false_action=item.get("falseAction"),
        correct_no_action=item.get("correctNoAction"),
        compensation_supported=bool(item["compensationSupported"]),
        compensation_simulated=bool(item["compensationSimulated"]),
        boundary_violation=bool(item["boundaryViolation"]),
        kill_switch_observed=bool(item["killSwitchObserved"]),
    ) for item in raw]
    result = build_report(items, args.method_date)
    rendered = json.dumps(result, indent=2, sort_keys=True)
    if args.output:
        open(args.output, "w", encoding="utf-8").write(rendered + "\n")
    else:
        print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
