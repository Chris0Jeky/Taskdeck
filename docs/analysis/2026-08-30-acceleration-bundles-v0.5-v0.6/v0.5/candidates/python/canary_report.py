#!/usr/bin/env python3
from __future__ import annotations
import argparse, json
from collections import Counter
from pathlib import Path

def build(events: list[dict]) -> dict:
    outcomes=Counter(e.get('outcome','unknown') for e in events)
    total=len(events); reversals=outcomes['reversed']; edits=outcomes['human-edited']
    return {'total':total,'outcomes':dict(outcomes),'reversalRate':reversals/total if total else None,'humanEditRate':edits/total if total else None,'safeToExpand':total>=50 and reversals==0 and edits/total<=0.1 if total else False}

def main()->int:
    ap=argparse.ArgumentParser(); ap.add_argument('events'); ap.add_argument('--out',required=True); ns=ap.parse_args()
    report=build(json.loads(Path(ns.events).read_text())); Path(ns.out).write_text(json.dumps(report,indent=2)+'\n'); return 0
if __name__=='__main__': raise SystemExit(main())
