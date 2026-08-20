from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from typing import Mapping


RETIRED_WRAPPER = "TASKDECK_LLM_GEMINI_API_KEY"
PRESENCE_MARKER = "TaskdeckMigration__RetiredLlmProviderConfigurationPresent"
SYNTHETIC_RETIRED_VALUE = "synthetic-retired-wrapper-value-never-forward"
SAFE_PROCESS_ENVIRONMENT_NAMES = (
    "PATH",
    "PATHEXT",
    "SYSTEMROOT",
    "WINDIR",
    "COMSPEC",
    "PROGRAMFILES",
    "PROGRAMFILES(X86)",
    "PROGRAMW6432",
    "COMMONPROGRAMFILES",
    "COMMONPROGRAMFILES(X86)",
    "COMMONPROGRAMW6432",
    "HOME",
    "USERPROFILE",
    "HOMEDRIVE",
    "HOMEPATH",
    "APPDATA",
    "LOCALAPPDATA",
    "TEMP",
    "TMP",
    "XDG_CONFIG_HOME",
    "XDG_RUNTIME_DIR",
    "DOCKER_CONFIG",
    "DOCKER_CONTEXT",
    "DOCKER_HOST",
    "DOCKER_TLS_VERIFY",
    "DOCKER_CERT_PATH",
)


class RetiredLlmComposeMigrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.repository_root = Path(__file__).resolve().parents[2]
        cls.compose_file = cls.repository_root / "deploy" / "docker-compose.yml"
        cls.base_environment = _compose_environment(os.environ)
        cls.temp_directory = tempfile.TemporaryDirectory(prefix="taskdeck-compose-migration-")
        cls.empty_env_file = Path(cls.temp_directory.name) / "empty.env"
        cls.empty_env_file.write_text("", encoding="utf-8")
        _run_compose_config(
            cls.compose_file,
            cls.base_environment,
            env_file=cls.empty_env_file,
        )

    @classmethod
    def tearDownClass(cls) -> None:
        cls.temp_directory.cleanup()

    def test_process_environment_maps_retired_wrapper_to_true_only(self) -> None:
        environment = dict(self.base_environment)
        environment[RETIRED_WRAPPER] = SYNTHETIC_RETIRED_VALUE

        resolved = _run_compose_config(
            self.compose_file,
            environment,
            env_file=self.empty_env_file,
        )

        self.assertEqual("true", _api_environment(resolved)[PRESENCE_MARKER])

    def test_env_file_maps_retired_wrapper_to_true_only(self) -> None:
        with tempfile.TemporaryDirectory(prefix="taskdeck-compose-migration-") as raw:
            env_file = Path(raw) / "migration.env"
            env_file.write_text(
                f"{RETIRED_WRAPPER}={SYNTHETIC_RETIRED_VALUE}\n",
                encoding="utf-8",
            )

            resolved = _run_compose_config(
                self.compose_file,
                self.base_environment,
                env_file=env_file,
            )

        self.assertEqual("true", _api_environment(resolved)[PRESENCE_MARKER])

    def test_absent_retired_wrapper_maps_to_inert_empty_marker(self) -> None:
        resolved = _run_compose_config(
            self.compose_file,
            self.base_environment,
            env_file=self.empty_env_file,
        )

        self.assertEqual("", _api_environment(resolved)[PRESENCE_MARKER])


def _compose_environment(source: Mapping[str, str]) -> dict[str, str]:
    environment = {}
    for name in SAFE_PROCESS_ENVIRONMENT_NAMES:
        value = source.get(name)
        if value is not None:
            environment[name] = value

    environment.update(
        {
            "TASKDECK_JWT_SECRET": "synthetic-compose-only-jwt-secret-with-sufficient-length",
            "TASKDECK_CONNECTORS_ENCRYPTION_KEY": "c3ludGhldGljLWNvbXBvc2Utb25seS1rZXktMzJiIQ==",
        }
    )
    return environment


def _run_compose_config(
    compose_file: Path,
    environment: Mapping[str, str],
    *,
    env_file: Path | None = None,
) -> dict[str, object]:
    command = ["docker", "compose"]
    if env_file is not None:
        command.extend(["--env-file", str(env_file)])
    command.extend(
        [
            "-f",
            str(compose_file),
            "--profile",
            "baseline",
            "config",
            "--format",
            "json",
        ]
    )
    try:
        result = subprocess.run(
            command,
            cwd=str(compose_file.parent),
            env=dict(environment),
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise AssertionError("Docker Compose config could not run for the migration regression.") from exc

    combined_output = f"{result.stdout}\n{result.stderr}"
    if SYNTHETIC_RETIRED_VALUE in combined_output:
        raise AssertionError("Docker Compose exposed the synthetic retired wrapper value.")
    if result.returncode != 0:
        raise AssertionError("Docker Compose config failed for the migration regression.")

    try:
        document = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise AssertionError("Docker Compose config did not return valid JSON.") from exc
    if not isinstance(document, dict):
        raise AssertionError("Docker Compose config returned an unexpected document shape.")
    return document


def _api_environment(document: Mapping[str, object]) -> Mapping[str, object]:
    services = document.get("services")
    if not isinstance(services, dict):
        raise AssertionError("Docker Compose config omitted services.")
    api = services.get("api")
    if not isinstance(api, dict):
        raise AssertionError("Docker Compose config omitted the API service.")
    environment = api.get("environment")
    if not isinstance(environment, dict):
        raise AssertionError("Docker Compose config returned an unexpected API environment shape.")
    if set(key for key in environment if key == PRESENCE_MARKER) != {PRESENCE_MARKER}:
        raise AssertionError("Docker Compose config omitted the retired-wrapper presence marker.")
    return environment


if __name__ == "__main__":
    unittest.main()
