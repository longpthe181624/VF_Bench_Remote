#!/usr/bin/env python3
"""
Bench Console — agent thật chạy trên máy bench.

Khác `bench_simulator.py` ở chỗ căn bản: simulator bịa số, agent này không bịa
gì cả. Mọi thứ nó báo về đều đọc từ máy thật — log Qauto, kênh PCAN, danh sách
tiến trình, `adb devices`.

Nửa giao thức (MQTT, Last Will, ack, status, result) bê từ simulator vì phần
đó đã chạy thật qua LAN giữa hai máy. Nửa cảm biến là phần mới.

Chạy:
    python bench_agent.py --once                      # in một lần rồi thoát, không cần broker
    python bench_agent.py --host 192.168.0.132 --id QAUTO-01 --model vf6

Máy bench phải được đăng ký trước trên Console, nếu không backend cố ý bỏ dữ
liệu (xem quy ước trong CLAUDE.md). Đăng ký bằng:

    curl -X POST http://<máy A>:5000/api/benches -H "Content-Type: application/json" \
      -d '{"code":"QAUTO-01","model":"vf6","workshop":"...","primaryChannel":null}'

In tiếng Việt trên Windows cần PYTHONIOENCODING=utf-8, không thì lỗi cp1252.
"""

import argparse
import ctypes as C
import json
import os
import re
import shutil
import signal
import socket
import subprocess
import time
import hashlib
import tempfile
import threading
import unicodedata
import urllib.error
import urllib.request
import zipfile
from ctypes import wintypes as W
from dataclasses import dataclass
from datetime import datetime, timezone

import paho.mqtt.client as mqtt


LOG_QAUTO_MAC_DINH = r"D:\Qauto_2610\Qauto_2610\Logs\log.txt"
ADB_QAUTO_MAC_DINH = r"D:\Qauto_2610\Qauto_2610\ADB\adb.exe"
AUTOTESTS_MAC_DINH = r"D:\Qauto_2610\Qauto_2610\AutoTests"

# Trần kích thước gói, phải khớp KhoGoiTestCase.KichThuocToiDa bên C#.
# Kiểm cả hai đầu: máy A chặn lúc tải lên, agent chặn lúc tải về — giữa hai
# đầu còn một chặng mạng và một cấu hình có thể lệch.
GOI_TOI_DA = 64 * 1024 * 1024


# Tên máy cố định suốt vòng đời tiến trình, đọc một lần là đủ.
#
# Gửi kèm mọi gói status để backend đối chiếu với tên máy đã khai cho bench.
# Mã bench là do agent TỰ KHAI qua `--id`, không có gì kiểm chứng — mang máy
# sang bench khác mà quên đổi là Console gán nhầm danh tính mà không ai biết.
TEN_MAY = socket.gethostname()


def ma_model(ten: str | None) -> str:
    """`VF8New ME` → `vf8new-me`. Phải khớp `MaModel.Ma()` bên C#.

    Tên model do tester đặt có khoảng trắng và chữ hoa. Topic vẫn chấp nhận
    được, nhưng khoảng trắng trong topic hành hạ mọi thứ phía sau — log, URL,
    dòng lệnh `mosquitto_sub`. Hai đầu phải chuẩn hoá GIỐNG NHAU, nếu không
    cùng một bench sẽ hiện hai kiểu topic trên broker.
    """
    if not ten:
        return ""
    ra = []
    for c in unicodedata.normalize("NFD", ten):
        if unicodedata.combining(c):
            continue
        if c.isalnum():
            ra.append(c.lower())
        elif ra and ra[-1] != "-":
            ra.append("-")
    return "".join(ra).strip("-")


def bay_gio() -> str:
    return datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")


# ==========================================================================
# Cảm biến — mọi hàm dưới đây CHỈ ĐỌC, không mở bus, không gửi lệnh
# ==========================================================================

def tien_trinh_dang_chay(ten: str) -> bool:
    """Dùng tasklist thay vì psutil để agent không cần cài thêm gói nào."""
    try:
        out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {ten}", "/NH"],
                             capture_output=True, text=True, timeout=10).stdout
    except (subprocess.TimeoutExpired, OSError):
        return False
    return ten.lower() in out.lower()


def kenh_can() -> list[dict]:
    """Hỏi PCANBasic.dll xem có mấy kênh và kênh nào đang bị chiếm.

    Chỉ gọi CAN_GetValue — KHÔNG gọi CAN_Initialize, nên agent không hề lên
    bus. Đây là điều kiện bắt buộc: kỹ sư ngồi tại bench đang dùng chính cái
    adapter này, agent mà mở bus là tranh nhau phần cứng.

    Kênh báo OCCUPIED nghĩa là có chương trình khác (Qauto/VDSA/PCAN-View)
    đang giữ, tức bench đang có người dùng tại chỗ.
    """
    try:
        dll = C.WinDLL(r"C:\WINDOWS\System32\PCANBasic.dll")
    except OSError:
        return []

    class ChanInfo(C.Structure):
        _fields_ = [("handle", W.WORD), ("device_type", C.c_ubyte),
                    ("controller", C.c_ubyte), ("features", W.DWORD),
                    ("name", C.c_char * 33), ("device_id", W.DWORD),
                    ("condition", W.DWORD)]

    PCAN_NONEBUS = 0
    SO_KENH, DANH_SACH = 0x2A, 0x2B

    n = W.DWORD(0)
    if dll.CAN_GetValue(W.WORD(PCAN_NONEBUS), C.c_ubyte(SO_KENH),
                        C.byref(n), C.sizeof(n)) != 0 or n.value == 0:
        return []

    arr = (ChanInfo * n.value)()
    if dll.CAN_GetValue(W.WORD(PCAN_NONEBUS), C.c_ubyte(DANH_SACH),
                        C.byref(arr), C.sizeof(arr)) != 0:
        return []

    ten_tt = {0: "unavailable", 1: "available", 2: "occupied", 3: "pcanview"}
    return [{"handle": f"0x{c.handle:03X}",
             "ten": c.name.decode(errors="replace"),
             "controller": c.controller,
             "tinh_trang": ten_tt.get(c.condition, str(c.condition))}
            for c in arr]


# VID của các hãng làm adapter CAN đã gặp trên bench thật.
VID_ADAPTER_CAN = {
    "1FC9": "NXP",           # VCAN_D1266 — adapter nội bộ, WinUSB thuần
    "0C72": "PEAK-System",
    "0BFD": "Kvaser",
    "1CBE": "IXXAT/HMS",
}

_cache_quet: tuple[float, dict] = (0.0, {"usb": [], "ung_dung": []})

# Một lần gọi PowerShell lấy cả hai thứ, vì mỗi lần gọi tốn vài trăm ms mà cả
# thiết bị USB lẫn tình trạng ứng dụng đều đổi chậm.
_PS_QUET = (
    "$u = Get-PnpDevice -PresentOnly | "
    "Select-Object FriendlyName,InstanceId,Status; "
    "$p = Get-Process Qauto,VDSA -ErrorAction SilentlyContinue | "
    "Select-Object Name,Id,Responding,"
    "@{n='Start';e={$_.StartTime.ToString('o')}},"
    "@{n='Title';e={$_.MainWindowTitle}}; "
    "@{usb=@($u); ung_dung=@($p)} | ConvertTo-Json -Depth 4 -Compress"
)


