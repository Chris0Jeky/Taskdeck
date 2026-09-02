#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json
from pathlib import Path

def f1(expected: set[str], actual: set[str]) -> float:
    if not expected and not actual: return 1.0
    if not expected or not actual: return 0.0
    tp=len(expected & actual); precision=tp/len(actual); recall=tp/len(expected)
    return 0.0 if precision+recall==0 else 2*precision*recall/(precision+recall)

def score_fixture(fixture: dict, result: dict) -> dict:
    expected=set(fixture.get('expectedKeys', [])); actual=set(result.get('candidateKeys', []))
    return {'fixtureId':fixture['id'],'precisionRecallF1':f1(expected,actual),'expected':len(expected),'actual':len(actual)}

def main() -> int:
    ap=argparse.ArgumentParser(); ap.add_argument('fixtures'); ap.add_argument('results'); ap.add_argument('--out',required=True); ns=ap.parse_args()
    fixtures={x['id']:x for x in json.loads(Path(ns.fixtures).read_text())}; results={x['fixtureId']:x for x in json.loads(Path(ns.results).read_text())}
    scores=[score_fixture(f,results.get(fid,{'candidateKeys':[]})) for fid,f in sorted(fixtures.items())]
    report={'scores':scores,'meanF1':sum(x['precisionRecallF1'] for x in scores)/len(scores) if scores else None}
    Path(ns.out).write_text(json.dumps(report,indent=2)+'\n'); return 0
if __name__=='__main__': raise SystemExit(main())
