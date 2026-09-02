import datetime as dt
import unittest
from _load import load

m = load('03_IMPLEMENTATION_CANDIDATES/smart-ci/validate_quarantine.py', 'validate_quarantine')


class ValidateQuarantineTests(unittest.TestCase):
    def test_valid_entry_passes(self):
        data = {'schema_version': 1, 'entries': [{
            'test': 'A.Tests.Flaky',
            'issue': '#123',
            'owner': 'ci-owner',
            'reason': 'Known deterministic runner race under investigation.',
            'created_on': '2026-08-30',
            'expires_on': '2026-09-10',
            'compensating_coverage': 'nightly-windows'
        }]}
        self.assertEqual([], m.validate_document(data, dt.date(2026, 8, 30)))

    def test_expired_and_missing_fields_fail(self):
        data = {'schema_version': 1, 'entries': [{
            'test': 'A.Tests.Flaky',
            'issue': '#123',
            'reason': 'Known deterministic runner race under investigation.',
            'created_on': '2026-08-01',
            'expires_on': '2026-08-29',
            'compensating_coverage': 'nightly-windows'
        }]}
        errors = m.validate_document(data, dt.date(2026, 8, 30))
        self.assertTrue(any('expired' in error for error in errors))
        self.assertTrue(any('.owner:required' in error for error in errors))


if __name__ == '__main__':
    unittest.main()