def quet_may(tuoi_toi_da: float = 30.0) -> dict:
    """Quét USB tìm adapter CAN, và hỏi tình trạng thật của Qauto/VDSA.

    PCANBasic chỉ biết phần cứng PEAK. Bench thật còn dùng adapter khác — đã
    gặp `VCAN_D1266` (NXP, WinUSB thuần, không COM port, không class driver
    CAN). Nếu chỉ hỏi PCANBasic thì agent báo "không có adapter" trong khi dây
    vẫn cắm tốt, đẩy người trực đi mò nhầm chỗ.

    Lọc USB theo VID là chính. Lọc theo tên chỉ nhận "CAN" VIẾT HOA, vì so
    không phân biệt hoa thường sẽ dính cả "Scanner".
    """
    global _cache_quet
    gio = time.time()
    if gio - _cache_quet[0] < tuoi_toi_da:
        return _cache_quet[1]

    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", _PS_QUET],
                             capture_output=True, text=True, timeout=25).stdout
        tho = json.loads(out) if out.strip() else {}
    except (subprocess.TimeoutExpired, OSError, json.JSONDecodeError):
        return _cache_quet[1]

    usb = []
    for d in tho.get("usb") or []:
        ten = d.get("FriendlyName") or ""
        iid = (d.get("InstanceId") or "").upper()
        hang = next((h for v, h in VID_ADAPTER_CAN.items() if f"VID_{v}" in iid), None)
        if hang is None and "CAN" not in ten:
            continue
        usb.append({"ten": ten, "hang": hang or "không rõ",
                    "tinh_trang_windows": d.get("Status"), "id": iid})

    ket_qua = {"usb": usb, "ung_dung": tho.get("ung_dung") or []}
    _cache_quet = (gio, ket_qua)
    return ket_qua


def log_dang_song(duong_dan: str) -> str:
    """Qauto XOAY log: `log.txt` đầy thì nó chuyển sang `log_001.txt`.

    Đã trả giá 23/09: agent soi `log.txt` (đã về hưu từ 09:32) rồi kết luận
    Qauto "chưa sẵn sàng", trong khi Qauto vẫn chạy ngon và ghi vào
    `log_001.txt`. Luôn lấy file `log*.txt` mới nhất trong thư mục, đừng bám
    vào một tên cố định.
    """
    thu_muc = os.path.dirname(duong_dan) or "."
    try:
        ung_vien = [os.path.join(thu_muc, t) for t in os.listdir(thu_muc)
                    if t.lower().startswith("log") and t.lower().endswith(".txt")]
        ung_vien = [t for t in ung_vien if os.path.isfile(t)]
    except OSError:
        return duong_dan
    return max(ung_vien, key=os.path.getmtime) if ung_vien else duong_dan


def danh_gia_qauto(ung_dung: list[dict], log: str) -> dict:
    """Tiến trình tồn tại KHÔNG có nghĩa là ứng dụng dùng được.

    Dùng hai bằng chứng rẻ: cửa sổ chính đã có tiêu đề chưa, và log có được
    ghi SAU mốc khởi động tiến trình chưa.

    Cảnh báo cho người sửa sau: mốc log phải lấy qua `log_dang_song()`. Bản
    đầu soi thẳng `log.txt` và kết luận Qauto "chưa sẵn sàng" suốt buổi, trong
    khi Qauto chạy hoàn toàn bình thường — nó đã xoay sang `log_001.txt` từ
    lâu. Kết luận sai kiểu này đắt hơn là không kết luận gì.
    """
    q = next((u for u in ung_dung if (u.get("Name") or "").lower() == "qauto"), None)
    if q is None:
        return {"tien_trinh": False, "danh_gia": "tat"}

    co_tieu_de = bool((q.get("Title") or "").strip())

    log_sau_khoi_dong = None
    try:
        khoi_dong = datetime.fromisoformat(q["Start"])
        ghi_cuoi = datetime.fromtimestamp(
            os.path.getmtime(log_dang_song(log))).astimezone()
        log_sau_khoi_dong = ghi_cuoi > khoi_dong
    except (KeyError, ValueError, OSError, TypeError):
        pass

    if co_tieu_de or log_sau_khoi_dong:
        danh_gia = "san_sang"
    elif log_sau_khoi_dong is False:
        danh_gia = "chua_san_sang"
    else:
        danh_gia = "khong_ro"

    return {"tien_trinh": True, "co_tieu_de": co_tieu_de,
            "log_sau_khoi_dong": log_sau_khoi_dong, "danh_gia": danh_gia}


def mhu_adb(adb: str) -> list[str]:
    """MHU còn phản hồi không. Dùng luôn adb.exe của Qauto để khỏi đụng
    phiên bản adb khác trên máy — hai adb server khác bản sẽ đá nhau."""
    if not os.path.exists(adb):
        return []
    try:
        out = subprocess.run([adb, "devices"], capture_output=True,
                             text=True, timeout=15).stdout
    except (subprocess.TimeoutExpired, OSError):
        return []
    return [d.split("\t")[0] for d in out.splitlines()[1:] if "\tdevice" in d]


# ==========================================================================
# Nhận gói test case từ Console và bung vào AutoTests
# ==========================================================================

class GoiHong(Exception):
    """Gói không dùng được. Nói rõ lý do để Console hiện lại cho người gửi."""


def _duong_dan_an_toan(ten: str) -> str:
    """Chuẩn hoá một mục trong file nén, ném lỗi nếu nó cố thoát ra ngoài.

    Gói này đi qua mạng rồi được bung trên máy bench, nên một mục tên
    `..\\..\\Windows\\System32\\x.dll` sẽ ghi đè bừa ra ngoài thư mục đích.
    Python KHÔNG tự chặn việc đó khi ta tự ghép đường dẫn, nên phải tự kiểm.
    """
    ten = ten.replace("\\", "/")
    if ten.startswith("/") or (len(ten) > 1 and ten[1] == ":"):
        raise GoiHong(f"Gói chứa đường dẫn tuyệt đối: {ten!r}")
    phan = [p for p in ten.split("/") if p not in ("", ".")]
    if any(p == ".." for p in phan):
        raise GoiHong(f"Gói chứa đường dẫn thoát ra ngoài: {ten!r}")
    return "/".join(phan)


def _bo_vo_boc(ten_muc: list[str]) -> str:
    """Nếu cả gói nằm gọn trong MỘT thư mục gốc thì trả về tên thư mục đó.

    Nén một thư mục bằng WinRAR hay Explorer thường ra gói có đúng một thư mục
    ở gốc. Bung thẳng sẽ thành `AutoTests/<tên>/<tên>/...` — lồng hai lần, đúng
    cái đã thấy với `9VN_all_language`. Qauto quét đệ quy nên vẫn chạy, nhưng
    cây trong giao diện thừa một tầng, người nhìn khó chịu.
    """
    goc = {p.split("/")[0] for p in ten_muc if p}
    return goc.pop() if len(goc) == 1 else ""


