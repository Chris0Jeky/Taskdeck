# Context Fabric v0.5 benchmark corpus

This is the first useful CF-24A (#2319) corpus slice. It is deliberately small and synthetic:
Nine text/transcript examples cover `Action`, `Decision`, `Question`, `Risk`, `Fact`, and
`Reference`, plus a no-candidate control and a hostile-injection transcript. The source files are
licensed as `synthetic`; no external, private, audio, image, or PDF material is checked in.

Each record in `fixtures.json` is validated against
[`evaluation_fixture.schema.json`](../../../../scripts/context_fabric/evaluation_fixture.schema.json).
`sourceHash` is the lowercase SHA-256 of the exact source bytes. `referenceOutputPath` points to
the adjudicated candidate set for that source. `measurementMethod` and `measurementDate` travel
with every record, and `hostileInjection` is mandatory even when false.

Run the contract check and deterministic scorer from the repository root:

```powershell
py -3 -B scripts/context_fabric/benchmark_fixtures.py `
  tests/fixtures/context_fabric/benchmark/fixtures.json `
  --predictions tests/fixtures/context_fabric/benchmark/predictions.example.json `
  --out benchmark-report.json
```

The command reads only the paths under this fixture directory, checks every source hash and
reference output, and enforces a 16 KiB source-byte budget. The checked-in corpus currently uses
934 bytes. Omit `--predictions` to validate the corpus while emitting `metricsStatus: unavailable`;
the command never executes a processor and never treats references compared with themselves as
processor accuracy. Prediction rows contain `{kind, key}` candidates and the report gives
precision, recall, and F1 per candidate kind.

The example predictions are intentionally imperfect so false positives, missed candidates, and a
hostile instruction-like string remain visible in the report. They are test data, not a processor
claim or a quality baseline.

This slice leaves the follow-on work explicit: add separately licensed audio fixtures and measured
transcript/WER processing; add image fixtures and OCR/layout measurements; add PDF fixtures and
text extraction measurements; and add runtime/provider timing, memory, cost, and outcome capture
once the Context Fabric processors and run records exist. Those measurements belong to later
processor/runtime benchmark work and cannot be inferred from this static text scorer.
