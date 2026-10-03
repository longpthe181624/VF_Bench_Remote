"""Đồng bộ Database cho Client/Qauto; chọn bản file bằng ID và SHA-256.

Không chọn bản theo ngày upload, không tự thay file của lượt test đang chạy.
Chỉ dùng thư viện chuẩn Python, có thể import vào Qauto hoặc chạy CLI.
"""
import argparse
import hashlib
import json
import os
import re
import tempfile
import threading
import urllib.parse
import urllib.request


class DatabaseClient:
    def __init__(self, console, cache_path=None):
        self.console = console.rstrip('/')
        if urllib.parse.urlsplit(self.console).scheme not in ('http', 'https'):
            raise ValueError('Console phải là URL HTTP/HTTPS')
        self.cache_path = cache_path
        self.files = {}
        self.last_error = None
        self._lock = threading.Lock()
        self._refresh_lock = threading.Lock()
        self._wake = threading.Event()
        self._stop = threading.Event()
        self._thread = None

    def _json(self, path):
        with urllib.request.urlopen(self.console + '/api/client/database/' + path, timeout=30) as response:
            return json.load(response)

    def refresh(self):
        with self._refresh_lock:
            rows, page = {}, 1
            while True:
                body = self._json(f'manifest?page={page}&size=500')
                for item in body['items']:
                    rows[item['id']] = item
                if page * body['size'] >= body['total']:
                    break
                page += 1
            # Thay snapshot một lần; không giữ file đã xoá hoặc trạng thái cũ.
            if self.cache_path:
                folder = os.path.dirname(os.path.abspath(self.cache_path))
                os.makedirs(folder, exist_ok=True)
                fd, temporary = tempfile.mkstemp(dir=folder, prefix='database-', suffix='.tmp')
                try:
                    with os.fdopen(fd, 'w', encoding='utf-8') as output:
                        json.dump({'items': list(rows.values())}, output, ensure_ascii=False, indent=2)
                    os.replace(temporary, self.cache_path)
                finally:
                    if os.path.exists(temporary):
                        os.unlink(temporary)
            with self._lock:
                self.files = rows
                self.last_error = None
            return list(rows.values())

    def start(self):
        if not self._thread or not self._thread.is_alive():
            self._stop.clear()
            self._thread = threading.Thread(target=self._loop, daemon=True, name='database-sync')
            self._thread.start()
        self.notify_changed()

    def notify_changed(self):
        self._wake.set()

    def _loop(self):
        while not self._stop.is_set():
            self._wake.wait(60)
            self._wake.clear()
            if self._stop.is_set():
                break
            try:
                self.refresh()
            except Exception as error:
                # Cache cũ chỉ để hiển thị; chọn file luôn kiểm lại trạng thái qua API.
                self.last_error = str(error)

    def close(self):
        self._stop.set()
        self._wake.set()

    def pin(self, file_id, sha256, testing=False):
        if not re.fullmatch(r'[0-9a-fA-F]{64}', sha256 or ''):
            raise ValueError('Cần SHA-256 cụ thể của bản file đã chọn')
        if not isinstance(file_id, int) or file_id < 1:
            raise ValueError('Cần ID file cụ thể')
        file = self._json(f'files/{file_id}')
        if file['id'] != file_id or file['sha256'].lower() != sha256.lower():
            raise ValueError('ID / SHA-256 không khớp bản đã chọn')
        if file['status'] not in ('Release', 'Draft'):
            raise ValueError('Trạng thái file không hợp lệ')
        if file['status'] == 'Draft' and not testing:
            raise ValueError('File đang Draft; phải chọn chế độ kiểm thử Draft rõ ràng')
        return dict(file)

    def download(self, file_id, sha256, destination, testing=False):
        file = self.pin(file_id, sha256, testing)
        name = file['tenFile']
        if not name or name in ('.', '..') or '/' in name or '\\' in name or ':' in name:
            raise ValueError('Tên file không hợp lệ')
        # Mỗi ID có thư mục riêng, hai phiên bản trùng tên không ghi đè nhau.
        folder = os.path.join(os.path.abspath(destination), str(file_id))
        os.makedirs(folder, exist_ok=True)
        fd, temporary = tempfile.mkstemp(dir=folder, suffix='.part')
        try:
            url = self.console + f'/api/client/database/files/{file_id}/download?' + urllib.parse.urlencode({'sha256': sha256, 'testing': str(testing).lower()})
            digest, size = hashlib.sha256(), 0
            with os.fdopen(fd, 'wb') as output, urllib.request.urlopen(url, timeout=60) as response:
                while True:
                    chunk = response.read(64 * 1024)
                    if not chunk:
                        break
                    size += len(chunk)
                    if size > 256 * 1024 * 1024:
                        raise ValueError('File vượt 256 MB')
                    digest.update(chunk)
                    output.write(chunk)
            if digest.hexdigest() != sha256.lower() or size != file['kichThuoc']:
                raise ValueError('Nội dung tải về không khớp SHA-256 / dung lượng')
            target = os.path.join(folder, name)
            os.replace(temporary, target)
            return {'file': file, 'path': target}
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)


def main():
    parser = argparse.ArgumentParser(description='Database Client — chọn ID và SHA-256, không chọn latest')
    parser.add_argument('--console', required=True)
    parser.add_argument('--file-id', type=int)
    parser.add_argument('--sha256')
    parser.add_argument('--testing', action='store_true', help='Cho phép bản Draft đã chọn cụ thể')
    parser.add_argument('--dest', default='DatabaseFiles')
    args = parser.parse_args()
    client = DatabaseClient(args.console)
    if args.file_id is None:
        print(json.dumps(client.refresh(), ensure_ascii=False, indent=2))
    else:
        print(json.dumps(client.download(args.file_id, args.sha256, args.dest, args.testing), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
