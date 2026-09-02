import unittest
from _load import load

m = load('03_IMPLEMENTATION_CANDIDATES/python/telemetry_payload_linter.py', 'telemetry_payload_linter')

ALLOWED = {
    'schema_version', 'event_name', 'installation_id', 'app.version', 'app.os_family',
    'app.install_kind', 'activation.first_capture', 'activation.first_proposal',
    'activation.first_apply', 'feature_area', 'outcome'
}


class TelemetryPayloadLinterTests(unittest.TestCase):
    def test_content_free_payload_passes(self):
        payload = {
            'schema_version': 1,
            'event_name': 'activation_snapshot',
            'installation_id': 'a' * 64,
            'app': {'version': '0.4.0', 'os_family': 'windows', 'install_kind': 'self-hosted'},
            'activation': {'first_capture': True, 'first_proposal': False, 'first_apply': False},
            'feature_area': 'capture-loop',
            'outcome': 'success',
        }
        self.assertEqual([], m.lint(payload, ALLOWED))

    def test_unapproved_content_fields_fail(self):
        payload = {'schema_version': 1, 'card_title': 'My secret task'}
        errors = m.lint(payload, ALLOWED)
        self.assertIn('field_not_allowed:card_title', errors)
        self.assertIn('field_denied:card_title', errors)


if __name__ == '__main__':
    unittest.main()