# --------------------------------------------------------------------------
# Nguồn file kết quả
#
# ĐÂY LÀ CHỖ DUY NHẤT phải sửa khi chuyển từ chạy giả lập sang chạy thật. Mọi
# phần còn lại của luồng — nhận lệnh, nộp báo cáo, trả verdict — không cần biết
# file đến từ đâu.
# --------------------------------------------------------------------------

OUTPUT_QAUTO_MAC_DINH = r"D:\Qauto_2610\Qauto_2610\Output\OutputLog"


def _ket_qua_tu_sinh(test_case: str | None, cmd_id: str) -> list[str]:
    """Sinh một file mô tả lượt chạy giả lập.

    Dùng khi chưa nối được test thật. Nội dung nói THẲNG là giả lập — nhét một
    file trông giống log thật vào đây là gieo dữ liệu giả vào lịch sử, mà kiểu
    hỏng im lặng đó đắt hơn nhiều so với việc không có file nào.
    """
    fd, ra = tempfile.mkstemp(suffix=".txt", prefix="ket-qua-gia-lap-")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write("===== BÁO CÁO GIẢ LẬP — KHÔNG PHẢI KẾT QUẢ TEST THẬT =====\n")
        f.write(f"bench     : {TEN_MAY}\n")
        f.write(f"cmd_id    : {cmd_id}\n")
        f.write(f"test_case : {test_case or '(không nêu)'}\n")
        f.write(f"sinh lúc  : {datetime.now().isoformat(timespec='seconds')}\n")
        f.write("\nAgent chưa điều khiển được Qauto nên không có lượt chạy nào.\n"
                "File này chỉ để chứng minh đường nộp báo cáo đã thông.\n")
    return [ra]


def _ket_qua_tu_qauto(thu_muc_output: str) -> list[str]:
    """Lấy toàn bộ file của lượt chạy MỚI NHẤT trong Output/OutputLog.

    Cấu trúc Qauto sinh ra:
        OutputLog/<yyyy-MM-dd-HH-mm-ss>/<TenTestCase>/<HH-mm-ss-fff>/
    Đọc theo thư mục này thì tránh hẳn chuyện Qauto xoay log, và mỗi lượt chạy
    là một bộ file độc lập.
    """
    if not os.path.isdir(thu_muc_output):
        raise GoiHong(f"Không thấy thư mục Output của Qauto: {thu_muc_output}")

    luot = []
    for goc, _, files in os.walk(thu_muc_output):
        if files:
            luot.append((os.path.getmtime(goc), goc, files))
    if not luot:
        raise GoiHong(f"Chưa có lượt chạy nào trong {thu_muc_output}")

    _, goc, files = max(luot)
    return [os.path.join(goc, f) for f in files]


def tim_file_ket_qua(spec: str, test_case: str | None, cmd_id: str) -> list[str]:
    """Quy ra danh sách file để nộp lên Console.

    `spec` nhận ba dạng:
        tu-sinh          sinh file mô tả — luôn có, dùng khi chạy giả lập
        qauto[:<đường>]  lượt chạy mới nhất trong Output/OutputLog của Qauto
        mau:<đường>      một file, hoặc mọi file trong một thư mục
    """
    spec = (spec or "tu-sinh").strip()

    if spec == "tu-sinh":
        return _ket_qua_tu_sinh(test_case, cmd_id)

    if spec == "qauto" or spec.startswith("qauto:"):
        duong = spec[6:] if spec.startswith("qauto:") else OUTPUT_QAUTO_MAC_DINH
        return _ket_qua_tu_qauto(duong)

    if spec.startswith("mau:"):
        duong = spec[4:]
        if os.path.isdir(duong):
            ra = [os.path.join(duong, f) for f in sorted(os.listdir(duong))
                  if os.path.isfile(os.path.join(duong, f))]
            if not ra:
                raise GoiHong(f"Thư mục mẫu rỗng: {duong}")
            return ra
        if os.path.isfile(duong):
            return [duong]
        raise GoiHong(f"Không thấy file hay thư mục mẫu: {duong}")

    raise GoiHong(f"Không hiểu --ket-qua={spec!r}. Dùng tu-sinh, qauto, "
                  "qauto:<đường dẫn> hoặc mau:<đường dẫn>")


def nop_bao_cao(url: str, duong_dan: list[str], test_case: str | None,
                bench_code: str) -> int:
    """Đẩy file bằng chứng lên Console bằng multipart, chỉ dùng thư viện chuẩn.

    Không dùng `requests` vì máy bench trong xưởng thường bị khoá, cài thêm gói
    là phiền. Dựng multipart bằng tay chỉ tốn chừng hai chục dòng.
    """
    ranh = "----BenchConsole" + os.urandom(12).hex()
    than = bytearray()
    CRLF = b"\r\n"

    def truong(ten: str, gia_tri: str) -> None:
        than.extend(b"--" + ranh.encode() + CRLF)
        than.extend(f'Content-Disposition: form-data; name="{ten}"'.encode() + CRLF + CRLF)
        than.extend(gia_tri.encode("utf-8") + CRLF)

    if test_case:
        truong("testCase", test_case)
    truong("benchCode", bench_code)

    for d in duong_dan:
        ten = os.path.basename(d)
        than.extend(b"--" + ranh.encode() + CRLF)
        than.extend(
            f'Content-Disposition: form-data; name="file"; filename="{ten}"'.encode()
            + CRLF + b"Content-Type: application/octet-stream" + CRLF + CRLF)
        with open(d, "rb") as f:
            than.extend(f.read())
        than.extend(CRLF)

    than.extend(b"--" + ranh.encode() + b"--" + CRLF)

    req = urllib.request.Request(
        url, data=bytes(than), method="POST",
        headers={"Content-Type": f"multipart/form-data; boundary={ranh}"})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            r.read()
            return r.status
    except urllib.error.HTTPError as ex:
        chi_tiet = ex.read()[:200].decode("utf-8", "replace")
        raise GoiHong(f"Console trả HTTP {ex.code} khi nhận báo cáo: {chi_tiet}") from ex
    except urllib.error.URLError as ex:
        raise GoiHong(f"Không với tới Console ở {url}: {ex.reason}") from ex


def sha256_file(duong_dan: str) -> str:
    """Băm theo khối, không nạp cả file vào RAM."""
    h = hashlib.sha256()
    with open(duong_dan, "rb") as f:
        for khoi in iter(lambda: f.read(1024 * 1024), b""):
            h.update(khoi)
    return h.hexdigest()


