from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import collect_runner_context as context


class RunnerContextTests(unittest.TestCase):
    def test_begin_and_finalize_emit_only_the_allowed_schema(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            state = root / "state.json"
            output = root / "output.json"
            snapshots = [
                {"logicalCpuCount": 4, "totalPhysicalMemoryBytes": 100, "workspaceVolumeFreeBytes": 50},
                {"logicalCpuCount": 4, "availablePhysicalMemoryBytes": 25, "workspaceVolumeFreeBytes": 45},
            ]
            with (
                patch.object(context, "collect_snapshot", side_effect=snapshots),
                patch.object(context, "_dotnet_sdk_version", return_value="8.0.412"),
                patch.object(context.time, "monotonic", side_effect=[100.0, 104.25]),
            ):
                context.begin_context(state_path=state, matrix_os="windows-latest", workspace=root)
                context.finalize_context(state_path=state, output_path=output, workspace=root)

            payload = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(set(payload), context._CONTEXT_FIELDS)
            self.assertEqual(payload["matrixOs"], "windows-latest")
            self.assertEqual(payload["dotnetSdkVersion"], "8.0.412")
            self.assertEqual(payload["testPhaseWallSeconds"], 4.25)
            self.assertEqual(payload["before"], snapshots[0])
            self.assertEqual(payload["after"], snapshots[1])
            serialized = json.dumps(payload)
            self.assertNotIn(str(root), serialized)
            self.assertNotIn("PATH", serialized)

    def test_unavailable_metrics_are_omitted(self) -> None:
        with (
            patch.object(context.os, "cpu_count", return_value=None),
            patch.object(context, "_memory", return_value=(None, None)),
            patch.object(context.shutil, "disk_usage", side_effect=OSError()),
        ):
            self.assertEqual(context.collect_snapshot(Path("unavailable-workspace")), {})

    def test_dotnet_version_parser_is_bounded_and_does_not_relay_output(self) -> None:
        with patch.object(
            context.subprocess,
            "run",
            return_value=SimpleNamespace(returncode=0, stdout="8.0.412\n"),
        ) as run:
            self.assertEqual(context._dotnet_sdk_version(), "8.0.412")
        self.assertEqual(run.call_args.args[0], ["dotnet", "--version"])
        self.assertIs(run.call_args.kwargs["stderr"], subprocess.DEVNULL)
        for output in ("secret output\n8.0.412\n", "8.0.412" + "x" * 80, "not-a-version\n"):
            with patch.object(
                context.subprocess,
                "run",
                return_value=SimpleNamespace(returncode=0, stdout=output),
            ):
                self.assertIsNone(context._dotnet_sdk_version())

    def test_schema_validation_rejects_extra_and_out_of_range_values(self) -> None:
        valid = {
            "schemaVersion": 1,
            "matrixOs": "windows-latest",
            "dotnetSdkVersion": None,
            "before": {},
            "after": {},
            "testPhaseWallSeconds": 0.0,
        }
        context.validate_context(valid)
        with self.assertRaisesRegex(ValueError, "schema"):
            context.validate_context(valid | {"workspace": "must-not-emit"})
        invalid = valid | {"after": {"workspaceVolumeFreeBytes": -1}}
        with self.assertRaisesRegex(ValueError, "snapshot"):
            context.validate_context(invalid)

    def test_workflow_collects_before_and_after_without_test_behavior_changes(self) -> None:
        workflow = Path(__file__).parents[2] / ".github" / "workflows" / "reusable-api-integration.yml"
        text = workflow.read_text(encoding="utf-8")
        command = 'dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj --configuration Release --no-restore --logger "trx;LogFileName=api-integration.trx" --results-directory "backend/TestResults/api-integration/${{ matrix.os }}"'
        self.assertIn(command, text)
        self.assertLess(text.index("Capture API integration runner context"), text.index(command))
        self.assertGreater(text.index("Finalize API integration runner context"), text.index(command))
        self.assertIn("if: always()", text[text.index("Finalize API integration runner context") :])
        self.assertIn("api-integration-runner-context-${{ matrix.os }}", text)
        self.assertIn("retention-days: 14", text)
        lowered = text.lower()
        for forbidden in ("timeout-minutes", "retry", "quarantine", "parallel", "coverage"):
            self.assertNotIn(forbidden, lowered)


if __name__ == "__main__":
    unittest.main()
