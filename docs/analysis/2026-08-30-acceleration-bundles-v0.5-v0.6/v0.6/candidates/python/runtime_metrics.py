
from __future__ import annotations

from dataclasses import dataclass, asdict
from decimal import Decimal
from typing import Iterable, Optional, Sequence
import json


@dataclass(frozen=True)
class ProposalFact:
    proposal_id: str
    reviewed: bool
    approved: bool
    approved_unchanged: bool
    rejected: bool
    target_correct: Optional[bool]
    permission_correct: Optional[bool]
    false_action: Optional[bool]
    correct_no_action: Optional[bool]
    accepted_operations: int
    attributable_cost: Optional[Decimal]
    currency: Optional[str]


@dataclass(frozen=True)
class RateMetric:
    name: str
    numerator: int
    denominator: int
    unknown: int
    value: Optional[float]
    minimum_cohort_met: bool


def _rate(name: str, numerator: int, denominator: int, unknown: int, minimum: int) -> RateMetric:
    return RateMetric(
        name=name,
        numerator=numerator,
        denominator=denominator,
        unknown=unknown,
        value=None if denominator == 0 else numerator / denominator,
        minimum_cohort_met=denominator >= minimum,
    )


def unchanged_acceptance(facts: Iterable[ProposalFact], minimum: int = 20) -> RateMetric:
    reviewed = [fact for fact in facts if fact.reviewed]
    return _rate(
        "unchanged-acceptance",
        sum(1 for fact in reviewed if fact.approved_unchanged),
        len(reviewed),
        0,
        minimum,
    )


def labelled_rate(
    facts: Sequence[ProposalFact],
    attribute: str,
    name: str,
    minimum: int = 20,
) -> RateMetric:
    labelled = [fact for fact in facts if getattr(fact, attribute) is not None]
    return _rate(
        name,
        sum(1 for fact in labelled if getattr(fact, attribute) is True),
        len(labelled),
        len(facts) - len(labelled),
        minimum,
    )


def cost_per_accepted_operation(facts: Sequence[ProposalFact]) -> dict:
    currencies = {fact.currency for fact in facts if fact.attributable_cost is not None and fact.currency}
    known = [fact for fact in facts if fact.attributable_cost is not None]
    accepted = sum(fact.accepted_operations for fact in facts)
    total = sum((fact.attributable_cost or Decimal("0")) for fact in known)
    value = None
    currency = next(iter(currencies)) if len(currencies) == 1 else None
    if accepted > 0 and len(currencies) <= 1:
        value = str(total / accepted)
    return {
        "knownCost": str(total),
        "acceptedOperations": accepted,
        "unknownCostRecords": len(facts) - len(known),
        "costPerAcceptedOperation": value,
        "currency": currency,
    }


def build_report(facts: Sequence[ProposalFact], method_date: str, minimum: int = 20) -> dict:
    report = {
        "schemaVersion": 1,
        "methodDate": method_date,
        "sampleSize": len(facts),
        "metrics": [
            asdict(unchanged_acceptance(facts, minimum)),
            asdict(labelled_rate(facts, "target_correct", "target-accuracy", minimum)),
            asdict(labelled_rate(facts, "permission_correct", "permission-accuracy", minimum)),
            asdict(labelled_rate(facts, "false_action", "false-action-adjudication", minimum)),
            asdict(labelled_rate(facts, "correct_no_action", "correct-no-action", minimum)),
        ],
        "cost": cost_per_accepted_operation(facts),
    }
    return report


def main() -> int:
    import argparse
    parser = argparse.ArgumentParser(description="Build content-free Context Fabric metrics")
    parser.add_argument("facts")
    parser.add_argument("--method-date", required=True)
    parser.add_argument("--minimum", type=int, default=20)
    parser.add_argument("--output")
    args = parser.parse_args()

    raw = json.loads(open(args.facts, encoding="utf-8").read())
    facts = [
        ProposalFact(
            proposal_id=item["proposalId"],
            reviewed=bool(item["reviewed"]),
            approved=bool(item["approved"]),
            approved_unchanged=bool(item["approvedUnchanged"]),
            rejected=bool(item["rejected"]),
            target_correct=item.get("targetCorrect"),
            permission_correct=item.get("permissionCorrect"),
            false_action=item.get("falseAction"),
            correct_no_action=item.get("correctNoAction"),
            accepted_operations=int(item.get("acceptedOperations", 0)),
            attributable_cost=None if item.get("attributableCost") is None else Decimal(str(item["attributableCost"])),
            currency=item.get("currency"),
        )
        for item in raw
    ]
    result = build_report(facts, args.method_date, args.minimum)
    rendered = json.dumps(result, indent=2, sort_keys=True)
    if args.output:
        open(args.output, "w", encoding="utf-8").write(rendered + "\n")
    else:
        print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
