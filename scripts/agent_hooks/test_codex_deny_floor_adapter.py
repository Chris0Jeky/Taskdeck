from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / ".codex" / "deny_floor_adapter.py"
SPEC = importlib.util.spec_from_file_location("taskdeck_deny_floor_adapter", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"cannot load {MODULE_PATH}")
adapter = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(adapter)


def payload(command: str = "git status --short --branch") -> str:
    return json.dumps(
        {
            "hook_event_name": "PreToolUse",
            "tool_name": "Bash",
            "tool_input": {"command": command},
            "cwd": str(ROOT),
        }
    )


def output_document(output: str) -> dict[str, object]:
    parsed = json.loads(output)
    if not isinstance(parsed, dict):
        raise AssertionError(f"adapter output was not an object: {parsed!r}")
    return parsed


def deny_reason(output: str) -> str:
    parsed = output_document(output)
    hook_output = parsed.get("hookSpecificOutput")
    if not isinstance(hook_output, dict):
        raise AssertionError(f"adapter output had no hookSpecificOutput: {parsed!r}")
    if hook_output.get("permissionDecision") != "deny":
        raise AssertionError(f"adapter output was not a deny: {parsed!r}")
    reason = hook_output.get("permissionDecisionReason")
    if not isinstance(reason, str):
        raise AssertionError(f"adapter deny had no reason: {parsed!r}")
    return reason


