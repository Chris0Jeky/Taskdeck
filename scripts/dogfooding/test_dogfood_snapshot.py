from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("dogfood-snapshot.py")
SPEC = importlib.util.spec_from_file_location("dogfood_snapshot", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class RedactPathTests(unittest.TestCase):
    def test_posix_home_boundary_and_case_are_preserved(self) -> None:
        cases = (
            ("/home/alice", "/home/alice", False, "~"),
            ("/home/alice/project/taskdeck.db", "/home/alice", False, "~/project/taskdeck.db"),
            ("/home/alice-client/acme/taskdeck.db", "/home/alice", False, "taskdeck.db"),
            ("/home/Alice/project/taskdeck.db", "/home/alice", False, "taskdeck.db"),
        )

        for path, home, windows, expected in cases:
            with self.subTest(path=path):
                self.assertEqual(MODULE.redact(path, home=home, windows=windows), expected)

    def test_windows_drive_and_unc_roots_use_windows_boundaries(self) -> None:
        cases = (
            ("C:\\Users\\Alice\\Project\\taskdeck.db", "C:\\Users\\alice", "~/Project/taskdeck.db"),
            ("C:\\taskdeck.db", "C:\\", "~/taskdeck.db"),
            ("\\\\server\\share\\alice\\Project\\taskdeck.db", "\\\\server\\share\\alice\\", "~/Project/taskdeck.db"),
        )

        for path, home, expected in cases:
            with self.subTest(path=path):
                self.assertEqual(MODULE.redact(path, home=home, windows=True), expected)

    def test_relative_paths_and_home_root_remain_shareable(self) -> None:
        self.assertEqual(
            MODULE.redact("relative/taskdeck.db", home="/home/alice", windows=False),
            "taskdeck.db",
        )
        self.assertEqual(
            MODULE.redact("/etc/taskdeck.db", home="/", windows=False),
            "~/etc/taskdeck.db",
        )

    def test_control_characters_are_escaped_without_disclosing_the_home(self) -> None:
        result = MODULE.redact("/home/alice/notes/line\nbreak\x00.db", home="/home/alice", windows=False)

        self.assertEqual(result, r"~/notes/line\x0abreak\x00.db")
        self.assertNotIn("\n", result)
        self.assertNotIn("alice", result)


if __name__ == "__main__":
    unittest.main()
