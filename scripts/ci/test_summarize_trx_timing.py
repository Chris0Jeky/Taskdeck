from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from summarize_trx_timing import TOP_N, summarize_trx


TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testId="slow" outcome="Passed" duration="00:00:02.2500000">
      <Output><StdOut>secret output must never appear</StdOut><ErrorInfo><Message>private error</Message></ErrorInfo></Output>
    </UnitTestResult>
    <UnitTestResult testId="fast" outcome="Failed" duration="00:00:01.0000000" />
    <UnitTestResult testId="untimed" outcome="Skipped" duration="" />
    <UnitTestResult testId="missing" outcome="NotExecuted" duration="00:00:00.5000000" />
  </Results>
  <Definitions>
    <UnitTest id="slow"><TestMethod className="Zeta.Tests.SlowTests" name="TakesTime" /></UnitTest>
    <UnitTest id="fast"><TestMethod className="Alpha.Tests.FastTests" name="RunsQuickly(theory-secret)" /></UnitTest>
    <UnitTest id="untimed"><TestMethod className="Alpha.Tests.FastTests" name="NoTiming" /></UnitTest>
  </Definitions>
</TestRun>
"""


class SummarizeTrxTimingTests(unittest.TestCase):
    def write_trx(self, contents: str = TRX) -> Path:
        directory = self.enterContext(tempfile.TemporaryDirectory())
        path = Path(directory) / "synthetic.trx"
        path.write_text(contents, encoding="utf-8")
        return path

    def test_summary_is_namespace_safe_sorted_and_content_free(self) -> None:
        summary = summarize_trx(self.write_trx())

        self.assertEqual(summary["resultCount"], 4)
        self.assertEqual(summary["timedResultCount"], 3)
        self.assertEqual(summary["missingDurationCount"], 1)
        self.assertEqual(summary["summedTestDurationSeconds"], 3.75)
        self.assertIsNone(summary["workflowWallTimeSeconds"])
        self.assertEqual(summary["schemaVersion"], 2)
        self.assertEqual(
            summary["durationStatistics"],
            {
                "p50Seconds": 1.0,
                "p90Seconds": 2.25,
                "p95Seconds": 2.25,
                "p99Seconds": 2.25,
                "maxSeconds": 2.25,
            },
        )
        self.assertEqual(
            [row["fullyQualifiedName"] for row in summary["topResults"]],
            [
                "Zeta.Tests.SlowTests.TakesTime",
                "Alpha.Tests.FastTests.RunsQuickly",
                None,
            ],
        )
        self.assertEqual(summary["topResults"][2]["identityStatus"], "missing-definition")
        self.assertEqual(summary["topClasses"][0]["className"], "Zeta.Tests.SlowTests")
        self.assertEqual(summary["topClasses"][1]["resultCount"], 2)
        serialized = json.dumps(summary)
        self.assertNotIn("secret output", serialized)
        self.assertNotIn("private error", serialized)
        self.assertNotIn("theory-secret", serialized)

    def test_percentiles_ties_top_n_and_missing_durations_are_deterministic(self) -> None:
        rows = []
        definitions = []
        for index in range(TOP_N + 2):
            duration = "00:00:03.0000000" if index < 2 else f"00:00:{index:02d}.0000000"
            rows.append(
                f'<UnitTestResult testId="id-{index}" outcome="Passed" duration="{duration}" />'
            )
            definitions.append(
                f'<UnitTest id="id-{index}"><TestMethod className="Tests.Tie{index:02d}" name="Run" /></UnitTest>'
            )
        rows.append('<UnitTestResult testId="untimed" outcome="Skipped" duration="" />')
        definitions.append('<UnitTest id="untimed"><TestMethod className="Tests.Untimed" name="Run" /></UnitTest>')
        trx = "<TestRun><Results>" + "".join(rows) + "</Results><Definitions>" + "".join(definitions) + "</Definitions></TestRun>"

        summary = summarize_trx(self.write_trx(trx))

        self.assertEqual(summary["timedResultCount"], TOP_N + 2)
        self.assertEqual(summary["missingDurationCount"], 1)
        self.assertEqual(len(summary["topResults"]), TOP_N)
        self.assertEqual(
            [row["fullyQualifiedName"] for row in summary["topResults"][-2:]],
            ["Tests.Tie00.Run", "Tests.Tie01.Run"],
        )
        self.assertEqual(summary["durationStatistics"]["p50Seconds"], 5.0)
        self.assertEqual(summary["durationStatistics"]["p90Seconds"], 10.0)
        self.assertNotIn("Tests.Untimed.Run", json.dumps(summary["topResults"]))

    def test_missing_and_invalid_duration_are_nullable_and_not_summed(self) -> None:
        path = self.write_trx(TRX.replace('duration=""', 'duration="not-a-duration"'))
        summary = summarize_trx(path)
        self.assertEqual(summary["missingDurationCount"], 1)
        self.assertEqual(summary["summedTestDurationSeconds"], 3.75)
        self.assertFalse(any(row["durationSeconds"] is None for row in summary["topResults"]))

    def test_malformed_xml_and_empty_results_fail_explicitly(self) -> None:
        with self.assertRaisesRegex(ValueError, "no UnitTestResult"):
            summarize_trx(self.write_trx("<TestRun><Results /></TestRun>"))
        with self.assertRaisesRegex(Exception, "mismatched|unclosed|no element"):
            summarize_trx(self.write_trx("<TestRun>"))

    def test_result_and_identity_bounds_fail_closed(self) -> None:
        with self.assertRaisesRegex(ValueError, "2-result limit"):
            summarize_trx(self.write_trx(), max_results=2)
        oversized = TRX.replace('className="Zeta.Tests.SlowTests"', 'className="' + ('x' * 20) + '"')
        with self.assertRaisesRegex(ValueError, "identity"):
            summarize_trx(self.write_trx(oversized), identity_limit=10)

    def test_cli_writes_json_to_a_temporary_directory(self) -> None:
        trx_path = self.write_trx()
        output_path = trx_path.parent / "timing.json"
        completed = subprocess.run(
            [
                sys.executable,
                str(Path(__file__).with_name("summarize_trx_timing.py")),
                "--trx",
                str(trx_path),
                "--output",
                str(output_path),
            ],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        self.assertEqual(json.loads(output_path.read_text(encoding="utf-8"))["resultCount"], 4)


if __name__ == "__main__":
    unittest.main()
