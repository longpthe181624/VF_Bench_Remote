import hashlib
import json
import os
import tempfile
import unittest
from unittest.mock import patch
from database_client import DatabaseClient


class Response:
    def __init__(self, data):
        self.data = data
    def __enter__(self):
        return self
    def __exit__(self, *args):
        pass
    def read(self, size=None):
        if size is None:
            return self.data
        chunk, self.data = self.data[:size], self.data[size:]
        return chunk


class DatabaseClientTests(unittest.TestCase):
    def setUp(self):
        self.old = b'old database'
        self.new = b'new database'
        self.files = [dict(id=i, sha256=hashlib.sha256(data).hexdigest(), kichThuoc=len(data), tenFile='same.dbc', status=status, phienBan=str(i))
                      for i, data, status in [(1, self.old, 'Release'), (2, self.new, 'Draft')]]
        self.client = DatabaseClient('http://console')

    def request(self, url, **kwargs):
        if '/download?' in url:
            return Response(self.old if '/files/1/' in url else self.new)
        if 'manifest?' in url:
            return Response(json.dumps(dict(items=self.files, total=2, size=500)).encode())
        return Response(json.dumps(self.files[int(url.rsplit('/', 1)[1]) - 1]).encode())

    def test_explicit_old_id_and_isolated_versions(self):
        with patch('urllib.request.urlopen', side_effect=self.request), tempfile.TemporaryDirectory() as root:
            selected = self.client.download(1, self.files[0]['sha256'], root)
            newer = self.client.download(2, self.files[1]['sha256'], root, testing=True)
            with open(selected['path'], 'rb') as file:
                self.assertEqual(file.read(), self.old)
            self.assertNotEqual(selected['path'], newer['path'])
            self.assertEqual(selected['file']['id'], 1)

    def test_draft_requires_explicit_testing_and_sha_required(self):
        with patch('urllib.request.urlopen', side_effect=self.request):
            with self.assertRaisesRegex(ValueError, 'Draft'):
                self.client.pin(2, self.files[1]['sha256'])
            with self.assertRaisesRegex(ValueError, 'SHA-256'):
                self.client.pin(1, None)
            with self.assertRaisesRegex(ValueError, 'khớp'):
                self.client.pin(1, self.files[1]['sha256'])

    def test_change_status_does_not_modify_pinned_file(self):
        with patch('urllib.request.urlopen', side_effect=self.request):
            pinned = self.client.pin(1, self.files[0]['sha256'])
            self.files[0]['status'] = 'Draft'
            self.client.refresh()
            self.assertEqual(pinned['status'], 'Release')
            self.assertEqual(self.client.files[1]['status'], 'Draft')
            with self.assertRaisesRegex(ValueError, 'Draft'):
                self.client.pin(1, self.files[0]['sha256'])

    def test_hash_mismatch_removes_partial_download(self):
        def wrong(url, **kwargs):
            return Response(b'corrupted') if '/download?' in url else self.request(url, **kwargs)
        with patch('urllib.request.urlopen', side_effect=wrong), tempfile.TemporaryDirectory() as root:
            with self.assertRaisesRegex(ValueError, 'SHA-256'):
                self.client.download(1, self.files[0]['sha256'], root)
            self.assertEqual(os.listdir(os.path.join(root, '1')), [])

    def test_cache_is_replaced_and_handles_deleted_files(self):
        with patch('urllib.request.urlopen', side_effect=self.request), tempfile.TemporaryDirectory() as root:
            self.client.cache_path = os.path.join(root, 'manifest.json')
            self.client.refresh()
            self.files = self.files[:1]
            self.client.refresh()
            self.assertNotIn(2, self.client.files)
            with open(self.client.cache_path, encoding='utf-8') as file:
                self.assertEqual(len(json.load(file)['items']), 1)


if __name__ == '__main__':
    unittest.main()
