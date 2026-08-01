from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).parent))
from assert_container_integration_results import assert_negative, assert_positive, read_results


class ContainerIntegrationResultTests(unittest.TestCase):
    def test_positive_contract_accepts_passing_postgres_results_without_skips(self) -> None:
        assert_positive(
            [
                {
                    "name": "Taskdeck.Integration.Tests.BoardCrudIntegrationTests.CreateBoard",
                    "outcome": "Passed",
                    "message": "",
                }
                for _ in range(28)
            ],
            minimum_postgres_results=28,
        )

    def test_positive_contract_rejects_skips(self) -> None:
        with self.assertRaisesRegex(ValueError, "zero skipped"):
            assert_positive(
                [
                    {
                        "name": "Taskdeck.Integration.Tests.BoardCrudIntegrationTests.CreateBoard",
                        "outcome": "NotExecuted",
                        "message": "",
                    }
                ],
                minimum_postgres_results=1,
            )

    def test_positive_contract_does_not_count_host_native_sqlite_results(self) -> None:
        results = [
            {
                "name": "Taskdeck.Integration.Tests.BoardCrudIntegrationTests.CreateBoard",
                "outcome": "Passed",
                "message": "",
            }
            for _ in range(27)
        ]
        results.append(
            {
                "name": "Taskdeck.Integration.Tests.SQLiteNativeVersionTests.MeetsSecurityFloor",
                "outcome": "Passed",
                "message": "",
            }
        )

        with self.assertRaisesRegex(ValueError, "found 27"):
            assert_positive(results, minimum_postgres_results=28)

    def test_negative_contract_requires_the_docker_required_failure(self) -> None:
        assert_negative(
            [
                {
                    "name": "Taskdeck.Integration.Tests.BoardCrudIntegrationTests.CreateBoard",
                    "outcome": "Failed",
                    "message": "Docker is required for this test run but is unavailable.",
                }
            ],
            "Docker is required for this test run but is unavailable.",
        )

    def test_trx_reader_accepts_namespaced_results(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.trx"
            path.write_text(
                """<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">\n"
                "<Results><UnitTestResult testName=\"Example\" outcome=\"Passed\" />"
                "</Results></TestRun>""",
                encoding="utf-8",
            )

            self.assertEqual(read_results(path), [{"name": "Example", "outcome": "Passed", "message": ""}])


if __name__ == "__main__":
    unittest.main()
