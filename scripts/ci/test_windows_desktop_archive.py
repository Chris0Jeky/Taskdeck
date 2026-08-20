from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

import windows_desktop_archive as harness


class WindowsDesktopArchiveTests(unittest.TestCase):
    def test_verify_checksum_requires_exact_archive_name_and_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            archive = root / "taskdeck-v0.1.1-win-x64.zip"
            archive.write_bytes(b"package")
            digest = hashlib.sha256(b"package").hexdigest()
            checksum = root / f"{archive.name}.sha256"
            checksum.write_text(f"{digest}  {archive.name}\n", encoding="utf-8")

            self.assertEqual(digest, harness.verify_checksum(archive, checksum))

            checksum.write_text(f"{digest}  other.zip\n", encoding="utf-8")
            with self.assertRaises(harness.AcceptanceFailure):
                harness.verify_checksum(archive, checksum)

    def test_verify_checksum_rejects_modified_archive(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            archive = root / "package.zip"
            archive.write_bytes(b"before")
            checksum = root / "package.zip.sha256"
            checksum.write_text(
                f"{hashlib.sha256(b'before').hexdigest()}  package.zip\n",
                encoding="utf-8",
            )
            archive.write_bytes(b"after")

            with self.assertRaises(harness.AcceptanceFailure):
                harness.verify_checksum(archive, checksum)

    def test_safe_extract_rejects_path_traversal(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            archive = root / "unsafe.zip"
            with zipfile.ZipFile(archive, "w") as package:
                package.writestr("../escape.txt", "bad")

            with self.assertRaises(harness.AcceptanceFailure):
                harness.safe_extract_archive(archive, root / "extract")
            self.assertFalse((root / "escape.txt").exists())

    def test_app_environment_preserves_ci_but_removes_false_proof_overrides(self) -> None:
        local_app_data = Path(tempfile.gettempdir()).resolve() / "taskdeck-unit-localappdata"
        source = {
            "CI": "true",
            "GITHUB_ACTIONS": "true",
            "ASPNETCORE_URLS": "http://127.0.0.1:5099",
            "ConnectionStrings__DefaultConnection": "Data Source=unsafe.db",
            "Jwt__SecretKey": "unsafe",
            "Connectors__EncryptionKey": "unsafe",
            "FirstRun__AutoOpenBrowser": "false",
            "TASKDECK_HEADLESS": "1",
            "OPENAI_API_KEY": "ambient",
        }

        environment = harness.build_app_environment(source, local_app_data, "operator-value")

        self.assertEqual("true", environment["CI"])
        self.assertEqual("true", environment["GITHUB_ACTIONS"])
        self.assertEqual(str(local_app_data), environment["LOCALAPPDATA"])
        self.assertNotIn("ASPNETCORE_URLS", environment)
        self.assertNotIn("ConnectionStrings__DefaultConnection", environment)
        self.assertNotIn("Jwt__SecretKey", environment)
        self.assertNotIn("Connectors__EncryptionKey", environment)
        self.assertNotIn("FirstRun__AutoOpenBrowser", environment)
        self.assertNotIn("TASKDECK_HEADLESS", environment)
        self.assertNotIn("OPENAI_API_KEY", environment)
        self.assertEqual("operator-value", environment["Llm__OpenAi__ApiKey"])

    def test_app_environment_canonicalizes_ci_and_keeps_operator_key_out_of_playwright(self) -> None:
        local_app_data = Path(tempfile.gettempdir()).resolve() / "taskdeck-unit-localappdata"
        for label, source_ci in (("absent", {}), ("mixed-case false", {"cI": "false"})):
            with self.subTest(label=label):
                source = {
                    **source_ci,
                    "TASKDECK_RELEASE_OPENAI_API_KEY": "operator-value",
                }
                operator_key = harness.resolve_operator_key(source)
                app_environment = harness.build_app_environment(
                    source,
                    local_app_data,
                    operator_key,
                )
                playwright_environment = harness.build_playwright_environment(
                    source,
                    base_url="http://127.0.0.1:54321",
                    evidence_path=Path(tempfile.gettempdir()) / "evidence.json",
                    journey_id="release-123456-789",
                    phase="create",
                    live_openai=True,
                    live_skip_reason="none",
                )

                self.assertEqual(
                    [("CI", "true")],
                    [(key, value) for key, value in app_environment.items() if key.upper() == "CI"],
                )
                self.assertEqual("operator-value", app_environment["Llm__OpenAi__ApiKey"])
                self.assertNotIn("TASKDECK_RELEASE_OPENAI_API_KEY", app_environment)
                self.assertNotIn("TASKDECK_HEADLESS", app_environment)
                self.assertNotIn("operator-value", playwright_environment.values())

    def test_playwright_environment_never_receives_operator_key(self) -> None:
        source = {
            "CI": "true",
            "GITHUB_ACTIONS": "true",
            "TASKDECK_RELEASE_OPENAI_API_KEY": "operator-value",
            "OPENAI_API_KEY": "ambient-value",
            "Llm__OpenAi__ApiKey": "mapped-value",
        }
        environment = harness.build_playwright_environment(
            source,
            base_url="http://127.0.0.1:54321",
            evidence_path=Path(tempfile.gettempdir()) / "evidence.json",
            journey_id="release-123456-789",
            phase="create",
            live_openai=True,
            live_skip_reason="none",
        )

        self.assertEqual("true", environment["CI"])
        self.assertEqual("true", environment["GITHUB_ACTIONS"])
        self.assertNotIn("operator-value", environment.values())
        self.assertNotIn("ambient-value", environment.values())
        self.assertNotIn("mapped-value", environment.values())

    def test_standard_taskdeck_key_enables_live_child_without_reaching_playwright(self) -> None:
        source = {"llm__openai__apikey": "  synthetic-operator-value  "}

        operator_key = harness.resolve_operator_key(source)
        app_environment = harness.build_app_environment(
            source,
            Path(tempfile.gettempdir()).resolve() / "taskdeck-unit-localappdata",
            operator_key,
        )
        playwright_environment = harness.build_playwright_environment(
            source,
            base_url="http://127.0.0.1:54321",
            evidence_path=Path(tempfile.gettempdir()) / "evidence.json",
            journey_id="release-123456-789",
            phase="create",
            live_openai=True,
            live_skip_reason="none",
        )

        self.assertEqual("synthetic-operator-value", operator_key)
        self.assertEqual("synthetic-operator-value", app_environment["Llm__OpenAi__ApiKey"])
        self.assertNotIn("synthetic-operator-value", playwright_environment.values())

    def test_ready_marker_accepts_only_resolved_ipv4_loopback(self) -> None:
        self.assertIsNotNone(harness.READY_PATTERN.fullmatch(
            "TASKDECK_DESKTOP_READY url=http://127.0.0.1:54321"
        ))
        self.assertIsNone(harness.READY_PATTERN.fullmatch(
            "TASKDECK_DESKTOP_READY url=http://0.0.0.0:54321"
        ))
        self.assertIsNone(harness.READY_PATTERN.fullmatch(
            "TASKDECK_DESKTOP_READY url=http://127.0.0.1:0"
        ))

    def test_remove_temp_root_refuses_unowned_path(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            parent = Path(raw).resolve()
            unrelated = parent / "unrelated"
            unrelated.mkdir()
            with self.assertRaises(harness.AcceptanceFailure):
                harness.remove_temp_root(unrelated, parent)
            self.assertTrue(unrelated.exists())

    def test_phase_evidence_rejects_forbidden_fields(self) -> None:
        evidence = {
            "schemaVersion": 2,
            "phase": "create",
            "journeyId": "release-123456-789",
            "board": {"id": "synthetic-id", "title": "synthetic"},
            "persistence": {"registered": True, "boardCreated": True},
            "http": [{"method": "POST", "path": "/api/auth/register", "status": 200}],
            "liveOpenAi": {"outcome": "skipped", "reason": "not_requested"},
        }
        self.assertEqual(
            evidence,
            harness.validate_phase_evidence(evidence, "create", "release-123456-789"),
        )

        forbidden = json.loads(json.dumps(evidence))
        forbidden["liveOpenAi"]["providerError"] = "must not persist"
        with self.assertRaises(harness.AcceptanceFailure):
            harness.validate_phase_evidence(forbidden, "create", "release-123456-789")

    def test_phase_evidence_accepts_bounded_integer_probe_latency(self) -> None:
        evidence = self._live_create_evidence(123)

        self.assertEqual(
            evidence,
            harness.validate_phase_evidence(evidence, "create", "release-123456-789"),
        )

    def test_phase_evidence_rejects_missing_probe_latency(self) -> None:
        evidence = self._live_create_evidence(123)
        del evidence["liveOpenAi"]["probeLatencyMs"]

        with self.assertRaises(harness.AcceptanceFailure):
            harness.validate_phase_evidence(evidence, "create", "release-123456-789")

    def test_phase_evidence_rejects_invalid_probe_latency_types_and_bounds(self) -> None:
        invalid_values = (True, "123", 1.5, None, 0, -1, 300_001)
        for value in invalid_values:
            with self.subTest(value=value):
                evidence = self._live_create_evidence(value)
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.validate_phase_evidence(evidence, "create", "release-123456-789")

    def test_snapshot_detects_extraction_mutation(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            file_path = root / "Taskdeck.Api.exe"
            file_path.write_bytes(b"original")
            before = harness.snapshot_tree(root)
            harness.assert_tree_unchanged(before, root, "archive")
            file_path.write_bytes(b"changed")
            with self.assertRaises(harness.AcceptanceFailure):
                harness.assert_tree_unchanged(before, root, "archive")

    @staticmethod
    def _live_create_evidence(probe_latency_ms: object) -> dict[str, object]:
        return {
            "schemaVersion": 2,
            "phase": "create",
            "journeyId": "release-123456-789",
            "board": {"id": "synthetic-id", "title": "synthetic"},
            "persistence": {"registered": True, "boardCreated": True},
            "http": [{"method": "POST", "path": "/api/auth/register", "status": 200}],
            "liveOpenAi": {
                "outcome": "passed",
                "provider": "OpenAI",
                "model": "gpt-5.6-luna",
                "isMock": False,
                "isProbed": True,
                "verificationStatus": "verified",
                "probeLatencyMs": probe_latency_ms,
                "cardTitle": "Synthetic card",
                "proposal": {
                    "id": "synthetic-proposal",
                    "statusBeforeApproval": "Pending",
                    "statusAfterApproval": "Approved",
                    "statusAfterApply": "Applied",
                    "operationCount": 1,
                },
                "cardCounts": {"beforeProposal": 0, "afterApproval": 0, "afterApply": 1},
            },
        }


if __name__ == "__main__":
    unittest.main()