def tai_goi(url: str, sha256_mong_doi: str, thu_muc_tam: str | None = None) -> str:
    """Tải gói test case từ Console về, kiểm sha256 rồi mới trả đường dẫn.

    Kiểm sha256 là bắt buộc chứ không phải cho chắc: tải dở giữa chừng mà vẫn
    bung là rải file hỏng vào `AutoTests/`, Qauto vẫn chạy nhưng chạy một bài
    không còn đúng nữa — mà verdict của Qauto vốn đã không phản ánh kết quả
    thật, nên sẽ chẳng có gì báo động.
    """
    if not sha256_mong_doi or len(sha256_mong_doi) != 64:
        raise GoiHong(f"sha256 trong lệnh không hợp lệ: {sha256_mong_doi!r}")

    fd, tam = tempfile.mkstemp(suffix=".zip", prefix="goi-", dir=thu_muc_tam)
    os.close(fd)
    try:
        with urllib.request.urlopen(url, timeout=60) as r:
            # Content-Length có thể thiếu, nên vẫn phải đếm lúc ghi.
            dai = r.headers.get("Content-Length")
            if dai and int(dai) > GOI_TOI_DA:
                raise GoiHong(f"Gói {int(dai) // 1024 // 1024} MB, vượt trần "
                              f"{GOI_TOI_DA // 1024 // 1024} MB")
            da = 0
            with open(tam, "wb") as f:
                while True:
                    khoi = r.read(256 * 1024)
                    if not khoi:
                        break
                    da += len(khoi)
                    if da > GOI_TOI_DA:
                        raise GoiHong("Gói vượt trần "
                                      f"{GOI_TOI_DA // 1024 // 1024} MB khi đang tải")
                    f.write(khoi)
    except urllib.error.HTTPError as ex:
        os.unlink(tam)
        raise GoiHong(f"Console trả HTTP {ex.code} khi tải gói") from ex
    except urllib.error.URLError as ex:
        os.unlink(tam)
        # Lỗi hay gặp nhất: backend máy A bind localhost nên máy bench không với
        # tới. Nói rõ URL để người trực biết phải sửa cấu hình chỗ nào.
        raise GoiHong(f"Không với tới Console ở {url}: {ex.reason}") from ex
    except Exception:
        os.unlink(tam)
        raise

    that = sha256_file(tam)
    if that != sha256_mong_doi.lower():
        os.unlink(tam)
        raise GoiHong("sha256 lệch — gói tải về hỏng. "
                      f"Chờ {sha256_mong_doi[:12]}, nhận {that[:12]}")
    return tam


def bung_goi_test_case(goi: str, thu_muc_autotests: str,
                       ten: str | None = None) -> dict:
    """Bung gói ZIP test case vào `AutoTests/<ten>/`, thay thế nếu đã có.

    Console là nguồn sự thật về nội dung thư mục này, nên bung đè là đúng. Chỉ
    đụng ĐÚNG thư mục đích — không bao giờ xoá các thư mục anh em, vì trong
    `AutoTests/` còn test case người ta để sẵn.
    """
    if not zipfile.is_zipfile(goi):
        # Đọc vài byte đầu để báo lỗi cho người chứ không chỉ nói "không hợp lệ".
        try:
            dau = open(goi, "rb").read(6)
        except OSError as ex:
            raise GoiHong(f"Không đọc được gói: {ex}") from ex
        if dau.startswith(b"7z\xbc\xaf\x27\x1c"):
            raise GoiHong("Gói là 7z, agent chỉ nhận ZIP. Console phải đổi "
                          "sang ZIP trước khi gửi.")
        raise GoiHong(f"Gói không phải ZIP (6 byte đầu: {dau.hex()})")

    ten = ten or os.path.splitext(os.path.basename(goi))[0]
    _duong_dan_an_toan(ten)                  # tên thư mục cũng phải sạch
    dich = os.path.join(thu_muc_autotests, ten)

    with zipfile.ZipFile(goi) as z:
        muc = [m for m in z.namelist() if not m.endswith("/")]
        if not muc:
            raise GoiHong("Gói rỗng, không có file nào")
        an_toan = {m: _duong_dan_an_toan(m) for m in muc}
        vo = _bo_vo_boc(list(an_toan.values()))

        # Kiểm tra xong TOÀN BỘ rồi mới ghi. Bung nửa chừng mới phát hiện mục
        # độc là đã kịp rải file ra đĩa.
        if os.path.isdir(dich):
            shutil.rmtree(dich)
        os.makedirs(dich, exist_ok=True)

        so_tc = 0
        for goc, sach in an_toan.items():
            if vo:
                sach = sach[len(vo) + 1:] or os.path.basename(sach)
            ra = os.path.join(dich, sach.replace("/", os.sep))
            os.makedirs(os.path.dirname(ra), exist_ok=True)
            with z.open(goc) as f_in, open(ra, "wb") as f_out:
                shutil.copyfileobj(f_in, f_out)
            if sach.lower().endswith((".tc", ".mtc")):
                so_tc += 1

    return {"thu_muc": dich, "so_file": len(an_toan), "so_test_case": so_tc,
            "da_bo_vo_boc": vo or None}


# ==========================================================================
# Đọc log Qauto
# ==========================================================================

# Dòng có mốc thời gian mới là dòng sự kiện. Các dòng "[Change]: ..." thụt đầu
# dòng và không có mốc thời gian — chúng là chi tiết của restbus simulation,
# nhiều vô kể, cố ý không khớp mẫu này.
RE_DONG = re.compile(
    r"^(?P<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s+"
    r"\[(?P<muc>\w+)\]\s+\[(?P<lop>\w+)\]\s+(?P<noi_dung>.*)$")

RE_BAT_DAU = re.compile(r'^Run Test Case:\s*"(?P<duong_dan>.+)"\s*$')
# Neo vào lớp RunningModel là bắt buộc: log còn có dòng
# "Enable Crc Can Message: PDCU_PA_Status = True" cũng chứa "Status =",
# bắt trần sẽ báo test xong trong khi nó chỉ đang bật CRC cho một bản tin.
RE_KET_THUC = re.compile(r"^Status\s*=\s*(?P<verdict>\S+)\s*$")
RE_BUOC = re.compile(r"^(?P<mo_dong>Start|End)\s+(?P<ten>.+?)\s*$")


def doi_verdict(tho: str) -> str:
    """Chỉ 'Pass' là chắc chắn.

    Trong toàn bộ log thu được tới 23/09 chưa có lượt nào trượt, nên chưa biết
    Qauto ghi chuỗi gì khi hỏng — 'Fail', 'Failed', hay 'NG'. Đoán bừa ở đây
    sẽ báo về Console một kết quả test sai, tệ hơn hẳn là nói 'không rõ'.
    """
    return "pass" if tho.strip().lower() == "pass" else "unknown"


@dataclass
class LuotChay:
    test_case: str
    bat_dau: float
    buoc: str | None = None


