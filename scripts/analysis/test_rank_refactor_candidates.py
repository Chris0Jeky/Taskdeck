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

    def test_log_parser_handles_normal_rename_binary_spaces_and_tabs(self) -> None:
        commit = "a" * 40
        raw = (
            b"\x01" + commit.encode("ascii") + b"\0"
            b"\n:100644 100644 1111111 2222222 M\0dir/file with spaces.cs\0"
            b":100644 100644 3333333 4444444 R100\0old.cs\0new.cs\0"
            b":100644 100644 5555555 6666666 M\0image.bin\0"
            b":000000 100644 0000000 7777777 A\0dir/file\twith-tab.ts\0"
            b"2\t1\tdir/file with spaces.cs\0"
            b"0\t0\t\0old.cs\0new.cs\0"
            b"-\t-\timage.bin\0"
            b"3\t4\tdir/file\twith-tab.ts\0"
        )
        self.assertEqual(
            [
                (
                    commit,
                    {
                        "dir/file with spaces.cs": ("M", None, 2, 1),
                        "new.cs": ("R", "old.cs", 0, 0),
                        "image.bin": ("M", None, None, None),
                        "dir/file\twith-tab.ts": ("A", None, 3, 4),
                    },
                )
            ],
            ranker.parse_git_log_z(raw),
        )

    def test_log_parser_skips_paths_that_are_not_valid_utf8(self) -> None:
        commit = "b" * 40
        raw = (
            b"\x01" + commit.encode("ascii") + b"\0"
            b"\n:000000 100644 0000000 1111111 A\0notes-\xff.txt\0"
            b":000000 100644 0000000 2222222 A\0src/a.cs\0"
            b"1\t0\tnotes-\xff.txt\0"
            b"2\t0\tsrc/a.cs\0"
        )
        self.assertEqual([(commit, {"src/a.cs": ("A", None, 2, 0)})], ranker.parse_git_log_z(raw))

    def test_literal_backslash_paths_are_not_rewritten(self) -> None:
        self.assertEqual("src/a\\b.py", ranker._decode_git_path(b"src/a\\b.py"))

    def test_unsafe_paths_are_still_rejected(self) -> None:
        with self.assertRaises(ranker.AnalysisError):
            ranker._decode_git_path(b"../escape.cs")

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
        self.assertFalse(report["sourceStateAuthoritative"])
        self.assertEqual(4, report["candidates"][0]["lines"])

    def test_baseline_kind_separates_tags_from_exploratory_baselines(self) -> None:
        self.assertEqual("tag", self.report()["baseRefKind"])
        branch_report = ranker.build_report(self.repo, "main", frozenset({".cs"}), 20)
        self.assertEqual("branch", branch_report["baseRefKind"])
        self.assertTrue(branch_report["sourceStateAuthoritative"])

    def test_assume_unchanged_worktree_content_cannot_change_head_line_count(self) -> None:
        self.git("update-index", "--assume-unchanged", "src/new.cs")
        self.write("src/new.cs", "dirty\n" * 52)
        report = self.report()
        self.assertTrue(report["trackedTreeClean"])
        self.assertEqual(4, report["candidates"][0]["lines"])

    def test_replacement_blob_cannot_change_exact_head_line_count(self) -> None:
        original_blob = self.git("rev-parse", "HEAD:src/new.cs")
        self.write("replacement.txt", "replacement\n" * 52)
        replacement_blob = self.git("hash-object", "-w", "replacement.txt")
        self.git("replace", original_blob, replacement_blob)
        self.assertEqual(52, len(self.git("cat-file", "-p", original_blob).splitlines()))

        report = self.report()

        self.assertTrue(report["sourceStateAuthoritative"])
        self.assertEqual(4, report["candidates"][0]["lines"])
        self.assertEqual("ignored", report["gitObjectPolicy"]["replacementObjects"])

    def test_graft_metadata_is_rejected(self) -> None:
        grafts_path = Path(self.git("rev-parse", "--path-format=absolute", "--git-path", "info/grafts"))
        grafts_path.parent.mkdir(parents=True, exist_ok=True)
        grafts_path.write_text(f"{self.git('rev-parse', 'HEAD')}\n", encoding="utf-8")

        with self.assertRaisesRegex(ranker.AnalysisError, "graft metadata"):
            self.report()

    def test_merge_commit_does_not_recount_merged_branch_changes(self) -> None:
        self.git("switch", "-c", "feature")
        self.write("src/new.cs", "one\ntwo\nthree\nfour\nfive\n")
        self.git("add", ".")
        self.git("commit", "-m", "feature edit")
        self.git("switch", "main")
        self.write("README.md", "force a non-fast-forward merge\n")
        self.git("add", ".")
        self.git("commit", "-m", "main edit")
        self.git("merge", "--no-ff", "feature", "-m", "merge feature")

        candidate = self.report()["candidates"][0]
        self.assertEqual(3, candidate["churn"])
        self.assertEqual(4, candidate["touchingCommits"])

    def _merge_sibling_edit_and_rename(self, rename_branch_is_second_parent: bool) -> dict[str, object]:
        """Edit `src/new.cs` under its old name on one branch, rename it on another, merge."""

        self.git("switch", "-c", "rename-branch")
        self.git("mv", "src/new.cs", "src/renamed.cs")
        self.git("commit", "-m", "rename on sibling branch")
        self.git("switch", "main")
        self.write("src/new.cs", "one\ntwo\nthree\nfour\nfive\n")
        self.git("add", ".")
        self.git("commit", "-m", "edit old name on sibling branch")
        if rename_branch_is_second_parent:
            self.git("merge", "--no-ff", "rename-branch", "-m", "merge rename")
        else:
            self.git("switch", "rename-branch")
            self.git("merge", "--no-ff", "main", "-m", "merge edit")
        return self.report()

    def test_sibling_branch_rename_and_edit_are_attributed_regardless_of_merge_side(self) -> None:
        # The rename and the edit to the old name are incomparable commits, so Git may
        # linearise either first. Both orders must report the same lineage totals.
        report = self._merge_sibling_edit_and_rename(rename_branch_is_second_parent=True)
        candidate = report["candidates"][0]
        self.assertEqual("src/renamed.cs", candidate["path"])
        self.assertEqual(3, candidate["churn"])
        self.assertEqual(5, candidate["touchingCommits"])

    def test_sibling_branch_attribution_is_identical_with_the_merge_taken_the_other_way(self) -> None:
        report = self._merge_sibling_edit_and_rename(rename_branch_is_second_parent=False)
        candidate = report["candidates"][0]
        self.assertEqual("src/renamed.cs", candidate["path"])
        self.assertEqual(3, candidate["churn"])
        self.assertEqual(5, candidate["touchingCommits"])

    def test_deleted_path_history_is_not_inherited_by_a_later_occupant(self) -> None:
        self.git("rm", "src/new.cs")
        self.git("commit", "-m", "delete new.cs")
        self.write("src/unrelated.cs", "alpha\n")
        self.git("add", ".")
        self.git("commit", "-m", "add unrelated")
        self.git("mv", "src/unrelated.cs", "src/new.cs")
        self.git("commit", "-m", "reuse the deleted path")

        candidate = self.report()["candidates"][0]
        self.assertEqual("src/new.cs", candidate["path"])
        # Only the unrelated file's own creation and its rename, never the deleted file's
        # two earlier edits or its deletion.
        self.assertEqual(1, candidate["churn"])
        self.assertEqual(2, candidate["touchingCommits"])

    def test_global_attributes_file_cannot_change_reported_churn(self) -> None:
        baseline = self.report()["candidates"][0]
        attributes = self.repo / "global.gitattributes"
        attributes.write_text("*.cs binary\n", encoding="utf-8")
        self.git("config", "core.attributesFile", str(attributes))

        candidate = self.report()["candidates"][0]
        self.assertEqual(baseline["churn"], candidate["churn"])
        self.assertEqual(baseline["touchingCommits"], candidate["touchingCommits"])

    def test_colliding_json_and_csv_destinations_are_rejected(self) -> None:
        destination = self.repo / "artifacts" / "report.json"
        exit_code = ranker.main(
            [
                "--repo",
                str(self.repo),
                "--base",
                "baseline",
                "--json-out",
                str(destination),
                "--csv-out",
                str(destination),
            ]
        )
        self.assertEqual(2, exit_code)
        self.assertFalse(destination.exists())

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
