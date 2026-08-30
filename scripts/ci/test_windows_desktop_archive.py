from __future__ import annotations

import hashlib
import io
import json
import os
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

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

    def test_mcp_initialize_request_pins_the_release_protocol(self) -> None:
        encoded = harness.build_mcp_initialize_request()
        self.assertTrue(encoded.endswith("\n"))
        self.assertEqual(
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2025-11-25",
                    "capabilities": {},
                    "clientInfo": {
                        "name": "taskdeck-desktop-archive-smoke",
                        "version": "1.0",
                    },
                },
            },
            json.loads(encoded),
        )

    def test_mcp_initialize_stdout_requires_one_valid_server_info_response(self) -> None:
        response = self._mcp_initialize_response()
        self.assertEqual(
            {
                "initialized": True,
                "serverInfoValid": True,
                "stdoutClean": True,
            },
            harness.validate_mcp_initialize_stdout(response),
        )

        invalid_outputs = (
            f"startup log\n{response}",
            f"\n{response}",
            f"{response}{response}",
            '{"jsonrpc":"2.0","id":1,"error":{"code":-1}}\n',
            '{"jsonrpc":"2.0","id":1,"result":{"serverInfo":{"name":"Taskdeck"}}}\n',
            '{"jsonrpc":"2.0","id":2,"result":{"serverInfo":{"name":"Taskdeck","version":"1"}}}\n',
        )
        for invalid in invalid_outputs:
            with self.subTest(invalid=invalid[:40]):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.validate_mcp_initialize_stdout(invalid)

    def test_packaged_mcp_probe_waits_for_response_then_closes_stdin(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw).resolve()
            executable = root / "Taskdeck.Api.exe"
            executable.write_bytes(b"synthetic executable")
            cwd = root / "cwd"
            cwd.mkdir()
            process = mock.Mock()
            process.stdin = mock.Mock()
            process.stdout = io.StringIO(self._mcp_initialize_response())
            process.wait.return_value = 0
            process.returncode = 0

            with mock.patch.object(harness.subprocess, "Popen", return_value=process) as popen:
                evidence = harness.verify_packaged_mcp_stdio(
                    executable,
                    cwd,
                    {
                        "CI": "true",
                        "GITHUB_ACTIONS": "true",
                        "TF_BUILD": "true",
                        "LOCALAPPDATA": str(root / "local-app-data"),
                    },
                )

            self.assertTrue(evidence["initialized"])
            self.assertEqual([str(executable), "--mcp"], popen.call_args.args[0])
            self.assertEqual(str(cwd), popen.call_args.kwargs["cwd"])
            for runner_flag in ("CI", "GITHUB_ACTIONS", "TF_BUILD"):
                self.assertNotIn(runner_flag, popen.call_args.kwargs["env"])
            self.assertEqual(
                str(root / "local-app-data"),
                popen.call_args.kwargs["env"]["LOCALAPPDATA"],
            )
            self.assertEqual(harness.subprocess.PIPE, popen.call_args.kwargs["stdin"])
            self.assertEqual(harness.subprocess.PIPE, popen.call_args.kwargs["stdout"])
            self.assertEqual(harness.subprocess.DEVNULL, popen.call_args.kwargs["stderr"])
            process.stdin.write.assert_called_once_with(harness.build_mcp_initialize_request())
            process.stdin.flush.assert_called_once_with()
            process.stdin.close.assert_called_once_with()
            process.wait.assert_called_once_with(
                timeout=harness.MCP_STDIO_TIMEOUT_SECONDS
            )

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
        legacy_aliases = {
            "tAsKdEcK_lLm_OpEnAi_ApI_kEy": "synthetic-legacy-openai-value",
            "TaSkDeCk_LlM_oPeNaI_cOmPaTiBlE_aPi_KeY": "synthetic-compatible-value",
            "gEmInI_aPi_KeY": "synthetic-gemini-value",
            "tAsKdEcK_dEmO_gEmInI_aPi_KeY": "synthetic-demo-gemini-value",
        }
        source = {
            "CI": "true",
            "GITHUB_ACTIONS": "true",
            "TASKDECK_RELEASE_OPENAI_API_KEY": "operator-value",
            "OPENAI_API_KEY": "ambient-value",
            "Llm__OpenAi__ApiKey": "mapped-value",
            "tAsKdEcK_lLm_GeMiNi_ApI_kEy": "retired-value",
            **legacy_aliases,
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
        self.assertNotIn("retired-value", environment.values())
        self.assertNotIn("TASKDECK_LLM_GEMINI_API_KEY", {key.upper() for key in environment})
        environment_names = {key.upper() for key in environment}
        for name, value in legacy_aliases.items():
            with self.subTest(name=name):
                self.assertNotIn(name.upper(), environment_names)
                self.assertNotIn(value, environment.values())

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

    def test_live_openai_mode_matrix(self) -> None:
        cases = (
            (
                "off",
                False,
                False,
                {"LlM__oPeNaI__aPiKeY": "must-be-ignored"},
                False,
                "not_requested",
                None,
            ),
            ("optional-missing", False, True, {}, False, "credential_unavailable", None),
            (
                "optional-configured",
                False,
                True,
                {"tAsKdEcK_rElEaSe_OpEnAi_ApI_kEy": "  optional-value  "},
                True,
                "none",
                "optional-value",
            ),
            (
                "required-configured",
                True,
                False,
                {"LlM__oPeNaI__aPiKeY": "  required-value  "},
                True,
                "none",
                "required-value",
            ),
        )

        for mode, required, optional, environment, enabled, skip_reason, operator_key in cases:
            with self.subTest(mode=mode):
                resolution = harness.resolve_live_openai_mode(
                    required=required,
                    optional=optional,
                    environment=environment,
                )
                self.assertEqual(mode.split("-", maxsplit=1)[0], resolution.mode)
                self.assertEqual(enabled, resolution.enabled)
                self.assertEqual(skip_reason, resolution.skip_reason)
                self.assertEqual(operator_key, resolution.operator_key)

    def test_required_live_openai_without_credential_fails_with_generic_text(self) -> None:
        with self.assertRaises(harness.AcceptanceFailure) as raised:
            harness.resolve_live_openai_mode(required=True, optional=False, environment={})

        message = str(raised.exception)
        self.assertEqual("Required hosted acceptance is unavailable.", message)
        for forbidden in (
            "openai",
            "api",
            "key",
            "credential",
            "environment",
            "path",
            "prompt",
            "response",
        ):
            self.assertNotIn(forbidden, message.lower())

    def test_live_openai_modes_are_mutually_exclusive(self) -> None:
        with self.assertRaises(harness.AcceptanceFailure):
            harness.resolve_live_openai_mode(required=True, optional=True, environment={})

    @unittest.skipUnless(os.name == "nt", "PowerShell wrapper regression requires Windows")
    def test_required_missing_wrapper_output_is_nonzero_generic_and_path_free(self) -> None:
        system_root = Path(os.environ["SystemRoot"])
        powershell = system_root / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
        wrapper = Path(__file__).with_name("Test-WindowsDesktopArchive.ps1").resolve()
        repo_root = wrapper.parents[2]
        child_environment = {
            name: os.environ[name]
            for name in ("SystemRoot", "TEMP", "TMP")
            if name in os.environ
        }
        child_environment["PATH"] = os.pathsep.join((str(system_root), str(system_root / "System32")))
        child_environment["PATHEXT"] = ".COM;.EXE;.BAT;.CMD"

        result = subprocess.run(
            [
                str(powershell),
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(wrapper),
                "-ArchivePath",
                "missing.zip",
                "-ChecksumPath",
                "missing.sha256",
                "-EvidencePath",
                "missing.json",
                "-FrontendDirectory",
                "missing-frontend",
                "-LiveOpenAI",
            ],
            cwd=repo_root,
            env=child_environment,
            capture_output=True,
            text=True,
            check=False,
        )

        output = "\n".join(part.strip() for part in (result.stdout, result.stderr) if part.strip())
        self.assertEqual(1, result.returncode)
        self.assertEqual("ERROR: Required hosted acceptance is unavailable.", output)
        self.assertNotIn("NativeCommandError", output)
        self.assertNotIn(str(wrapper), output)

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

    def test_bootstrap_identity_marker_accepts_exact_bounded_booleans(self) -> None:
        self.assertEqual(
            {"jwtCreated": True, "connectorCreated": False},
            harness.validate_bootstrap_identity_markers([
                "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=false"
            ]),
        )

    def test_bootstrap_identity_marker_rejects_missing_and_duplicate_records(self) -> None:
        valid = "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=true"

        with self.assertRaises(harness.AcceptanceFailure):
            harness.validate_bootstrap_identity_markers([])
        with self.assertRaises(harness.AcceptanceFailure):
            harness.validate_bootstrap_identity_markers([valid, valid])

    def test_bootstrap_identity_marker_rejects_malformed_and_unknown_records(self) -> None:
        invalid_markers = (
            "TASKDECK_DESKTOP_BOOTSTRAP connector_created=true jwt_created=true",
            "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=true extra=true",
            "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=True connector_created=true",
            "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=generated connector_created=false",
            "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=false connector_created=unknown",
            "TASKDECK_DESKTOP_BOOTSTRAP_V2 jwt_created=true connector_created=true",
        )

        for marker in invalid_markers:
            with self.subTest(marker=marker):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.validate_bootstrap_identity_markers([marker])

    def test_process_monitor_requires_one_bootstrap_marker_before_ready(self) -> None:
        valid_monitor = self._process_monitor(
            "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=false",
            "TASKDECK_DESKTOP_READY url=http://127.0.0.1:54321",
        )
        self.assertEqual(
            (
                "http://127.0.0.1:54321",
                54321,
                {"jwtCreated": True, "connectorCreated": False},
            ),
            valid_monitor.wait_for_ready(timeout_seconds=1),
        )

        invalid_sequences = (
            ("TASKDECK_DESKTOP_READY url=http://127.0.0.1:54321",),
            (
                "TASKDECK_DESKTOP_READY url=http://127.0.0.1:54321",
                "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=true",
            ),
            (
                "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=true",
                "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=true",
                "TASKDECK_DESKTOP_READY url=http://127.0.0.1:54321",
            ),
        )
        for sequence in invalid_sequences:
            with self.subTest(sequence=sequence):
                with self.assertRaises(harness.AcceptanceFailure):
                    self._process_monitor(*sequence).wait_for_ready(timeout_seconds=1)

    def test_retired_provider_failure_output_requires_exact_bounded_secret_free_guidance(self) -> None:
        output = "\n".join(
            (
                "TASKDECK_DESKTOP_STARTING",
                harness.RETIRED_PROVIDER_FATAL_MARKER,
                harness.RETIRED_PROVIDER_FATAL_GUIDANCE,
            )
        )

        harness.validate_retired_provider_failure_output(output)

        invalid_outputs = (
            output.replace(
                harness.RETIRED_PROVIDER_FATAL_MARKER,
                "TASKDECK_DESKTOP_FATAL code=startup_failed",
            ),
            f"{output}\nTASKDECK_DESKTOP_READY url=http://127.0.0.1:5000",
            f"{output}\n{harness.SYNTHETIC_RETIRED_PROVIDER_VALUE}",
            f"{output}\nTaskdeck.Application.Services.RetiredLlmProviderConfigurationException",
            f"{output}\n{'x' * 513}",
        )
        for invalid in invalid_outputs:
            with self.subTest(invalid=invalid[-80:]):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.validate_retired_provider_failure_output(invalid)

    def test_supported_provider_regression_uses_explicit_mock_with_inert_retired_child_setting(self) -> None:
        monitor = mock.Mock()
        monitor.wait_for_ready.return_value = (
            "http://127.0.0.1:5000",
            5000,
            {"jwtCreated": True, "connectorCreated": True},
        )
        with (
            mock.patch.object(harness, "start_packaged_process", return_value=monitor) as start,
            mock.patch.object(harness, "request_health_and_spa") as request_health,
            mock.patch.object(harness, "stop_packaged_process") as stop,
        ):
            harness.verify_supported_provider_ignores_inert_retired_child_settings(
                Path("C:/package/Taskdeck.Api.exe"),
                Path("C:/unrelated-cwd"),
                Path(tempfile.gettempdir()).resolve() / "taskdeck-unit-localappdata",
            )

        environment = start.call_args.args[2]
        self.assertEqual("Mock", environment["Llm__Provider"])
        self.assertEqual(
            harness.SYNTHETIC_RETIRED_PROVIDER_VALUE,
            environment["Llm__Gemini__ApiKey"],
        )
        request_health.assert_called_once_with("http://127.0.0.1:5000")
        stop.assert_called_once_with(monitor)

    def test_clean_bootstrap_gate_requires_created_then_not_created_flags(self) -> None:
        harness.require_bootstrap_identity(
            {"jwtCreated": True, "connectorCreated": True},
            {"jwtCreated": True, "connectorCreated": True},
        )

        for invalid in (
            {"jwtCreated": False, "connectorCreated": True},
            {"jwtCreated": True, "connectorCreated": False},
            {"jwtCreated": False, "connectorCreated": False},
        ):
            with self.subTest(invalid=invalid):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.require_bootstrap_identity(
                        invalid,
                        {"jwtCreated": True, "connectorCreated": True},
                    )

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
            "schemaVersion": 3,
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

    def test_required_live_mode_rejects_skipped_evidence(self) -> None:
        evidence = {
            "schemaVersion": 3,
            "phase": "create",
            "journeyId": "release-123456-789",
            "board": {"id": "synthetic-id", "title": "synthetic"},
            "persistence": {"registered": True, "boardCreated": True},
            "http": [{"method": "POST", "path": "/api/auth/register", "status": 200}],
            "liveOpenAi": {"outcome": "skipped", "reason": "credential_unavailable"},
        }

        with self.assertRaises(harness.AcceptanceFailure):
            harness.validate_phase_evidence(
                evidence,
                "create",
                "release-123456-789",
                require_live_openai=True,
            )

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

    def test_live_capture_evidence_is_an_exact_value_minimized_whitelist(self) -> None:
        evidence = self._live_create_evidence(123)
        harness.validate_phase_evidence(evidence, "create", "release-123456-789")
        live = evidence["liveOpenAi"]

        self.assertEqual(
            {
                "outcome",
                "provider",
                "model",
                "promptVersion",
                "isMock",
                "isProbed",
                "verificationStatus",
                "probeLatencyMs",
                "proposal",
                "cardCounts",
            },
            set(live),
        )
        self.assertEqual(
            {"statusBeforeApproval", "statusAfterApproval", "statusAfterApply", "operationCount"},
            set(live["proposal"]),
        )
        for forbidden_field in (
            "transcript",
            "prompt",
            "response",
            "rawError",
            "log",
            "credential",
            "environment",
            "filesystemPath",
            "userId",
        ):
            forbidden = json.loads(json.dumps(evidence))
            forbidden["liveOpenAi"][forbidden_field] = "must not persist"
            with self.subTest(forbidden_field=forbidden_field):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.validate_phase_evidence(forbidden, "create", "release-123456-789")

    def test_live_capture_evidence_rejects_degraded_attribution_and_unbounded_statuses(self) -> None:
        mutations = (
            ("provider", "Deterministic"),
            ("model", "fallback"),
            ("promptVersion", "deterministic.v1"),
        )
        for field, value in mutations:
            with self.subTest(field=field):
                evidence = self._live_create_evidence(123)
                evidence["liveOpenAi"][field] = value
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.validate_phase_evidence(evidence, "create", "release-123456-789")

        evidence = self._live_create_evidence(123)
        evidence["liveOpenAi"]["proposal"]["statusAfterApproval"] = "Unexpected"
        with self.assertRaises(harness.AcceptanceFailure):
            harness.validate_phase_evidence(evidence, "create", "release-123456-789")

    def test_final_evidence_v7_records_both_explicit_journeys_and_mcp_stdio(self) -> None:
        final = harness.build_final_evidence(
            "taskdeck-v0.1.1-win-x64.zip",
            "a" * 64,
            {"ready": True, "spa": True},
            {"ready": True, "spa": True},
            {"jwtCreated": True, "connectorCreated": True},
            {"jwtCreated": False, "connectorCreated": False},
            {"phase": "create"},
            {"phase": "restart"},
            {
                "initialized": True,
                "serverInfoValid": True,
                "stdoutClean": True,
            },
            {
                "legacy": {"location": "adjacent", "state": "retained"},
                "durable": {"location": "app-data", "state": "imported"},
                "database": {"location": "app-data", "state": "reused"},
                "board": {"location": "app-data", "state": "created"},
            },
            {"phase": "create", "journeyId": "migration-123"},
            {"phase": "restart", "journeyId": "migration-123"},
        )

        self.assertEqual(3, harness.PHASE_EVIDENCE_SCHEMA_VERSION)
        self.assertEqual(7, final["schemaVersion"])
        self.assertEqual({"cleanInstall", "migration"}, set(final) - {"schemaVersion", "release"})
        self.assertEqual(
            {
                "initialized": True,
                "serverInfoValid": True,
                "stdoutClean": True,
            },
            final["cleanInstall"]["mcpStdio"],
        )
        self.assertEqual(
            {"jwtCreated": True, "connectorCreated": True},
            final["cleanInstall"]["launches"][0]["bootstrapIdentity"],
        )
        self.assertEqual(
            {"jwtCreated": False, "connectorCreated": False},
            final["cleanInstall"]["launches"][1]["bootstrapIdentity"],
        )
        self.assertEqual("create", final["migration"]["create"]["phase"])
        self.assertEqual("restart", final["migration"]["restart"]["phase"])

    def test_final_evidence_rejects_non_boolean_or_unknown_bootstrap_fields(self) -> None:
        invalid_identities = (
            {"jwtCreated": "true", "connectorCreated": True},
            {"jwtCreated": True, "connectorCreated": False, "source": "file"},
        )

        for identity in invalid_identities:
            with self.subTest(identity=identity):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.build_final_evidence(
                        "taskdeck-v0.1.1-win-x64.zip",
                        "a" * 64,
                        {"ready": True, "spa": True},
                        {"ready": True, "spa": True},
                        identity,
                        {"jwtCreated": False, "connectorCreated": False},
                        {"phase": "create"},
                        {"phase": "restart"},
                        {
                            "initialized": True,
                            "serverInfoValid": True,
                            "stdoutClean": True,
                        },
                        {
                            "legacy": {"location": "adjacent", "state": "retained"},
                            "durable": {"location": "app-data", "state": "imported"},
                            "database": {"location": "app-data", "state": "reused"},
                            "board": {"location": "app-data", "state": "created"},
                        },
                        {"phase": "create", "journeyId": "migration-123"},
                        {"phase": "restart", "journeyId": "migration-123"},
                    )

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

    def test_synthetic_legacy_state_is_importable_without_retaining_identity_material(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            local_app_data = root / "local-app-data"
            legacy_path = root / "legacy-extract" / "appsettings.local.json"
            legacy_path.parent.mkdir()

            payload = harness.seed_legacy_v01_state(legacy_path, local_app_data)
            durable_path = local_app_data / "Taskdeck" / "appsettings.local.json"
            durable_path.write_bytes(payload)

            harness.assert_legacy_identity_imported_and_retained(
                legacy_path,
                durable_path,
                payload,
            )
            harness.assert_legacy_state_reused(local_app_data / "Taskdeck" / "taskdeck.db")
            harness.assert_data_isolated(root, local_app_data, legacy_path)

    def test_legacy_import_allows_formatting_only_durable_reserialization(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            local_app_data = root / "local-app-data"
            legacy_path = root / "legacy-extract" / "appsettings.local.json"
            legacy_path.parent.mkdir()

            payload = harness.seed_legacy_v01_state(legacy_path, local_app_data)
            durable_path = local_app_data / "Taskdeck" / "appsettings.local.json"
            durable_path.write_text(
                json.dumps(json.loads(payload), indent=2) + "\n",
                encoding="utf-8",
            )

            harness.assert_legacy_identity_imported_and_retained(
                legacy_path,
                durable_path,
                payload,
            )

    def test_legacy_import_rejects_loss_of_non_identity_config_sentinel(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            local_app_data = root / "local-app-data"
            legacy_path = root / "legacy-extract" / "appsettings.local.json"
            legacy_path.parent.mkdir()

            payload = harness.seed_legacy_v01_state(legacy_path, local_app_data)
            durable_path = local_app_data / "Taskdeck" / "appsettings.local.json"
            durable = json.loads(payload)
            self.assertEqual(
                "synthetic-non-identity-setting",
                durable["ArchiveAcceptance"]["Sentinel"],
            )
            del durable["ArchiveAcceptance"]
            durable_path.write_text(json.dumps(durable, separators=(",", ":")), encoding="utf-8")

            with self.assertRaises(harness.AcceptanceFailure):
                harness.assert_legacy_identity_imported_and_retained(
                    legacy_path,
                    durable_path,
                    payload,
                )

    def test_legacy_fixture_refuses_to_overwrite_a_packaged_local_config(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            legacy_path = Path(raw) / "appsettings.local.json"
            legacy_path.write_text("{}", encoding="utf-8")

            with self.assertRaises(harness.AcceptanceFailure):
                harness.require_absent_legacy_fixture_path(legacy_path)

    def test_migration_evidence_is_an_exact_value_free_whitelist(self) -> None:
        evidence = {
            "legacy": {"location": "adjacent", "state": "retained"},
            "durable": {"location": "app-data", "state": "imported"},
            "database": {"location": "app-data", "state": "reused"},
            "board": {"location": "app-data", "state": "created"},
        }
        self.assertEqual(evidence, harness.validate_migration_evidence(evidence))

        evidence["durable"] = {"location": "app-data", "state": "created"}
        with self.assertRaises(harness.AcceptanceFailure):
            harness.validate_migration_evidence(evidence)

    @staticmethod
    def _process_monitor(*markers: str) -> harness.ProcessMonitor:
        class FakeProcess:
            def __init__(self) -> None:
                self.stdout = [f"{marker}\n" for marker in markers]

            @staticmethod
            def poll() -> None:
                return None

        return harness.ProcessMonitor(FakeProcess())

    @staticmethod
    def _live_create_evidence(probe_latency_ms: object) -> dict[str, object]:
        return {
            "schemaVersion": 3,
            "phase": "create",
            "journeyId": "release-123456-789",
            "board": {"id": "synthetic-id", "title": "synthetic"},
            "persistence": {"registered": True, "boardCreated": True},
            "http": [{"method": "POST", "path": "/api/auth/register", "status": 200}],
            "liveOpenAi": {
                "outcome": "passed",
                "provider": "OpenAI",
                "model": "gpt-5.6-luna",
                "promptVersion": "llm-triage.v2",
                "isMock": False,
                "isProbed": True,
                "verificationStatus": "verified",
                "probeLatencyMs": probe_latency_ms,
                "proposal": {
                    "statusBeforeApproval": "PendingReview",
                    "statusAfterApproval": "Approved",
                    "statusAfterApply": "Applied",
                    "operationCount": 1,
                },
                "cardCounts": {"beforeProposal": 0, "afterApproval": 0, "afterApply": 1},
            },
        }

    @staticmethod
    def _mcp_initialize_response() -> str:
        return json.dumps(
            {
                "jsonrpc": "2.0",
                "id": 1,
                "result": {
                    "protocolVersion": "2025-11-25",
                    "capabilities": {},
                    "serverInfo": {"name": "Taskdeck", "version": "1.0.0"},
                },
            },
            separators=(",", ":"),
        ) + "\n"


class InheritedRetiredProviderConfigurationTests(unittest.TestCase):
    """#2233: inherited retired variables start; the same settings in a file stay fatal."""

    @staticmethod
    def _ready_monitor(
        warning_markers: list[str],
        output_lines: list[str] | None = None,
        output_truncated: bool = False,
    ) -> mock.Mock:
        monitor = mock.Mock()
        monitor.wait_for_ready.return_value = (
            "http://127.0.0.1:5000",
            5000,
            {"jwtCreated": True, "connectorCreated": True},
        )
        monitor.warning_markers = warning_markers
        monitor.output_lines = (
            output_lines if output_lines is not None else list(warning_markers)
        )
        monitor.output_truncated = output_truncated
        monitor.seen_markers = {"TASKDECK_DESKTOP_READY"}
        return monitor

    def test_ignored_warning_must_be_announced_exactly_once(self) -> None:
        good_output = [
            "TASKDECK_DESKTOP_STARTING",
            harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER,
            "Taskdeck ignored retired Gemini provider settings left in this profile's environment.",
        ]
        harness.validate_retired_provider_ignored_warning(
            [harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER],
            good_output,
            harness.SYNTHETIC_RETIRED_PROVIDER_VALUE,
        )

        invalid_cases = (
            ([], good_output, False),
            ([harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER] * 2, good_output, False),
            (["TASKDECK_DESKTOP_WARNING code=something_else"], good_output, False),
            # The value leaks on the guidance line, which carries no marker prefix at all —
            # the case a markers-only scan cannot see.
            (
                [harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER],
                good_output + [f"Ignored {harness.SYNTHETIC_RETIRED_PROVIDER_VALUE}"],
                False,
            ),
            # Output past the bound cannot be scanned, so it is a failure, not a pass.
            ([harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER], good_output, True),
        )
        for markers, observed, truncated in invalid_cases:
            with self.subTest(markers=markers, observed=observed[-1:], truncated=truncated):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.validate_retired_provider_ignored_warning(
                        markers,
                        observed,
                        harness.SYNTHETIC_RETIRED_PROVIDER_VALUE,
                        truncated,
                    )

    def test_process_monitor_captures_non_marker_lines_for_the_value_blind_scan(self) -> None:
        monitor = WindowsDesktopArchiveTests._process_monitor(
            "TASKDECK_DESKTOP_STARTING",
            "Taskdeck is starting. Keep this window open while you use Taskdeck.",
            harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER,
            "guidance line with no marker prefix",
        )
        monitor.wait_for_output_completion()

        self.assertIn("guidance line with no marker prefix", monitor.output_lines)
        self.assertFalse(monitor.output_truncated)
        self.assertEqual(
            [harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER],
            monitor.warning_markers,
        )

    def test_inherited_retired_variables_start_with_and_without_a_selector(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            local_app_data = Path(temp).resolve() / "inherited"
            local_app_data.mkdir()
            monitors = [self._ready_monitor([harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER])
                        for _ in range(2)]
            with (
                mock.patch.object(
                    harness, "start_packaged_process", side_effect=monitors
                ) as start,
                mock.patch.object(harness, "request_health_and_spa") as request_health,
                mock.patch.object(harness, "stop_packaged_process"),
            ):
                harness.verify_inherited_retired_provider_configuration_starts(
                    Path("C:/package/Taskdeck.Api.exe"),
                    Path("C:/unrelated-cwd"),
                    local_app_data,
                )

        self.assertEqual(2, start.call_count)
        selector_case, children_case = (call.args[2] for call in start.call_args_list)
        self.assertEqual("Gemini", selector_case["Llm__Provider"])
        self.assertNotIn("Llm__Provider", children_case)
        for environment in (selector_case, children_case):
            self.assertEqual(
                harness.SYNTHETIC_RETIRED_PROVIDER_VALUE,
                environment["Llm__Gemini__ApiKey"],
            )
        self.assertEqual(2, request_health.call_count)
        # Each case gets its own data directory so both see a first-run bootstrap.
        self.assertNotEqual(
            selector_case["LOCALAPPDATA"],
            children_case["LOCALAPPDATA"],
        )

    def test_inherited_retired_variables_require_the_ignored_warning(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            local_app_data = Path(temp).resolve() / "inherited"
            local_app_data.mkdir()
            with (
                mock.patch.object(
                    harness,
                    "start_packaged_process",
                    return_value=self._ready_monitor([]),
                ),
                mock.patch.object(harness, "request_health_and_spa"),
                mock.patch.object(harness, "stop_packaged_process"),
            ):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.verify_inherited_retired_provider_configuration_starts(
                        Path("C:/package/Taskdeck.Api.exe"),
                        Path("C:/unrelated-cwd"),
                        local_app_data,
                    )

    def test_ambient_openai_pin_starts_without_an_ignored_warning(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            local_app_data = Path(temp).resolve()
            with (
                mock.patch.object(
                    harness,
                    "start_packaged_process",
                    return_value=self._ready_monitor([]),
                ) as start,
                mock.patch.object(harness, "request_health_and_spa"),
                mock.patch.object(harness, "stop_packaged_process"),
            ):
                harness.verify_ambient_openai_pins_do_not_block_start(
                    Path("C:/package/Taskdeck.Api.exe"),
                    Path("C:/unrelated-cwd"),
                    local_app_data,
                )

        environment = start.call_args.args[2]
        self.assertEqual("stale-pinned-model", environment["Llm__OpenAi__Model"])
        self.assertNotIn("Llm__Gemini__ApiKey", environment)

    def test_ambient_openai_pin_rejects_an_ignored_warning(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            local_app_data = Path(temp).resolve()
            with (
                mock.patch.object(
                    harness,
                    "start_packaged_process",
                    return_value=self._ready_monitor(
                        [harness.RETIRED_PROVIDER_IGNORED_WARNING_MARKER]
                    ),
                ),
                mock.patch.object(harness, "request_health_and_spa"),
                mock.patch.object(harness, "stop_packaged_process"),
            ):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.verify_ambient_openai_pins_do_not_block_start(
                        Path("C:/package/Taskdeck.Api.exe"),
                        Path("C:/unrelated-cwd"),
                        local_app_data,
                    )

    def test_durable_settings_file_case_writes_the_retired_section_and_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()
            executable = root / "Taskdeck.Api.exe"
            executable.write_bytes(b"stub")
            local_app_data = root / "local-app-data"
            local_app_data.mkdir()
            durable_config = local_app_data / "Taskdeck" / "appsettings.local.json"

            process = mock.Mock()
            process.communicate.return_value = (
                "\n".join(
                    (
                        harness.RETIRED_PROVIDER_FATAL_MARKER,
                        harness.RETIRED_PROVIDER_FATAL_GUIDANCE,
                    )
                ),
                None,
            )
            process.returncode = 1
            with mock.patch.object(harness.subprocess, "Popen", return_value=process):
                harness.verify_retired_provider_configuration_failure(
                    executable,
                    root,
                    local_app_data,
                )

            written = json.loads(durable_config.read_text(encoding="utf-8"))
            self.assertEqual(
                harness.SYNTHETIC_RETIRED_PROVIDER_VALUE,
                written["Llm"]["Gemini"]["ApiKey"],
            )

    def test_durable_settings_file_case_requires_a_nonzero_exit(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()
            executable = root / "Taskdeck.Api.exe"
            executable.write_bytes(b"stub")
            local_app_data = root / "local-app-data"
            local_app_data.mkdir()

            process = mock.Mock()
            process.communicate.return_value = ("TASKDECK_DESKTOP_READY url=http://127.0.0.1:5000", None)
            process.returncode = 0
            with mock.patch.object(harness.subprocess, "Popen", return_value=process):
                with self.assertRaises(harness.AcceptanceFailure):
                    harness.verify_retired_provider_configuration_failure(
                        executable,
                        root,
                        local_app_data,
                    )


if __name__ == "__main__":
    unittest.main()