class DocLogQauto:
    """Theo đuôi log.txt, suy ra bench đang chạy test nào, bước nào, verdict gì.

    Định dạng Qauto: yyyy-MM-dd HH:mm:ss.fff [LEVEL] [Class] message
    Khác định dạng VDSA mà CLAUDE.md mô tả, nên nếu bench chạy VDSA thì phải
    viết thêm một lớp đọc nữa chứ không sửa lớp này.
    """

    # Log ghi nối tiếp nên vài chục byte đầu không bao giờ đổi. Đổi nghĩa là
    # Qauto đã thay file. Chỉ so kích thước thì sót trường hợp file mới dài
    # bằng đúng file cũ, hoặc mới đã vượt qua con trỏ cũ giữa hai lần đọc.
    DAI_VAN_TAY = 64

    def __init__(self, duong_dan: str):
        self.duong_dan = duong_dan
        self.vi_tri = 0
        self.du = ""
        self.van_tay = b""
        self.dang_chay: LuotChay | None = None
        self._dong_bo_vi_tri_dau()

    def _theo_file_moi(self) -> bool:
        """Chuyển sang file log mới nếu Qauto vừa xoay. Trả về True nếu có đổi.

        File mới bắt đầu từ rỗng nên đọc lại từ đầu là đúng và rẻ — khác hẳn
        trường hợp cùng một file bị ghi đè.
        """
        moi = log_dang_song(self.duong_dan)
        if moi == self.duong_dan:
            return False
        self.duong_dan, self.vi_tri, self.du = moi, 0, ""
        self.van_tay = self._doc_van_tay()
        return True

    def _doc_van_tay(self) -> bytes:
        try:
            with open(self.duong_dan, "rb") as f:
                return f.read(self.DAI_VAN_TAY)
        except OSError:
            return b""

    def _dong_bo_vi_tri_dau(self) -> None:
        """Nhảy tới cuối file lúc khởi động, nhưng vẫn đọc ngược để biết có
        lượt nào đang dở không.

        Nếu đọc từ đầu file, agent sẽ phát lại bốn lượt chạy của hôm trước
        thành bốn `result` mới và Console có thêm lịch sử giả. Bỏ qua hẳn thì
        lại không biết bench đang bận. Nên: xác định trạng thái từ đuôi file,
        còn con trỏ phát sự kiện thì đặt ở cuối.
        """
        self.duong_dan = log_dang_song(self.duong_dan)
        if not os.path.exists(self.duong_dan):
            return
        co = os.path.getsize(self.duong_dan)
        self.vi_tri = co
        self.van_tay = self._doc_van_tay()

        with open(self.duong_dan, "rb") as f:
            f.seek(max(0, co - 200_000))
            duoi = f.read().decode("utf-8", errors="replace").splitlines()

        for dong in reversed(duoi):
            m = RE_DONG.match(dong)
            if not m or m["lop"] != "RunningModel":
                continue
            if RE_KET_THUC.match(m["noi_dung"]):
                return                      # lượt gần nhất đã xong
            bd = RE_BAT_DAU.match(m["noi_dung"])
            if bd:
                # Có "Run Test Case" mà chưa thấy "Status =" sau nó → đang dở.
                self.dang_chay = LuotChay(
                    test_case=os.path.basename(bd["duong_dan"]),
                    bat_dau=time.time())
                return

    def doc_moi(self) -> list[dict]:
        """Đọc phần mới thêm vào file, trả về danh sách sự kiện."""
        su_kien: list[dict] = []
        self._theo_file_moi()
        if not os.path.exists(self.duong_dan):
            return su_kien

        co = os.path.getsize(self.duong_dan)
        van_tay = self._doc_van_tay()

        # File nhỏ đi, hoặc đầu file đổi, đều nghĩa là Qauto vừa thay log.
        # Giữ con trỏ cũ thì sẽ đọc vào giữa một dòng và parse ra rác.
        if co < self.vi_tri or (self.van_tay and van_tay != self.van_tay):
            self.vi_tri, self.du = 0, ""
        self.van_tay = van_tay

        if co == self.vi_tri:
            return su_kien

        with open(self.duong_dan, "rb") as f:
            f.seek(self.vi_tri)
            tho = f.read().decode("utf-8", errors="replace")
            self.vi_tri = f.tell()

        # Dòng cuối có thể bị cắt giữa chừng vì Qauto đang ghi dở — giữ lại
        # chờ lần đọc sau ghép vào, đừng parse một nửa.
        tho = self.du + tho
        *dong_du, self.du = tho.split("\n")

        for dong in dong_du:
            m = RE_DONG.match(dong.rstrip("\r"))
            if not m:
                continue
            lop, noi_dung = m["lop"], m["noi_dung"]

            if lop == "RunningModel":
                bd = RE_BAT_DAU.match(noi_dung)
                if bd:
                    self.dang_chay = LuotChay(
                        test_case=os.path.basename(bd["duong_dan"]),
                        bat_dau=time.time())
                    su_kien.append({"loai": "bat_dau",
                                    "test_case": self.dang_chay.test_case})
                    continue

                kt = RE_KET_THUC.match(noi_dung)
                if kt and self.dang_chay:
                    su_kien.append({
                        "loai": "ket_thuc",
                        "test_case": self.dang_chay.test_case,
                        "verdict": doi_verdict(kt["verdict"]),
                        "verdict_tho": kt["verdict"],
                        "duration_s": round(time.time() - self.dang_chay.bat_dau, 1),
                    })
                    self.dang_chay = None

            elif lop == "StepExecutor" and self.dang_chay:
                b = RE_BUOC.match(noi_dung)
                if b and b["mo_dong"] == "Start":
                    self.dang_chay.buoc = b["ten"]
                    su_kien.append({"loai": "buoc", "ten": b["ten"]})

        return su_kien


# ==========================================================================
# Suy trạng thái
# ==========================================================================

def mo_ta_qauto(q: dict | None) -> str:
    """Một mệnh đề ngắn về Qauto, đủ để người đọc biết nên tin tới đâu."""
    return {
        "tat": "Qauto chưa bật",
        "san_sang": "Qauto đang mở",
        # Nói thẳng là nó kẹt, đừng gọi là "đang mở" — người ở xa sẽ tưởng
        # bench dùng được rồi đặt lịch vào đó.
        "chua_san_sang": "Qauto có tiến trình nhưng chưa sẵn sàng",
        "khong_ro": "không rõ tình trạng Qauto",
    }.get((q or {}).get("danh_gia", "khong_ro"), "không rõ tình trạng Qauto")


