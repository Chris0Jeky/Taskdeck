import tempfile
import unittest
from pathlib import Path
from _load import load

m = load('03_IMPLEMENTATION_CANDIDATES/ops/backup_manifest.py', 'backup_manifest')


class BackupManifestTests(unittest.TestCase):
    def test_create_and_verify_detects_mutation(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            db = root / 'taskdeck.db'
            db.write_bytes(b'abc123')
            manifest = m.create_manifest(root)
            self.assertEqual([], m.verify_manifest(root, manifest))
            db.write_bytes(b'changed')
            self.assertTrue(any('mismatch' in error for error in m.verify_manifest(root, manifest)))

    def test_absolute_manifest_path_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            manifest = {
                'schema_version': 1,
                'hash_algorithm': 'sha256',
                'files': [{'path': '/etc/passwd', 'byte_size': 0, 'sha256': '0' * 64}],
            }
            self.assertIn('path_invalid:/etc/passwd', m.verify_manifest(root, manifest))


if __name__ == '__main__':
    unittest.main()
