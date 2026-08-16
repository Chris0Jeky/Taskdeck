from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from summarize_trx_timing import summarize_trx


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
        self.assertEqual(
            [row["fullyQualifiedName"] for row in summary["results"]],
            [
                "Zeta.Tests.SlowTests.TakesTime",
                "Alpha.Tests.FastTests.RunsQuickly",
                None,
                "Alpha.Tests.FastTests.NoTiming",
            ],
        )
        self.assertEqual(summary["results"][2]["identityStatus"], "missing-definition")
        self.assertEqual(summary["classes"][0]["className"], "Zeta.Tests.SlowTests")
        self.assertEqual(summary["classes"][1]["resultCount"], 2)
        serialized = json.dumps(summary)
        self.assertNotIn("secret output", serialized)
        self.assertNotIn("private error", serialized)
        self.assertNotIn("theory-secret", serialized)

    def test_missing_and_invalid_duration_are_nullable_and_not_summed(self) -> None:
        path = self.write_trx(TRX.replace('duration=""', 'duration="not-a-duration"'))
        summary = summarize_trx(path)
        self.assertIsNone(summary["results"][-1]["durationSeconds"])
        self.assertEqual(summary["missingDurationCount"], 1)
        self.assertEqual(summary["summedTestDurationSeconds"], 3.75)

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