def suy_trang_thai(cb: dict, dang_chay: "LuotChay | None") -> dict:
    """Gộp cảm biến + log thành đúng payload mà backend đang chờ.

    Hàm thuần, tách khỏi lớp BenchAgent để kiểm thử được mà không cần phần
    cứng — đây là chỗ dễ kết luận sai nhất.
    """
    if dang_chay:
        tt = {"state": "running", "test_case": dang_chay.test_case}
        if dang_chay.buoc:
            tt["step"] = dang_chay.buoc
        return tt

    kenh, usb = cb.get("kenh_can") or [], cb.get("adapter_usb") or []

    if not kenh and not usb:
        # Bench hỏng hai thứ cùng lúc là chuyện thường. Thẻ chỉ có một dòng,
        # nên ghép lại chứ đừng để người ở xa sửa xong adapter rồi mới phát
        # hiện Qauto cũng đang kẹt.
        detail = "Không thấy adapter CAN nào — kiểm tra dây USB"
        if (cb.get("qauto") or {}).get("danh_gia") != "san_sang":
            detail += "; " + mo_ta_qauto(cb.get("qauto"))
        return {"state": "error", "error": "can_adapter_missing",
                "detail": detail}

    if not kenh:
        # Có adapter nhưng không phải PEAK, nên PCANBasic không đọc được tình
        # trạng kênh. KHÔNG được báo "mất adapter" — dây vẫn cắm, báo thế là
        # đẩy người trực đi kiểm tra nhầm chỗ. Nói đúng cái mình biết và cả
        # cái mình không biết.
        a = usb[0]
        return {"state": "idle",
                "detail": f"Adapter {a['ten']} ({a['hang']}) — không đọc được "
                          f"kênh qua PCANBasic, {mo_ta_qauto(cb.get('qauto'))}"}

    if cb.get("can_bi_chiem"):
        # Bench không hỏng, nhưng cũng không nhận lệnh từ xa được: có người
        # đang ngồi thao tác tay. CLAUDE.md gọi đây là "đang dùng tại chỗ".
        return {"state": "idle",
                "detail": "Đang dùng tại chỗ — Qauto giữ kênh CAN"}

    # Phần cứng lành nhưng Qauto kẹt thì bench vẫn không chạy test được. Người
    # ở xa phải thấy điều đó, nếu không họ sẽ xếp lịch vào một con bench chết.
    if (cb.get("qauto") or {}).get("danh_gia") == "chua_san_sang":
        return {"state": "idle", "detail": mo_ta_qauto(cb["qauto"])}

    return {"state": "idle"}


# ==========================================================================
# Agent
# ==========================================================================

