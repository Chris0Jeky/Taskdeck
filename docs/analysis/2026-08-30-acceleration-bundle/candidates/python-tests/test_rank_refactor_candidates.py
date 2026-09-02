import unittest
from _load import load

m = load('03_IMPLEMENTATION_CANDIDATES/python/rank_refactor_candidates.py', 'rank_refactor_candidates')


class RankRefactorCandidatesTests(unittest.TestCase):
    def test_ranking_rewards_size_churn_and_touching_commits(self):
        rows = [
            {'path': 'a.cs', 'lines': 900, 'churn': 2500, 'touching_commits': 20},
            {'path': 'b.cs', 'lines': 400, 'churn': 300, 'touching_commits': 5},
        ]
        ranked = m.rank_rows(rows)
        self.assertEqual('a.cs', ranked[0]['path'])
        self.assertGreater(ranked[0]['score'], ranked[1]['score'])

    def test_zero_churn_scores_zero(self):
        ranked = m.rank_rows([{'path': 'empty.cs', 'lines': 10, 'churn': 0, 'touching_commits': 0}])
        self.assertEqual(0.0, ranked[0]['score'])


if __name__ == '__main__':
    unittest.main()