class CodexDenyFloorAdapterTests(unittest.TestCase):
    def make_dispatcher(self, directory: Path, body: str) -> tuple[Path, str]:
        source = (
            f'FLOOR_VERSION = "{adapter.EXPECTED_FLOOR_VERSION}"\n'
            + textwrap.dedent(body).lstrip()
            + "\n"
        )
        path = directory / "dispatch.py"
        path.write_text(source, encoding="utf-8")
        digest = hashlib.sha256(source.encode("utf-8")).hexdigest()
        return path, digest

    def invoke_fixture(
        self,
        dispatcher: Path,
        digest: str,
        *,
        raw_payload: str | None = None,
        environment: dict[str, str] | None = None,
    ) -> str | None:
        return adapter.invoke_dispatcher(
            payload() if raw_payload is None else raw_payload,
            dispatcher,
            expected_hash=digest,
            environment=environment,
            timeout_seconds=2.0,
        )

    def test_contract_constants_match_hooks_definition(self) -> None:
        hooks = json.loads((ROOT / ".codex" / "hooks.json").read_text(encoding="utf-8"))
        self.assertEqual(hooks.get("description"), "Taskdeck's reviewed, pinned Bash-command deny-floor adapter")
        groups = hooks["hooks"]["PreToolUse"]
        self.assertEqual(len(groups), 1)
        self.assertEqual(groups[0]["matcher"], "^Bash$")
        handlers = groups[0]["hooks"]
        self.assertEqual(len(handlers), 1)
        handler = handlers[0]
        self.assertEqual(handler["type"], "command")
        self.assertEqual(handler["timeout"], 5)
        for field in ("command", "commandWindows"):
            command = handler[field]
            self.assertIn(adapter.EXPECTED_DISPATCHER_SHA256, command)
            self.assertIn(".claude/hooks/dispatch.py", command)
            self.assertIn("--event pre --runtime codex", command)
            self.assertIn("invoke_deny_floor", command)
        config = (ROOT / ".codex" / "config.toml").read_text(encoding="utf-8")
        self.assertNotIn("\nGIT_CONFIG_GLOBAL =", config)

    def test_normalized_hash_matches_producer_line_ending_contract(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "dispatcher.py"
            path.write_bytes(b"one\r\ntwo\rthree\n")
            expected = hashlib.sha256(b"one\ntwo\nthree\n").hexdigest()
            self.assertEqual(adapter.normalized_text_sha256(path), expected)

    def test_account_home_ignores_hostile_process_home(self) -> None:
        hostile_environment = {"HOME": "C:\\foreign\\runtime-home", "USERPROFILE": "Z:\\spoofed"}
        rooted = Path(Path.cwd().anchor or "/")
        with mock.patch.dict(os.environ, hostile_environment, clear=False):
            posix = adapter.account_home(
                platform_name="posix",
                posix_resolver=lambda: rooted / "real/account",
            )
            windows = adapter.account_home(
                platform_name="nt",
                windows_resolver=lambda: rooted / "real/windows-account",
            )
        self.assertEqual(posix, rooted / "real/account")
        self.assertEqual(windows, rooted / "real/windows-account")

    def test_dispatcher_suffix_is_exact_and_account_anchored(self) -> None:
        actual = adapter.dispatcher_path(
            adapter.DISPATCHER_SUFFIX,
            home_resolver=lambda: Path("/account/home"),
        )
        self.assertEqual(actual, Path("/account/home/.claude/hooks/dispatch.py"))
        with self.assertRaises(adapter.AdapterFailure):
            adapter.dispatcher_path(
                "../repo/dispatch.py", home_resolver=lambda: Path("/account/home")
            )

    def test_unknown_and_malformed_bash_payloads_fail_closed(self) -> None:
        malformed = (
            "{not-json",
            "[]",
            "{}",
            json.dumps({"tool_name": "Read", "tool_input": {"command": "git status"}}),
            json.dumps({"tool_name": "Bash", "tool_input": []}),
            json.dumps({"tool_name": "Bash", "tool_input": {"command": ""}}),
            json.dumps({"tool_name": "Bash", "tool_input": {"command": 42}}),
            json.dumps(
                {
                    "hook_event_name": "PostToolUse",
                    "tool_name": "Bash",
                    "tool_input": {"command": "git status"},
                    "cwd": str(ROOT),
                }
            ),
            json.dumps(
                {
                    "hook_event_name": "PreToolUse",
                    "tool_name": "Bash",
                    "tool_input": {"command": "git status"},
                }
            ),
            json.dumps(
                {
                    "hook_event_name": "PreToolUse",
                    "tool_name": "Bash",
                    "tool_input": {"command": "git status"},
                    "cwd": "relative/path",
                }
            ),
        )
        for raw in malformed:
            with self.subTest(raw=raw), self.assertRaises(adapter.AdapterFailure):
                adapter.validate_bash_payload(raw)

    def test_malformed_arguments_return_attributed_deny(self) -> None:
        result = adapter.run(payload(), ["--event", "pre", "--runtime", "codex"])
        self.assertIsNotNone(result)
        self.assertTrue(deny_reason(result).startswith(adapter.ATTRIBUTION))

    def test_identity_mismatch_fails_closed_before_execution(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dispatcher, _digest = self.make_dispatcher(
                Path(directory), "raise SystemExit('must not execute')"
            )
            with self.assertRaisesRegex(adapter.AdapterFailure, "identity differs"):
                adapter.invoke_dispatcher(
                    payload(), dispatcher, expected_hash="0" * 64, timeout_seconds=1.0
                )

    def test_dispatcher_replacement_during_execution_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dispatcher, digest = self.make_dispatcher(Path(directory), "pass")

            def replacing_runner(*_args: object, **_kwargs: object) -> subprocess.CompletedProcess[str]:
                dispatcher.write_text(
                    f'FLOOR_VERSION = "{adapter.EXPECTED_FLOOR_VERSION}"\n# replaced\n',
                    encoding="utf-8",
                )
                return subprocess.CompletedProcess([], 0, "", "")

            with self.assertRaisesRegex(adapter.AdapterFailure, "identity differs"):
                adapter.invoke_dispatcher(
                    payload(),
                    dispatcher,
                    expected_hash=digest,
                    runner=replacing_runner,
                )

    def test_empty_success_is_the_only_allow_shape(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dispatcher, digest = self.make_dispatcher(
                Path(directory),
                """
                import json, sys
                json.load(sys.stdin)
                """,
            )
            self.assertIsNone(self.invoke_fixture(dispatcher, digest))

            whitespace_dispatcher, whitespace_digest = self.make_dispatcher(
                Path(directory), "print('   ')"
            )
            with self.assertRaises(adapter.AdapterFailure):
                self.invoke_fixture(whitespace_dispatcher, whitespace_digest)

        windows_launcher = (ROOT / ".codex" / "invoke_deny_floor.ps1").read_text(
            encoding="utf-8"
        )
        self.assertNotIn("2>$null", windows_launcher)
        self.assertIn("-3 -I -B", windows_launcher)
        self.assertIn("Windows bridge wrote unexpected diagnostic output", windows_launcher)
        posix_launcher = (ROOT / ".codex" / "invoke_deny_floor.sh").read_text(
            encoding="utf-8"
        )
        self.assertIn('"$python" -I -B', posix_launcher)

    def test_valid_deny_is_attributed_and_unknown_fields_are_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dispatcher, digest = self.make_dispatcher(
                Path(directory),
                """
                import json, os, sys
                json.load(sys.stdin)
                print(json.dumps({
                    "producerField": {"preserved": True},
                    "hookSpecificOutput": {
                        "hookEventName": "PreToolUse",
                        "permissionDecision": "deny",
                        "permissionDecisionReason": "producer denied on " + os.environ.get("GH_HOST", "missing"),
                        "futureField": 7,
                    },
                }))
                """,
            )
            environment = dict(os.environ)
            environment["GH_HOST"] = "hostile.enterprise.invalid"
            result = self.invoke_fixture(
                dispatcher, digest, environment=environment
            )
            self.assertIsNotNone(result)
            document = output_document(result)
            self.assertEqual(document["producerField"], {"preserved": True})
            hook_output = document["hookSpecificOutput"]
            self.assertEqual(hook_output["futureField"], 7)
            self.assertEqual(
                hook_output["permissionDecisionReason"],
                f"{adapter.ATTRIBUTION} producer denied on hostile.enterprise.invalid",
            )

    def test_malformed_or_unknown_dispatcher_results_fail_closed(self) -> None:
        bodies = {
            "malformed": "print('not-json')",
            "non-object": "print('[]')",
            "missing-output": "print('{}')",
            "unknown-event": """
                import json
                print(json.dumps({"hookSpecificOutput": {"hookEventName": "PostToolUse", "permissionDecision": "deny", "permissionDecisionReason": "x"}}))
            """,
            "unknown-decision": """
                import json
                print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "allow", "permissionDecisionReason": "x"}}))
            """,
            "missing-reason": """
                import json
                print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny"}}))
            """,
        }
        for label, body in bodies.items():
            with self.subTest(label=label), tempfile.TemporaryDirectory() as directory:
                dispatcher, digest = self.make_dispatcher(Path(directory), body)
                with self.assertRaises(adapter.AdapterFailure):
                    self.invoke_fixture(dispatcher, digest)

    def test_nonzero_stderr_and_timeout_each_fail_closed(self) -> None:
        bodies = {
            "nonzero": "raise SystemExit(7)",
            "stderr": "import sys; print('unexpected', file=sys.stderr)",
            "whitespace-stderr": "import sys; print('   ', file=sys.stderr)",
            "timeout": "import time; time.sleep(1)",
        }
        for label, body in bodies.items():
            with self.subTest(label=label), tempfile.TemporaryDirectory() as directory:
                dispatcher, digest = self.make_dispatcher(Path(directory), body)
                timeout = 0.05 if label == "timeout" else 1.0
                with self.assertRaises(adapter.AdapterFailure):
                    adapter.invoke_dispatcher(
                        payload(),
                        dispatcher,
                        expected_hash=digest,
                        timeout_seconds=timeout,
                    )

    def test_exact_platform_command_survives_hostile_environment(self) -> None:
        hooks = json.loads((ROOT / ".codex" / "hooks.json").read_text(encoding="utf-8"))
        handler = hooks["hooks"]["PreToolUse"][0]["hooks"][0]
        environment = dict(os.environ)
        environment["HOME"] = "C:\\foreign\\runtime-home"
        environment["GH_HOST"] = "hostile.enterprise.invalid"
        environment.pop("GIT_CONFIG_GLOBAL", None)
        hostile_pythonpath = tempfile.TemporaryDirectory()
        self.addCleanup(hostile_pythonpath.cleanup)
        Path(hostile_pythonpath.name, "json.py").write_text(
            "raise RuntimeError('hostile PYTHONPATH was imported')\n", encoding="utf-8"
        )
        environment["PYTHONPATH"] = hostile_pythonpath.name

        if os.name == "nt":
            system_root = os.environ.get("SystemRoot", r"C:\Windows")
            powershell = Path(system_root) / "System32/WindowsPowerShell/v1.0/powershell.exe"
            if not powershell.is_file():
                self.skipTest(f"Windows PowerShell not available at {powershell}")
            try:
                dispatcher = adapter.dispatcher_path(adapter.DISPATCHER_SUFFIX)
                adapter.validate_dispatcher_identity(dispatcher)
            except adapter.AdapterFailure as exc:
                self.skipTest(f"current installed 1.6.18 dispatcher unavailable: {exc}")
            environment["PATH"] = str(ROOT / "intentionally-missing-path")
            command = handler["commandWindows"]
            allow = subprocess.run(
                [str(powershell), "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
                cwd=ROOT,
                env=environment,
                input=payload(),
                text=True,
                capture_output=True,
                timeout=12,
                check=False,
            )
            self.assertEqual(allow.returncode, 0, allow.stderr)
            self.assertEqual(allow.stdout.strip(), "", allow.stdout)
            deny = subprocess.run(
                [str(powershell), "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
                cwd=ROOT,
                env=environment,
                input=payload(
                    "git push --force --dry-run origin HEAD:refs/heads/codex-hook-canary"
                ),
                text=True,
                capture_output=True,
                timeout=12,
                check=False,
            )
            self.assertEqual(deny.returncode, 0, deny.stderr)
            reason = deny_reason(deny.stdout)
            self.assertTrue(reason.startswith(adapter.ATTRIBUTION), reason)
            self.assertIn("[floor 1.6.18 (2026-07-27)]", reason)
        else:
            # Hosted POSIX runners do not install the user's shared floor. The
            # exact adapter must still start with a stripped inherited PATH,
            # ignore hostile HOME, and produce an attributed deny rather than
            # silently allowing the command.
            environment["PATH"] = "/intentionally/missing"
            result = subprocess.run(
                ["/bin/sh", "-c", handler["command"]],
                cwd=ROOT,
                env=environment,
                input=payload(),
                text=True,
                capture_output=True,
                timeout=12,
                check=False,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue(deny_reason(result.stdout).startswith(adapter.ATTRIBUTION))


if __name__ == "__main__":
    unittest.main()