class BenchAgent:
    def __init__(self, args):
        self.id = args.id
        self.model = args.model
        self.prefix = f"bench/{ma_model(args.model)}/{args.id}"
        self.chu_ky = args.interval
        self.nhip_tim = args.heartbeat
        self.adb = args.adb
        self.log = DocLogQauto(args.qauto_log)
        self.autotests = getattr(args, "autotests", AUTOTESTS_MAC_DINH)
        self.ket_qua = getattr(args, "ket_qua", "tu-sinh")
        self.gia_lap_giay = getattr(args, "gia_lap_giay", 2.0)
        self.console = getattr(args, "console", None)
        # Tên bài đang chạy giả lập, để gói status báo "đang chạy" cho đúng.
        self.dang_gia_lap: str | None = None
        # Chỉ nhắc một lần chuyện thiếu --console, đừng rải kín màn hình.
        self._da_nhac_console = False

        self.trang_thai_cu: dict | None = None
        self.lan_gui_cuoi = 0.0
        self.dung = False

        self.client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2,
                                  client_id=f"agent-{args.id}",
                                  clean_session=True)
        self.client.on_connect = self._khi_noi
        self.client.on_message = self._khi_co_lenh

        # Last Will: broker tự phát cái này khi agent chết hoặc đứt mạng. Đây
        # chính là thứ tạo ra trạng thái "Mất kết nối" trên Console, không phải
        # việc backend đợi lâu không thấy dữ liệu.
        self.client.will_set(f"{self.prefix}/status",
                             json.dumps({"state": "offline", "ts": None,
                                         "host": TEN_MAY}),
                             qos=1, retain=True)

    # ---------------------------------------------------------- cảm biến

    def doc_cam_bien(self) -> dict:
        kenh = kenh_can()
        bi_chiem = any(k["tinh_trang"] in ("occupied", "pcanview") for k in kenh)
        quet = quet_may()
        return {
            "qauto": danh_gia_qauto(quet["ung_dung"], self.log.duong_dan),
            "vdsa": tien_trinh_dang_chay("VDSA.exe"),
            "kenh_can": kenh,
            "can_bi_chiem": bi_chiem,
            "adapter_usb": quet["usb"],
            "mhu": mhu_adb(self.adb),
        }

    # ---------------------------------------------------------- MQTT

    def _khi_noi(self, client, userdata, flags, rc, properties=None):
        hong = getattr(rc, "is_failure", None)
        if hong is None:
            hong = rc != 0
        if hong:
            print(f"[{self.id}] không kết nối được broker: {rc}")
            return
        # Nghe bằng ký tự đại diện ở chỗ model, KHÔNG nghe đúng prefix của mình.
        #
        # Lý do: model của bench đổi khi thay MHU (VF8 → VF8New ME), và Console
        # gửi lệnh vào topic dựng từ model MỚI. Nếu agent chỉ nghe prefix cũ thì
        # lệnh rơi vào khoảng không — mà chiều lên vẫn chạy bình thường (ingest
        # khớp theo mã bench, bỏ qua model), nên nhìn bề ngoài bench vẫn khoẻ.
        # Console sẽ báo "bench không phản hồi", sai nguyên nhân hoàn toàn.
        #
        # Mã bench là duy nhất nên nghe rộng ở chỗ model không lẫn sang bench khác.
        topic_nghe = f"bench/+/{self.id}/cmd"
        client.subscribe(topic_nghe, qos=1)
        print(f"[{self.id}] đã kết nối, nghe {topic_nghe}")
        self.lan_gui_cuoi = 0.0          # ép gửi status ngay sau khi nối lại

    def _khi_co_lenh(self, client, userdata, msg):
        try:
            lenh = json.loads(msg.payload.decode())
        except (json.JSONDecodeError, UnicodeDecodeError):
            print(f"[{self.id}] lệnh không phải JSON, bỏ qua")
            return

        cmd_id = lenh.get("cmd_id", "?")
        action = lenh.get("action", "")

        if action == "deploy_testcase":
            self._nhan_goi(cmd_id, lenh)
            return

        if action in ("start_test", "run_plan"):
            self._chay_test(cmd_id, lenh)
            return

        # Mọi lệnh còn lại đều dính câu hỏi #1 trong CLAUDE.md: chưa ra lệnh cho
        # Qauto chạy test mà không bấm tay được. Từ chối thẳng và nói rõ lý do,
        # chứ im lặng thì Console treo tới lúc hết hạn chờ ack rồi báo "bench
        # không phản hồi" — sai nguyên nhân hoàn toàn.
        self._gui("ack", {
            "cmd_id": cmd_id,
            "status": "rejected",
            "reason": f"Agent chưa làm được lệnh '{action}' — mới chỉ giám sát "
                      "và nhận gói test case, chưa điều khiển được Qauto/VDSA",
        })

    def _nhan_goi(self, cmd_id: str, lenh: dict) -> None:
        """Nhận lệnh đẩy gói test case xuống.

        Đây là lệnh DUY NHẤT agent làm được lúc này, vì nó chỉ động tới file —
        không cần điều khiển Qauto nên không vướng câu hỏi #1.

        Tải rồi bung mất vài giây tới vài chục giây. Làm ngay trong callback là
        chẹn luôn vòng lặp mạng của paho, quá keepalive thì broker cắt kết nối
        và Last Will bắn ra — Console sẽ báo bench mất kết nối giữa lúc nó đang
        làm việc bình thường. Nên đẩy sang luồng riêng.
        """
        goi = lenh.get("goi") or {}
        url, sha, ten = goi.get("url"), goi.get("sha256"), goi.get("ten")

        if not url or not sha or not ten:
            self._gui("ack", {
                "cmd_id": cmd_id, "status": "rejected",
                "reason": "Lệnh thiếu goi.url, goi.sha256 hoặc goi.ten",
            })
            return

        # Nhận trước rồi làm, để Console biết lệnh đã tới chứ không phải rơi mất.
        self._gui("ack", {"cmd_id": cmd_id, "status": "accepted"})
        threading.Thread(target=self._lam_goi, args=(cmd_id, url, sha, ten),
                         daemon=True).start()

    def _lam_goi(self, cmd_id: str, url: str, sha: str, ten: str) -> None:
        tam = None
        try:
            tam = tai_goi(url, sha)
            kq = bung_goi_test_case(tam, self.autotests, ten)
            print(f"[{self.id}] đã bung gói {ten}: {kq['so_test_case']} bài")
            self._gui("result", {
                "cmd_id": cmd_id,
                "action": "deploy_testcase",
                "verdict": "pass",
                "test_case": ten,
                "detail": kq,
            })
        except GoiHong as ex:
            # Hỏng ở đây thì nói đúng câu lỗi ra, đừng gộp thành "thất bại".
            # Người đọc ở xa không mở được máy bench để tự xem.
            print(f"[{self.id}] gói {ten} hỏng: {ex}")
            self._gui("result", {
                "cmd_id": cmd_id,
                "action": "deploy_testcase",
                "verdict": "fail",
                "test_case": ten,
                "reason": str(ex),
            })
        except Exception as ex:                       # noqa: BLE001
            print(f"[{self.id}] lỗi không lường khi nhận gói {ten}: {ex!r}")
            self._gui("result", {
                "cmd_id": cmd_id,
                "action": "deploy_testcase",
                "verdict": "fail",
                "test_case": ten,
                "reason": f"Lỗi không lường: {ex!r}",
            })
        finally:
            if tam and os.path.exists(tam):
                os.unlink(tam)

    def _chay_test(self, cmd_id: str, lenh: dict) -> None:
        """Nhận lệnh chạy test.

        Hiện agent CHƯA điều khiển được Qauto, nên đây là chạy giả lập: nó chờ
        một nhịp rồi nộp file kết quả lấy theo `--ket-qua`. Mục đích là chứng
        minh cả vòng đã khép kín — đẩy gói xuống, ra lệnh chạy, nhận báo cáo về
        — để khi Qauto làm được client thì chỉ còn thay đúng phần chạy.

        Verdict trả về là `unknown`, KHÔNG phải `pass`. Chạy giả lập mà báo pass
        là gieo kết quả giả vào lịch sử test, đúng kiểu hỏng im lặng mà dự án
        này đã dính nhiều lần.
        """
        test_case = lenh.get("test_case") or (lenh.get("plan") or {}).get("ten")

        # Địa chỉ nộp báo cáo do Console gắn sẵn vào lệnh, nên máy bench không
        # phải cấu hình thêm URL nào. `--console` chỉ là đường lui khi chạy tay.
        mau_url = lenh.get("report_url")
        if not mau_url and self.console:
            mau_url = self.console.rstrip("/") + "/api/runs/{cmd_id}/report"
        if not mau_url:
            self._gui("ack", {
                "cmd_id": cmd_id, "status": "rejected",
                "reason": "Lệnh không có report_url và agent chưa đặt --console, "
                          "không biết nộp kết quả về đâu",
            })
            return

        self._gui("ack", {"cmd_id": cmd_id, "status": "accepted"})
        threading.Thread(target=self._lam_chay_test,
                         args=(cmd_id, test_case, mau_url.replace("{cmd_id}", cmd_id)),
                         daemon=True).start()

    def _lam_chay_test(self, cmd_id: str, test_case: str | None, url: str) -> None:
        t0 = time.time()
        tam = []
        try:
            # Chờ một nhịp cho giống lượt chạy thật, để người xem trên Console
            # kịp thấy bench chuyển sang "đang chạy" rồi mới có kết quả.
            self.dang_gia_lap = test_case or "(không nêu)"
            self.lan_gui_cuoi = 0.0            # ép gửi status ngay
            time.sleep(self.gia_lap_giay)

            files = tim_file_ket_qua(self.ket_qua, test_case, cmd_id)
            # File tự sinh nằm ở thư mục tạm, dọn sau khi nộp xong.
            tam = [f for f in files if os.path.basename(f).startswith("ket-qua-gia-lap-")]

            ma = nop_bao_cao(url, files, test_case, self.id)
            print(f"[{self.id}] đã nộp {len(files)} file báo cáo (HTTP {ma})")

            self._gui("result", {
                "cmd_id": cmd_id,
                "test_case": test_case,
                # KHÔNG bịa pass. Chưa chạy test thật thì không biết kết quả.
                "verdict": "unknown",
                "duration_s": round(time.time() - t0, 2),
                "reason": "chạy giả lập — agent chưa điều khiển được Qauto",
                "detail": {"so_file_bao_cao": len(files),
                           "nguon_ket_qua": self.ket_qua},
            })
        except GoiHong as ex:
            print(f"[{self.id}] chạy {test_case} hỏng: {ex}")
            self._gui("result", {
                "cmd_id": cmd_id, "test_case": test_case,
                "verdict": "unknown",
                "duration_s": round(time.time() - t0, 2),
                "reason": str(ex),
            })
        except Exception as ex:                        # noqa: BLE001
            print(f"[{self.id}] lỗi không lường khi chạy {test_case}: {ex!r}")
            self._gui("result", {
                "cmd_id": cmd_id, "test_case": test_case,
                "verdict": "unknown",
                "duration_s": round(time.time() - t0, 2),
                "reason": f"Lỗi không lường: {ex!r}",
            })
        finally:
            self.dang_gia_lap = None
            self.lan_gui_cuoi = 0.0
            for f in tam:
                try:
                    os.unlink(f)
                except OSError:
                    pass

    def _tu_dong_nop(self, cmd_id: str, test_case: str | None) -> None:
        """Có kết quả là đẩy file lên ngay, không chờ ai ra lệnh.

        Đường nộp báo cáo lúc nhận lệnh thì lấy `report_url` ngay trong lệnh.
        Còn lượt chạy do người ngồi tại bench tự bấm thì không có lệnh nào cả,
        nên phải biết trước địa chỉ Console — đó là việc của `--console`.

        Thiếu `--console` thì vẫn gửi tóm tắt qua MQTT như cũ, chỉ mất phần
        file. Nói một lần rồi thôi, đừng để nó rải kín màn hình mỗi lượt chạy.
        """
        if not self.console:
            if not self._da_nhac_console:
                print(f"[{self.id}] có kết quả nhưng chưa đặt --console, "
                      "chỉ gửi tóm tắt, không đẩy được file")
                self._da_nhac_console = True
            return

        url = self.console.rstrip("/") + f"/api/runs/{cmd_id}/report"
        threading.Thread(target=self._lam_tu_dong_nop,
                         args=(cmd_id, test_case, url), daemon=True).start()

    def _lam_tu_dong_nop(self, cmd_id: str, test_case: str | None, url: str) -> None:
        tam = []
        try:
            files = tim_file_ket_qua(self.ket_qua, test_case, cmd_id)
            tam = [f for f in files if os.path.basename(f).startswith("ket-qua-gia-lap-")]
            ma = nop_bao_cao(url, files, test_case, self.id)
            print(f"[{self.id}] tự đẩy {len(files)} file của {test_case} (HTTP {ma})")
        except GoiHong as ex:
            # Không đẩy được file thì tóm tắt vẫn đã lên rồi, không mất kết quả.
            # Nhưng phải nói ra, đừng nuốt — im lặng ở đây nghĩa là bằng chứng
            # biến mất mà không ai biết.
            print(f"[{self.id}] không đẩy được file của {test_case}: {ex}")
        except Exception as ex:                        # noqa: BLE001
            print(f"[{self.id}] lỗi không lường khi tự đẩy file: {ex!r}")
        finally:
            for f in tam:
                try:
                    os.unlink(f)
                except OSError:
                    pass

    def _gui(self, leaf: str, payload: dict, retain: bool = False) -> None:
        payload.setdefault("ts", bay_gio())
        self.client.publish(f"{self.prefix}/{leaf}",
                            json.dumps(payload, ensure_ascii=False),
                            qos=1, retain=retain)

    # ---------------------------------------------------------- vòng đời

    def mot_vong(self) -> dict:
        for sk in self.log.doc_moi():
            if sk["loai"] == "ket_thuc":
                # Lượt này KHÔNG do Console ra lệnh — người ngồi tại bench tự
                # bấm chạy. Tự sinh một cmd_id để gói tóm tắt và đám file đẩy
                # lên sau còn ghép được với nhau.
                cmd_id = "tu-dong-" + os.urandom(6).hex()
                self._gui("result", {
                    "cmd_id": cmd_id,
                    "test_case": sk["test_case"],
                    "verdict": sk["verdict"],
                    "duration_s": sk["duration_s"],
                    # Giữ nguyên chuỗi Qauto ghi, để sau này đối chiếu được khi
                    # gặp lượt trượt đầu tiên mà chưa biết nó ghi chữ gì.
                    "detail": {"qauto_status": sk["verdict_tho"]},
                })
                # Có kết quả là đẩy file lên ngay, không chờ ai bấm gì.
                self._tu_dong_nop(cmd_id, sk["test_case"])

        tt = suy_trang_thai(self.doc_cam_bien(), self.log.dang_chay)

        # Lượt chạy giả lập đè lên trạng thái đọc từ cảm biến. Không có chỗ này
        # thì Console vẫn hiện "Sẵn sàng" suốt lúc agent đang chạy lệnh, và
        # người ở xa không thấy lệnh mình vừa bấm có tác dụng gì.
        if self.dang_gia_lap:
            tt = {"state": "running", "test_case": self.dang_gia_lap,
                  "detail": "Đang chạy giả lập — chưa điều khiển Qauto thật"}

        # Chỉ gửi khi trạng thái đổi, hoặc tới nhịp tim. Bench đứng yên hàng
        # giờ mà cứ 5 giây một gói retained là làm broker và DB bẩn vô ích.
        gio = time.time()
        if tt != self.trang_thai_cu or gio - self.lan_gui_cuoi >= self.nhip_tim:
            self._gui("status", dict(tt, host=TEN_MAY), retain=True)
            self.trang_thai_cu = tt
            self.lan_gui_cuoi = gio
        return tt

    def chay(self, host: str, port: int) -> None:
        # Agent chạy không người trực trên máy bench, nên lần nối đầu KHÔNG
        # được phép làm chết tiến trình: máy bench thường khởi động trước máy
        # A, hoặc mạng xưởng lên chậm. Cứ thử lại cho tới khi được — paho tự
        # lo việc nối lại sau khi đã nối thành công một lần.
        cho = 5
        while not self.dung:
            try:
                self.client.connect(host, port, keepalive=30)
                break
            except OSError as ex:
                print(f"[{self.id}] chưa nối được {host}:{port} ({ex}), "
                      f"thử lại sau {cho:.0f}s")
                time.sleep(cho)
                cho = min(cho * 2, 60)      # giãn dần để khỏi quay broker
        if self.dung:
            return

        self.client.loop_start()
        try:
            while not self.dung:
                self.mot_vong()
                time.sleep(self.chu_ky)
        finally:
            # Báo offline tử tế khi tắt có chủ đích, đừng để Console phải đợi
            # Last Will như lúc mất điện.
            self._gui("status", {"state": "offline", "host": TEN_MAY}, retain=True)
            time.sleep(0.5)
            self.client.loop_stop()
            self.client.disconnect()


