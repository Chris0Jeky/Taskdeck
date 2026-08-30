from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("rank_refactor_candidates.py")
SPEC = importlib.util.spec_from_file_location("rank_refactor_candidates", MODULE_PATH)
assert SPEC and SPEC.loader
ranker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ranker)


class RefactorRankerUnitTests(unittest.TestCase):
    def test_score_rewards_size_churn_and_touch_frequency(self) -> None:
        rows = [
            {"path": "large.cs", "lines": 900, "churn": 2500, "touchingCommits": 20},
            {"path": "small.cs", "lines": 400, "churn": 300, "touchingCommits": 5},
        ]
        ranked = ranker.rank_rows(rows)
        self.assertEqual("large.cs", ranked[0]["path"])
        self.assertGreater(ranked[0]["score"], ranked[1]["score"])

    def test_zero_lines_or_churn_scores_zero(self) -> None:
        self.assertEqual(0.0, ranker.score(0, 10, 2))
        self.assertEqual(0.0, ranker.score(10, 0, 2))

    def test_rank_order_is_deterministic_under_score_ties(self) -> None:
        rows = [
            {"path": "z.cs", "lines": 10, "churn": 10, "touchingCommits": 1},
            {"path": "a.cs", "lines": 10, "churn": 10, "touchingCommits": 1},
        ]
        self.assertEqual(["a.cs", "z.cs"], [row["path"] for row in ranker.rank_rows(rows)])

    def test_candidate_filter_is_case_insensitive_and_excludes_generated_content(self) -> None:
        extensions = frozenset({".cs", ".ts"})
        self.assertTrue(ranker.is_candidate("backend/Useful.CS", extensions))
        self.assertFalse(ranker.is_candidate("backend/Migrations/Useful.cs", extensions))
        self.assertFalse(ranker.is_candidate("frontend/node_modules/useful.ts", extensions))
        self.assertFalse(ranker.is_candidate("backend/BoardModelSnapshot.cs", extensions))
        self.assertFalse(ranker.is_candidate("frontend/package-lock.json", extensions))

    def test_numstat_z_parser_handles_normal_rename_binary_spaces_and_tabs(self) -> None:
        raw = (
            b"2\t1\tdir/file with spaces.cs\0"
            b"0\t0\t\0old.cs\0new.cs\0"
            b"-\t-\timage.bin\0"
            b"3\t4\tdir/file\twith-tab.ts\0"
        )
        self.assertEqual(
            [
                (2, 1, "dir/file with spaces.cs", None),
                (0, 0, "new.cs", "old.cs"),
                (None, None, "image.bin", None),
                (3, 4, "dir/file\twith-tab.ts", None),
            ],
            ranker.parse_numstat_z(raw),
        )

    def test_invalid_extension_is_rejected(self) -> None:
        with self.assertRaises(ranker.AnalysisError):
            ranker.parse_extensions(".cs,../secret")


class RefactorRankerRepositoryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.repo = Path(self.temporary_directory.name).resolve()
        self.git("init", "--initial-branch=main")
        self.git("config", "user.name", "Taskdeck Tests")
        self.git("config", "user.email", "taskdeck-tests@example.invalid")
        self.write("src/old.cs", "one\ntwo\n")
        self.git("add", ".")
        self.git("commit", "-m", "base")
        self.git("tag", "baseline")

        self.write("src/old.cs", "one\ntwo\nthree\n")
        self.git("add", ".")
        self.git("commit", "-m", "modify old name")
        self.git("mv", "src/old.cs", "src/new.cs")
        self.git("commit", "-m", "rename source")
        self.write("src/new.cs", "one\ntwo\nthree\nfour\n")
        self.git("add", ".")
        self.git("commit", "-m", "modify new name")

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def git(self, *args: str) -> str:
        result = subprocess.run(
            ["git", "-C", str(self.repo), *args],
            check=True,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        return result.stdout.strip()

    def write(self, relative: str, content: str) -> None:
        path = self.repo / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def report(self, allow_dirty: bool = False) -> dict[str, object]:
        return ranker.build_report(self.repo, "baseline", frozenset({".cs"}), 20, allow_dirty)

    def test_rename_history_is_attributed_to_current_path(self) -> None:
        report = self.report()
        candidate = report["candidates"][0]
        self.assertEqual("src/new.cs", candidate["path"])
        self.assertEqual(2, candidate["churn"])
        self.assertEqual(3, candidate["touchingCommits"])

    def test_report_is_deterministic_and_does_not_write_to_repository(self) -> None:
        before = self.git("status", "--porcelain=v1", "--untracked-files=all")
        first = json.dumps(self.report(), sort_keys=True)
        second = json.dumps(self.report(), sort_keys=True)
        after = self.git("status", "--porcelain=v1", "--untracked-files=all")
        self.assertEqual(first, second)
        self.assertEqual(before, after)

    def test_dirty_tracked_tree_is_rejected_by_default(self) -> None:
        self.write("src/new.cs", "dirty\n")
        with self.assertRaisesRegex(ranker.AnalysisError, "Tracked files are dirty"):
            self.report()

    def test_allow_dirty_marks_report_non_authoritative(self) -> None:
        self.write("src/new.cs", "dirty\n")
        report = self.report(allow_dirty=True)
        self.assertFalse(report["trackedTreeClean"])
        self.assertFalse(report["authoritative"])

    def test_unresolved_base_is_rejected(self) -> None:
        with self.assertRaises(ranker.AnalysisError):
            ranker.build_report(self.repo, "missing", frozenset({".cs"}), 20)

    def test_non_ancestor_base_is_rejected(self) -> None:
        tree = self.git("rev-parse", "HEAD^{tree}")
        unrelated = self.git("commit-tree", tree, "-m", "unrelated root")
        with self.assertRaisesRegex(ranker.AnalysisError, "ancestor"):
            ranker.build_report(self.repo, unrelated, frozenset({".cs"}), 20)


if __name__ == "__main__":
    unittest.main()
