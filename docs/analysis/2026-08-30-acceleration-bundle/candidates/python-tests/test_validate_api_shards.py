import unittest
from _load import load

m = load('03_IMPLEMENTATION_CANDIDATES/smart-ci/validate_api_shards.py', 'validate_api_shards')


class ValidateApiShardsTests(unittest.TestCase):
    def test_exact_union_is_valid(self):
        inventory = ['A.Tests', 'B.Tests']
        manifest = {'shards': [
            {'name': 'core', 'tests': ['A.Tests']},
            {'name': 'mcp', 'tests': ['B.Tests']},
        ]}
        self.assertEqual([], m.validate(inventory, manifest))

    def test_missing_duplicate_and_unknown_are_reported(self):
        inventory = ['A.Tests', 'B.Tests']
        manifest = {'shards': [
            {'name': 'one', 'tests': ['A.Tests', 'X.Tests']},
            {'name': 'two', 'tests': ['A.Tests']},
        ]}
        errors = m.validate(inventory, manifest)
        self.assertIn('test_missing:B.Tests', errors)
        self.assertTrue(any(error.startswith('test_duplicate:A.Tests:') for error in errors))
        self.assertIn('test_unknown:X.Tests', errors)


if __name__ == '__main__':
    unittest.main()