def main():
    ap = argparse.ArgumentParser(description="Agent Bench Console trên máy bench")
    ap.add_argument("--host", default="127.0.0.1", help="IP broker MQTT (máy A)")
    ap.add_argument("--port", type=int, default=1883)
    ap.add_argument("--id", default="QAUTO-01", help="Mã bench, phải khớp Console")
    ap.add_argument("--model", default="vf6")
    ap.add_argument("--qauto-log", default=LOG_QAUTO_MAC_DINH)
    ap.add_argument("--adb", default=ADB_QAUTO_MAC_DINH)
    ap.add_argument("--autotests", default=AUTOTESTS_MAC_DINH,
                    help="thư mục AutoTests của Qauto, nơi bung gói test case")
    ap.add_argument("--ket-qua", default="tu-sinh", dest="ket_qua",
                    help="nguồn file kết quả: tu-sinh | qauto | qauto:<đường> | mau:<đường>")
    ap.add_argument("--gia-lap-giay", type=float, default=2.0, dest="gia_lap_giay",
                    help="số giây giả vờ chạy trước khi nộp kết quả")
    ap.add_argument("--console", default=None,
                    help="địa chỉ Console, chỉ cần khi lệnh không mang report_url")
    ap.add_argument("--interval", type=float, default=5.0,
                    help="giây giữa hai lần đọc cảm biến")
    ap.add_argument("--heartbeat", type=float, default=30.0,
                    help="giây tối đa giữa hai gói status dù không đổi")
    ap.add_argument("--once", action="store_true",
                    help="đọc cảm biến một lần rồi in ra, không cần broker")
    args = ap.parse_args()

    agent = BenchAgent(args)

    if args.once:
        cb = agent.doc_cam_bien()
        print(json.dumps({
            "topic_prefix": agent.prefix,
            "status_se_gui": dict(suy_trang_thai(cb, agent.log.dang_chay), host=TEN_MAY),
            "cam_bien": cb,
            "log_qauto": {
                "duong_dan": agent.log.duong_dan,
                "ton_tai": os.path.exists(agent.log.duong_dan),
                "dang_chay": agent.log.dang_chay.test_case if agent.log.dang_chay else None,
            },
        }, ensure_ascii=False, indent=2))
        return

    def tat(signum, frame):
        agent.dung = True

    signal.signal(signal.SIGINT, tat)
    signal.signal(signal.SIGTERM, tat)

    print(f"Agent {args.id} → {args.host}:{args.port}, topic {agent.prefix}")
    print("Ctrl+C để dừng.\n")
    agent.chay(args.host, args.port)


if __name__ == "__main__":
    main()
