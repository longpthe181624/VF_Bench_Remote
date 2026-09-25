#!/usr/bin/env python3
"""
Kiểm thử bộ đọc log Qauto của `bench_agent.py`.

Không dùng framework test nào, giống BenchConsole.Core.SmokeTest — chạy được
mà không cần cài thêm gì, không cần broker, không cần database.

Dữ liệu là log THẬT do Qauto ghi ra trên máy bench. Nếu không tìm thấy file
thật thì dùng bản rút gọn nhúng sẵn bên dưới, và nói rõ là đang chạy bằng
mẫu chứ không phải log thật.

    PYTHONIOENCODING=utf-8 python bench_agent_test.py
"""

import json
import os
import sys
import tempfile
import time
import types
import zipfile

from bench_agent import (BenchAgent, DocLogQauto, GoiHong, LuotChay,
                         bung_goi_test_case, danh_gia_qauto, doi_verdict,
                         ma_model, mo_ta_qauto, sha256_file, suy_trang_thai,
                         tai_goi)

LOG_THAT = r"D:\Qauto_2610\Qauto_2610\Logs\log.txt"

# Rút gọn từ log thật: một lượt chạy trọn vẹn, kèm đúng những dòng đã từng
# suýt làm parser hiểu sai.
MAU = """2026-09-22 10:50:32.168 [DEBUG] [RunningModel] Run Test Case: "D:\\Qauto\\AutoTests\\Disable\\Disable_VF6_7_v2.tc"
2026-09-22 10:50:32.302 [DEBUG] [StepExecutor] Start CAN
                  Add Can Message: [Info_CAN]AVAS_STATUS
                  [Change]: AVAS_CANBusOff = 0
                  Enable Crc Can Message: PDCU_PA_Status = True
                  Enable Alive Can Message: PDCU_PA_Status = True
2026-09-22 10:50:32.350 [DEBUG] [StepExecutor] End CAN
2026-09-22 10:50:33.376 [DEBUG] [RunningModel] Status = Pass
"""

loi = []
so_phep = 0


def Check(dieu_kien, mo_ta):
    global so_phep
    so_phep += 1
    if not dieu_kien:
        loi.append(mo_ta)


def Section(ten):
    print(f"\n── {ten}")


def doc_het(noi_dung: str, theo_khuc: int = 0) -> list[dict]:
    """Ghi nội dung vào file tạm rồi cho DocLogQauto đọc.

    theo_khuc > 0 thì ghi thành nhiều lần, để kiểm tra việc ghép dòng bị cắt
    giữa chừng — đúng tình huống agent đọc trúng lúc Qauto đang ghi dở.
    """
    d = tempfile.mkdtemp()
    p = os.path.join(d, "log.txt")
    open(p, "w", encoding="utf-8").close()

    doc = DocLogQauto(p)                 # file rỗng → con trỏ ở 0
    su_kien = []

    if theo_khuc <= 0:
        with open(p, "a", encoding="utf-8") as f:
            f.write(noi_dung)
        su_kien += doc.doc_moi()
    else:
        for i in range(0, len(noi_dung), theo_khuc):
            with open(p, "a", encoding="utf-8") as f:
                f.write(noi_dung[i:i + theo_khuc])
            su_kien += doc.doc_moi()

    return su_kien, doc, p


# ------------------------------------------------------------------ verdict
Section("Đổi verdict")

Check(doi_verdict("Pass") == "pass", "Pass phải ra pass")
Check(doi_verdict("  pass  ") == "pass", "khoảng trắng thừa không được làm lệch")
Check(doi_verdict("Fail") == "unknown",
      "chưa từng thấy lượt trượt thật nên Fail phải ra unknown, không được đoán")
Check(doi_verdict("") == "unknown", "chuỗi rỗng phải ra unknown")

# ------------------------------------------------------------------ mẫu
Section("Đọc một lượt chạy")

sk, _, _ = doc_het(MAU)
bat_dau = [s for s in sk if s["loai"] == "bat_dau"]
ket_thuc = [s for s in sk if s["loai"] == "ket_thuc"]
buoc = [s for s in sk if s["loai"] == "buoc"]

Check(len(bat_dau) == 1, "phải thấy đúng một lần bắt đầu")
Check(bat_dau and bat_dau[0]["test_case"] == "Disable_VF6_7_v2.tc",
      "phải lấy tên file test case, không phải cả đường dẫn")
Check(len(buoc) == 1 and buoc[0]["ten"] == "CAN",
      "phải bắt được bước Start CAN, và chỉ Start chứ không nhân đôi với End")
Check(len(ket_thuc) == 1, "phải thấy đúng một lần kết thúc")
Check(ket_thuc and ket_thuc[0]["verdict"] == "pass", "verdict phải là pass")
Check(ket_thuc and ket_thuc[0]["verdict_tho"] == "Pass",
      "phải giữ nguyên chuỗi gốc Qauto ghi để sau này đối chiếu")

# Đây là cái bẫy thật đã gặp khi khảo sát log ngày 23/09.
Section("Không được nhầm dòng CRC thành kết thúc test")

BAY = """2026-09-22 10:50:32.168 [DEBUG] [RunningModel] Run Test Case: "D:\\a\\B.tc"
                  Enable Crc Can Message: PDCU_PA_Status = True
                  Enable Alive Can Message: PDCU_PA_Status = True
"""
sk2, doc2, _ = doc_het(BAY)
Check(not [s for s in sk2 if s["loai"] == "ket_thuc"],
      "'PDCU_PA_Status = True' KHÔNG được hiểu thành test đã xong")
Check(doc2.dang_chay is not None,
      "sau dòng CRC, lượt chạy vẫn phải còn đang dở")

# ------------------------------------------------------------------ ghi dở
Section("Đọc trúng lúc Qauto đang ghi dở dòng")

sk3, _, _ = doc_het(MAU, theo_khuc=37)   # cắt vụn, gần như dòng nào cũng đứt
Check(len([s for s in sk3 if s["loai"] == "bat_dau"]) == 1,
      "ghi thành nhiều khúc vẫn phải ra đúng một lần bắt đầu")
Check(len([s for s in sk3 if s["loai"] == "ket_thuc"]) == 1,
      "ghi thành nhiều khúc vẫn phải ra đúng một lần kết thúc")

# ------------------------------------------------------------------ xoay log
Section("Qauto thay log giữa chừng")

# Trường hợp dễ: file bị cắt cụt, kích thước nhỏ đi.
sk4, doc4, p4 = doc_het(MAU)
with open(p4, "w", encoding="utf-8") as f:
    f.write('2026-09-23 08:00:00.000 [DEBUG] [RunningModel] Run Test Case: "D:\\a\\C.tc"\n')
Check(doc4.doc_moi() and doc4.dang_chay
      and doc4.dang_chay.test_case == "C.tc",
      "file nhỏ đi thì phải đọc lại từ đầu, không parse vào giữa dòng")

# Trường hợp khó và mới là trường hợp nguy hiểm: phiên Qauto mới ghi đè, và
# giữa hai lần agent đọc nó đã dài vượt qua con trỏ cũ. Kích thước không bao
# giờ nhỏ đi nên chỉ so kích thước là sót — phải nhận ra qua đầu file.
MAU_MOI = MAU.replace("2026-09-22 10:50", "2026-09-23 08:15") + MAU
sk6, doc6, p6 = doc_het(MAU)
with open(p6, "w", encoding="utf-8") as f:
    f.write(MAU_MOI)
sk7 = doc6.doc_moi()
Check(len([s for s in sk7 if s["loai"] == "ket_thuc"]) == 2,
      "log bị thay bằng bản dài hơn vẫn phải nhận ra, không đọc trượt vào giữa")

# ------------------------------------------------------------------ trạng thái
Section("Mã model dùng trong topic")

# Phải ra KẾT QUẢ Y HỆT MaModel.Ma() bên C#, nếu không cùng một bench sẽ hiện
# hai kiểu topic trên broker.
for ten, mong_doi in [("VF8", "vf8"), ("VF9VN", "vf9vn"),
                      ("VF8New VN", "vf8new-vn"), ("VF8New ME", "vf8new-me"),
                      ("  VF8New   ME  ", "vf8new-me"), ("VF8_New.ME", "vf8-new-me"),
                      ("", ""), (None, ""), ("!!!", "")]:
    Check(ma_model(ten) == mong_doi, f"ma_model({ten!r}) phải ra {mong_doi!r}")

Section("Suy trạng thái từ cảm biến")

PEAK = [{"handle": "0x051", "ten": "PCAN-USB Pro FD", "controller": 0,
         "tinh_trang": "available"}]
PEAK_BAN = [dict(PEAK[0], tinh_trang="occupied")]
VCAN = [{"ten": "VCAN_D1266", "hang": "NXP",
         "tinh_trang_windows": "OK", "id": "USB\\VID_1FC9&PID_009C\\1266"}]


def cb(kenh=None, usb=None, qauto="tat"):
    return {"kenh_can": kenh or [], "adapter_usb": usb or [],
            "qauto": {"tien_trinh": qauto != "tat", "danh_gia": qauto},
            "can_bi_chiem": any(k["tinh_trang"] in ("occupied", "pcanview")
                                for k in (kenh or []))}


t = suy_trang_thai(cb(), LuotChay("A.tc", 0, buoc="CAN"))
Check(t["state"] == "running" and t["test_case"] == "A.tc" and t["step"] == "CAN",
      "đang chạy test thì phải ra running kèm test case và bước")

t = suy_trang_thai(cb(kenh=PEAK), LuotChay("A.tc", 0))
Check(t["state"] == "running" and "step" not in t,
      "chưa biết bước thì không được bịa ra trường step")

t = suy_trang_thai(cb(), None)
Check(t["state"] == "error" and t["error"] == "can_adapter_missing",
      "không có adapter nào thì mới được báo lỗi mất adapter")
Check("chưa bật" in t.get("detail", ""),
      "mất adapter mà Qauto cũng tắt thì phải nói cả hai, thẻ chỉ có một dòng")

t = suy_trang_thai(cb(qauto="san_sang"), None)
Check(";" not in t.get("detail", ""),
      "Qauto vẫn ổn thì đừng thêm mệnh đề thừa vào dòng lỗi")

# Đây là lỗi thật đã gặp 23/09: cắm adapter VCAN (NXP, WinUSB) thay cho PEAK,
# agent báo "không thấy adapter CAN" trong khi dây vẫn cắm tốt.
t = suy_trang_thai(cb(usb=VCAN, qauto="san_sang"), None)
Check(t["state"] == "idle",
      "có adapter không phải PEAK thì KHÔNG được báo lỗi mất adapter")
Check("VCAN_D1266" in t.get("detail", ""),
      "phải nói rõ đang thấy adapter nào, đừng để người trực đi mò")
Check("PCANBasic" in t.get("detail", ""),
      "phải nói rõ vì sao không đọc được kênh, chứ không im lặng bỏ qua")

t = suy_trang_thai(cb(kenh=PEAK_BAN), None)
Check(t["state"] == "idle" and "tại chỗ" in t.get("detail", ""),
      "kênh PEAK bị chiếm thì phải báo đang dùng tại chỗ")

t = suy_trang_thai(cb(kenh=PEAK), None)
Check(t["state"] == "idle" and "detail" not in t,
      "bench rảnh thật thì không kèm mô tả gì")

t = suy_trang_thai(cb(usb=VCAN), LuotChay("A.tc", 0))
Check(t["state"] == "running",
      "đang chạy test thì ưu tiên hơn mọi tình trạng phần cứng")

# Lỗi thật gặp 23/09: Qauto.exe có tiến trình, Responding=True, ngốn 486 giây
# CPU — nhưng cửa sổ không tiêu đề và không ghi log từ lúc khởi động. Agent
# bản cũ hỏi tasklist rồi báo "Qauto đang mở", người ở xa tưởng bench dùng được.
Section("Có tiến trình không có nghĩa là ứng dụng dùng được")

Check(danh_gia_qauto([], "khong-co-file.txt")["danh_gia"] == "tat",
      "không có tiến trình Qauto thì phải là tắt")

ket = danh_gia_qauto(
    [{"Name": "Qauto", "Id": 1, "Title": "", "Start": "2999-01-01T00:00:00+07:00"}],
    __file__)   # file này chắc chắn cũ hơn mốc khởi động giả ở trên
Check(ket["danh_gia"] == "chua_san_sang",
      "tiến trình sống mà cửa sổ không tiêu đề và log không ghi → chưa sẵn sàng")

ket = danh_gia_qauto(
    [{"Name": "Qauto", "Id": 1, "Title": "Qauto 2610", "Start": "2999-01-01T00:00:00+07:00"}],
    __file__)
Check(ket["danh_gia"] == "san_sang",
      "có tiêu đề cửa sổ thì coi là sẵn sàng dù log chưa ghi")

Check("chưa sẵn sàng" in mo_ta_qauto({"danh_gia": "chua_san_sang"}),
      "mô tả phải nói thẳng là chưa sẵn sàng, không được gọi là 'đang mở'")

t = suy_trang_thai(cb(kenh=PEAK, qauto="chua_san_sang"), None)
Check(t["state"] == "idle" and "chưa sẵn sàng" in t.get("detail", ""),
      "phần cứng lành mà Qauto kẹt thì vẫn phải báo cho người ở xa biết")

# --------------------------------------------------------------- bung gói
Section("Bung gói test case vào AutoTests")


def nen(cac_muc: dict, ten="goi.zip") -> str:
    d = tempfile.mkdtemp()
    p = os.path.join(d, ten)
    with zipfile.ZipFile(p, "w") as z:
        for k, v in cac_muc.items():
            z.writestr(k, v)
    return p


at = tempfile.mkdtemp()
kq = bung_goi_test_case(nen({"A/x.tc": "1", "A/y.tc": "2", "A/doc.txt": "3"}),
                        at, "GoiA")
Check(kq["so_file"] == 3 and kq["so_test_case"] == 2,
      "phải đếm đúng số file và riêng số test case")
Check(kq["da_bo_vo_boc"] == "A",
      "gói bọc trong một thư mục gốc thì phải bỏ lớp bọc, tránh lồng hai lần")
Check(os.path.isfile(os.path.join(at, "GoiA", "x.tc")),
      "file phải nằm thẳng trong AutoTests/GoiA/, không thêm tầng")

kq = bung_goi_test_case(nen({"a/x.tc": "1", "b/y.tc": "2"}), at, "GoiB")
Check(kq["da_bo_vo_boc"] is None and os.path.isfile(os.path.join(at, "GoiB", "a", "x.tc")),
      "gói có nhiều thư mục gốc thì giữ nguyên cấu trúc")

# Bung đè: Console là nguồn sự thật cho thư mục nó quản.
bung_goi_test_case(nen({"A/moi.tc": "1"}), at, "GoiA")
Check(os.path.isfile(os.path.join(at, "GoiA", "moi.tc"))
      and not os.path.exists(os.path.join(at, "GoiA", "x.tc")),
      "bung lại phải thay sạch nội dung cũ của đúng thư mục đó")
Check(os.path.isdir(os.path.join(at, "GoiB")),
      "nhưng TUYỆT ĐỐI không đụng thư mục anh em")

Section("Gói độc và gói hỏng phải bị chặn")

for xau, mo_ta in [
    ({"../../thoat.tc": "x"}, "đường dẫn thoát ra ngoài bằng .."),
    ({"..\\..\\thoat.tc": "x"}, "đường dẫn thoát dùng dấu gạch ngược"),
    ({"/tuyet_doi.tc": "x"}, "đường dẫn tuyệt đối"),
    ({"C:/Windows/x.tc": "x"}, "đường dẫn có ổ đĩa"),
]:
    try:
        bung_goi_test_case(nen(xau), tempfile.mkdtemp(), "Xau")
        Check(False, f"phải chặn {mo_ta}")
    except GoiHong:
        Check(True, f"phải chặn {mo_ta}")

try:
    bung_goi_test_case(nen({}), tempfile.mkdtemp(), "Rong")
    Check(False, "gói rỗng phải báo lỗi")
except GoiHong:
    Check(True, "gói rỗng phải báo lỗi")

d7 = tempfile.mkdtemp(); p7 = os.path.join(d7, "goi.zip")
open(p7, "wb").write(b"7z\xbc\xaf\x27\x1c\x00\x04" + b"\x00" * 40)
try:
    bung_goi_test_case(p7, tempfile.mkdtemp(), "Bay7z")
    Check(False, "gói 7z phải bị từ chối")
except GoiHong as e:
    Check("7z" in str(e), "từ chối gói 7z phải nói rõ là 7z, không nói chung chung")

# ---------------------------------------------------- gói dựng từ dữ liệu thật
Section("Bung gói dựng từ test case thật trên máy bench")

THAT = r"D:\Qauto_2610\Qauto_2610\AutoTests\Disable"
if os.path.isdir(THAT):
    dz = tempfile.mkdtemp(); pz = os.path.join(dz, "Disable.zip")
    with zipfile.ZipFile(pz, "w", zipfile.ZIP_DEFLATED) as z:
        for t in sorted(os.listdir(THAT)):
            if os.path.isfile(os.path.join(THAT, t)):
                z.write(os.path.join(THAT, t), f"Disable/{t}")
    at2 = tempfile.mkdtemp()
    kq = bung_goi_test_case(pz, at2, "Disable")
    print(f"   {kq['so_file']} file, {kq['so_test_case']} test case, "
          f"bỏ lớp bọc {kq['da_bo_vo_boc']!r}")
    Check(kq["so_test_case"] >= 10, "gói thật phải ra ít nhất 10 test case")
    Check(all(zipfile.is_zipfile(os.path.join(kq["thu_muc"], t))
              or t.lower().endswith(".mtc")
              for t in os.listdir(kq["thu_muc"]) if t.lower().endswith(".tc")),
          "file .tc bung ra phải còn nguyên vẹn, mở lại được")
else:
    print(f"   BỎ QUA — không thấy {THAT} trên máy này.")

# ------------------------------------------------------------- dựng agent
Section("Dựng agent không làm hỏng chính nó")

# Bài học 23/09: từng gán một thuộc tính trùng tên với phương thức `chay()`
# chạy vòng lặp, làm agent chết ngay khi chạy thật. Đường `--once` thoát trước
# khi gọi `chay()` nên toàn bộ test vẫn xanh. Giữ phép này để không tái diễn.
gia_args = types.SimpleNamespace(
    id="TEST-01", model="vf6", interval=5.0, heartbeat=20.0,
    adb=os.path.join(tempfile.mkdtemp(), "adb.exe"),
    qauto_log=os.path.join(tempfile.mkdtemp(), "log.txt"))
ag = BenchAgent(gia_args)

Check(callable(getattr(ag, "chay", None)),
      "agent.chay phải là phương thức chạy vòng lặp, không được bị thuộc tính đè")
Check(ag.prefix == "bench/vf6/TEST-01",
      "topic prefix phải dựng từ model và mã bench")

# ------------------------------------------------------------------ log thật
Section("Log thật do Qauto ghi trên máy bench")

if os.path.exists(LOG_THAT):
    with open(LOG_THAT, "r", encoding="utf-8", errors="replace") as f:
        that = f.read()
    sk6, _, _ = doc_het(that)
    bd6 = [s for s in sk6 if s["loai"] == "bat_dau"]
    kt6 = [s for s in sk6 if s["loai"] == "ket_thuc"]

    print(f"   {len(bd6)} lượt bắt đầu, {len(kt6)} lượt kết thúc")
    for s in kt6:
        print(f"   {s['test_case']} → {s['verdict']} (Qauto ghi: {s['verdict_tho']})")

    Check(len(bd6) > 0, "log thật phải có ít nhất một lượt chạy")
    Check(len(bd6) == len(kt6),
          "số lượt bắt đầu và kết thúc phải khớp nhau")
    Check(all(s["verdict"] in ("pass", "unknown") for s in kt6),
          "verdict chỉ được là pass hoặc unknown, không bịa ra fail")
else:
    print(f"   BỎ QUA — không thấy {LOG_THAT} trên máy này.")
    print("   Các phép trên chạy bằng mẫu rút gọn, chưa đối chiếu log thật.")

# ------------------------------------------------- tải gói test case từ Console
#
# Dựng HTTP server thật trong tiến trình test chứ không giả lập urlopen: chỗ
# hay hỏng nhất của phần này là tầng mạng — 404, tải dở, sai địa chỉ — mà giả
# lập thì không bao giờ tái hiện được.
print()
print("── Tải gói test case qua REST")

import functools
import hashlib
import http.server
import socketserver
import threading

kho_tam = tempfile.mkdtemp(prefix="kho-goi-")

goi_that = os.path.join(kho_tam, "tot.zip")
with zipfile.ZipFile(goi_that, "w") as z:
    z.writestr("Warning_VF8/bai1.tc", "noi dung bai 1")
    z.writestr("Warning_VF8/bai2.tc", "noi dung bai 2")
sha_that = hashlib.sha256(open(goi_that, "rb").read()).hexdigest()

Check(sha256_file(goi_that) == sha_that,
      "sha256_file phải cho cùng kết quả với băm một phát cả file")

phuc_vu = functools.partial(http.server.SimpleHTTPRequestHandler, directory=kho_tam)
phuc_vu.log_message = lambda *a, **k: None          # im lặng, khỏi bẩn output test
may_chu = socketserver.TCPServer(("127.0.0.1", 0), phuc_vu)
cong = may_chu.server_address[1]
threading.Thread(target=may_chu.serve_forever, daemon=True).start()

goc = f"http://127.0.0.1:{cong}"
try:
    tai_ve = tai_goi(f"{goc}/tot.zip", sha_that)
    Check(os.path.exists(tai_ve), "tải gói hợp lệ phải ra một file có thật")
    Check(sha256_file(tai_ve) == sha_that, "file tải về phải nguyên vẹn")

    kq_tai = bung_goi_test_case(tai_ve, kho_tam, "Warning_VF8")
    Check(kq_tai["so_test_case"] == 2,
          "bung gói tải về phải đếm đúng 2 bài")
    os.unlink(tai_ve)

    # sha lệch = gói hỏng hoặc tải dở. Phải chặn TRƯỚC khi bung, vì rải file
    # hỏng vào AutoTests/ thì Qauto vẫn chạy và vẫn báo Pass.
    try:
        tai_goi(f"{goc}/tot.zip", "b" * 64)
        Check(False, "sha256 lệch phải bị từ chối")
    except GoiHong as ex:
        Check("sha256" in str(ex).lower(), "báo lỗi sha lệch phải nói rõ là sha256")

    try:
        tai_goi(f"{goc}/khong-co.zip", sha_that)
        Check(False, "tải file không tồn tại phải ném GoiHong")
    except GoiHong as ex:
        Check("404" in str(ex), "lỗi 404 phải nói rõ mã HTTP")

    try:
        tai_goi(f"{goc}/tot.zip", "abc")
        Check(False, "sha256 sai độ dài phải bị chặn ngay, không cần tải")
    except GoiHong:
        Check(True, "sha256 sai độ dài bị chặn")

    # Cổng đóng: đúng tình huống máy A bind localhost nên máy bench không với tới.
    try:
        tai_goi("http://127.0.0.1:1/tot.zip", sha_that)
        Check(False, "không với tới Console phải ném GoiHong")
    except GoiHong as ex:
        Check("với tới" in str(ex), "lỗi mạng phải nói rõ là không với tới Console")
finally:
    may_chu.shutdown()
    may_chu.server_close()

# ------------------------- agent nhận lệnh đẩy gói, đi trọn đường không cần broker
#
# Phép quan trọng nhất của đợt này: nó chạy ĐÚNG đường mà lệnh thật đi —
# `_khi_co_lenh` → `_nhan_goi` → tải qua HTTP → kiểm sha → bung vào AutoTests/
# → publish result. Trước đó `bung_goi_test_case` có test nhưng KHÔNG ai gọi,
# nên phần nối dây chưa từng được kiểm.
print()
print("── Agent nhận lệnh deploy_testcase (không cần broker)")

autotests_gia = tempfile.mkdtemp(prefix="autotests-")
args_goi = types.SimpleNamespace(
    id="TEST-02", model="VF8New ME", interval=5.0, heartbeat=20.0,
    adb=os.path.join(tempfile.mkdtemp(), "adb.exe"),
    qauto_log=os.path.join(tempfile.mkdtemp(), "log.txt"),
    autotests=autotests_gia)
ag2 = BenchAgent(args_goi)

Check(ag2.prefix == "bench/vf8new-me/TEST-02",
      "tên model có khoảng trắng phải thành mã gạch nối trong topic")

# Thay client bằng cái ghi lại, để bắt đúng những gói agent định gửi đi.
da_gui = []
ag2.client = types.SimpleNamespace(
    publish=lambda topic, payload, qos=0, retain=False:
        da_gui.append((topic, json.loads(payload))))

may_chu2 = socketserver.TCPServer(("127.0.0.1", 0), phuc_vu)
cong2 = may_chu2.server_address[1]
threading.Thread(target=may_chu2.serve_forever, daemon=True).start()
try:
    lenh = types.SimpleNamespace(payload=json.dumps({
        "cmd_id": "abc123",
        "action": "deploy_testcase",
        "goi": {
            "ten": "Warning_VF8",
            "url": f"http://127.0.0.1:{cong2}/tot.zip",
            "sha256": sha_that,
        },
    }).encode())
    ag2._khi_co_lenh(None, None, lenh)

    # Tải + bung chạy ở luồng riêng để không chẹn vòng lặp mạng của paho.
    for _ in range(100):
        if any(t.endswith("/result") for t, _ in da_gui):
            break
        time.sleep(0.05)

    acks = [b for t, b in da_gui if t.endswith("/ack")]
    ketqua = [b for t, b in da_gui if t.endswith("/result")]

    Check(len(acks) == 1 and acks[0]["status"] == "accepted",
          "agent phải ack accepted NGAY, trước khi tải — không để Console treo chờ")
    Check(acks[0]["cmd_id"] == "abc123", "ack phải mang đúng cmd_id của lệnh")
    Check(len(ketqua) == 1, "bung xong phải gửi đúng một gói result")
    Check(ketqua[0]["verdict"] == "pass", f"result phải là pass, nhận {ketqua and ketqua[0]}")
    Check(ketqua[0]["cmd_id"] == "abc123", "result phải ghép được về đúng lệnh bằng cmd_id")

    bung_ra = os.path.join(autotests_gia, "Warning_VF8")
    Check(os.path.isdir(bung_ra), "gói phải được bung ra đúng AutoTests/<tên gói>/")
    Check(sorted(os.listdir(bung_ra)) == ["bai1.tc", "bai2.tc"],
          "hai bài trong gói phải nằm đúng chỗ sau khi bung")

    # sha lệch: phải hỏng ở agent, KHÔNG được để file hỏng lọt vào AutoTests/.
    da_gui.clear()
    lenh_xau = types.SimpleNamespace(payload=json.dumps({
        "cmd_id": "xau999",
        "action": "deploy_testcase",
        "goi": {"ten": "Goi_Hong", "url": f"http://127.0.0.1:{cong2}/tot.zip",
                "sha256": "c" * 64},
    }).encode())
    ag2._khi_co_lenh(None, None, lenh_xau)
    for _ in range(100):
        if any(t.endswith("/result") for t, _ in da_gui):
            break
        time.sleep(0.05)

    xau = [b for t, b in da_gui if t.endswith("/result")]
    Check(len(xau) == 1 and xau[0]["verdict"] == "fail",
          "sha lệch phải trả verdict fail")
    Check("sha256" in xau[0].get("reason", "").lower(),
          "lý do fail phải nói rõ sha256 lệch, đừng gộp thành 'thất bại'")
    Check(not os.path.exists(os.path.join(autotests_gia, "Goi_Hong")),
          "gói sha lệch TUYỆT ĐỐI không được bung ra AutoTests/")

    # Lệnh lạ vẫn phải bị từ chối tường minh, kèm tên lệnh.
    da_gui.clear()
    ag2._khi_co_lenh(None, None, types.SimpleNamespace(
        payload=json.dumps({"cmd_id": "la1", "action": "flash_ecu"}).encode()))
    tc = [b for t, b in da_gui if t.endswith("/ack")]
    Check(len(tc) == 1 and tc[0]["status"] == "rejected",
          "lệnh agent không làm được phải bị từ chối, không im lặng")
    Check("flash_ecu" in tc[0]["reason"],
          "lý do từ chối phải nói rõ lệnh nào không làm được")

    # start_test KHÔNG có report_url và agent không đặt --console: phải từ chối
    # chứ đừng nhận rồi im, vì nó không biết nộp kết quả về đâu.
    da_gui.clear()
    ag2._khi_co_lenh(None, None, types.SimpleNamespace(
        payload=json.dumps({"cmd_id": "run0", "action": "start_test"}).encode()))
    r0 = [b for t, b in da_gui if t.endswith("/ack")]
    Check(len(r0) == 1 and r0[0]["status"] == "rejected",
          "start_test thiếu report_url phải bị từ chối")
    Check("report_url" in r0[0]["reason"],
          "lý do phải nói rõ là thiếu report_url")

    # Lệnh thiếu trường: từ chối ngay, không được ném exception làm chết callback.
    da_gui.clear()
    ag2._khi_co_lenh(None, None, types.SimpleNamespace(
        payload=json.dumps({"cmd_id": "thieu", "action": "deploy_testcase",
                            "goi": {"ten": "X"}}).encode()))
    thieu = [b for t, b in da_gui if t.endswith("/ack")]
    Check(len(thieu) == 1 and thieu[0]["status"] == "rejected",
          "lệnh deploy thiếu url/sha phải bị từ chối, không được ném exception")
finally:
    may_chu2.shutdown()
    may_chu2.server_close()

# ------------------------------------------- nguồn file kết quả và nộp báo cáo
print()
print("── Nguồn file kết quả (chỗ duy nhất phải sửa khi có test thật)")

from bench_agent import nop_bao_cao, tim_file_ket_qua

# tu-sinh: luôn ra file, và nội dung phải NÓI RÕ là giả lập. Nhét một file
# trông giống log thật vào lịch sử test là kiểu hỏng im lặng đắt nhất.
f_sinh = tim_file_ket_qua("tu-sinh", "bai_thu", "cmd123")
Check(len(f_sinh) == 1 and os.path.exists(f_sinh[0]),
      "tu-sinh phải ra đúng một file có thật")
noi_dung = open(f_sinh[0], encoding="utf-8").read()
Check("GIẢ LẬP" in noi_dung, "file tự sinh phải tự khai là giả lập")
Check("cmd123" in noi_dung and "bai_thu" in noi_dung,
      "file tự sinh phải mang cmd_id và tên bài để truy ngược được")

# mau:<file>
thu_muc_mau = tempfile.mkdtemp(prefix="mau-kq-")
mot_file = os.path.join(thu_muc_mau, "TestCaseLog.txt")
open(mot_file, "w", encoding="utf-8").write("RESULT: Pass")
open(os.path.join(thu_muc_mau, "CAN1.txt"), "w", encoding="utf-8").write("frame")
Check(tim_file_ket_qua(f"mau:{mot_file}", None, "c") == [mot_file],
      "mau:<file> phải trả đúng file đó")
Check(len(tim_file_ket_qua(f"mau:{thu_muc_mau}", None, "c")) == 2,
      "mau:<thư mục> phải trả mọi file trong thư mục")

# qauto: lấy lượt chạy MỚI NHẤT, không phải lượt đầu tiên gặp
goc_out = tempfile.mkdtemp(prefix="outlog-")
cu = os.path.join(goc_out, "2026-09-24-10-00-00", "BaiCu", "10-00-00-000")
moi = os.path.join(goc_out, "2026-09-25-15-00-00", "BaiMoi", "15-00-00-000")
for d in (cu, moi):
    os.makedirs(d)
    open(os.path.join(d, "TestCaseLog.txt"), "w", encoding="utf-8").write("x")
os.utime(cu, (1_600_000_000, 1_600_000_000))
os.utime(moi, (1_700_000_000, 1_700_000_000))
lay = tim_file_ket_qua(f"qauto:{goc_out}", None, "c")
Check(len(lay) == 1 and "BaiMoi" in lay[0],
      f"qauto phải lấy lượt MỚI NHẤT, nhận {lay}")

for xau in ("khong-hieu", "mau:D:/khong-ton-tai-dau", "qauto:D:/khong-co-dau"):
    try:
        tim_file_ket_qua(xau, None, "c")
        Check(False, f"spec {xau!r} phải bị từ chối")
    except GoiHong:
        Check(True, f"spec {xau!r} bị từ chối đúng")

print()
print("── Nộp báo cáo lên Console qua multipart")

import http.server

nhan_duoc = {}


class NhanBaoCao(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        dai = int(self.headers.get("Content-Length", 0))
        than = self.rfile.read(dai)
        nhan_duoc["duong_dan"] = self.path
        nhan_duoc["ctype"] = self.headers.get("Content-Type", "")
        nhan_duoc["than"] = than
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(b"[]")

    def log_message(self, *a):
        pass


may_bc = socketserver.TCPServer(("127.0.0.1", 0), NhanBaoCao)
cong_bc = may_bc.server_address[1]
threading.Thread(target=may_bc.serve_forever, daemon=True).start()
try:
    url_bc = f"http://127.0.0.1:{cong_bc}/api/runs/cmd999/report"
    ma = nop_bao_cao(url_bc, [mot_file], "bai_thu", "VIVI-01")
    Check(ma == 200, "nộp báo cáo phải trả HTTP 200")
    Check(nhan_duoc["duong_dan"] == "/api/runs/cmd999/report",
          "phải POST đúng đường dẫn có cmd_id")
    Check("multipart/form-data; boundary=" in nhan_duoc["ctype"],
          "phải gửi đúng multipart kèm boundary")
    than = nhan_duoc["than"]
    Check(b'name="file"; filename="TestCaseLog.txt"' in than,
          "thân multipart phải mang tên file gốc")
    Check(b"RESULT: Pass" in than, "nội dung file phải đi trọn vẹn")
    Check(b'name="benchCode"' in than and b"VIVI-01" in than,
          "phải kèm mã bench để Console ghép được khi chưa có lệnh khớp")

    # Console trả lỗi thì agent phải nói rõ mã HTTP, đừng nuốt.
    class TraLoi500(NhanBaoCao):
        def do_POST(self):
            self.send_response(500); self.end_headers(); self.wfile.write(b"vo")
    may_loi = socketserver.TCPServer(("127.0.0.1", 0), TraLoi500)
    cong_loi = may_loi.server_address[1]
    threading.Thread(target=may_loi.serve_forever, daemon=True).start()
    try:
        nop_bao_cao(f"http://127.0.0.1:{cong_loi}/x", [mot_file], None, "B")
        Check(False, "Console trả 500 thì nop_bao_cao phải ném GoiHong")
    except GoiHong as ex:
        Check("500" in str(ex), "lỗi phải nói rõ mã HTTP 500")
    finally:
        may_loi.shutdown(); may_loi.server_close()

    # ---- cả vòng: nhận lệnh chạy -> nộp báo cáo -> trả verdict
    print()
    print("── Agent nhận lệnh chạy test (giả lập, không cần broker)")

    args_chay = types.SimpleNamespace(
        id="VIVI-01", model="VF6", interval=5.0, heartbeat=20.0,
        adb=os.path.join(tempfile.mkdtemp(), "adb.exe"),
        qauto_log=os.path.join(tempfile.mkdtemp(), "log.txt"),
        autotests=tempfile.mkdtemp(),
        ket_qua=f"mau:{mot_file}", gia_lap_giay=0.1, console=None)
    ag3 = BenchAgent(args_chay)
    gui3 = []
    ag3.client = types.SimpleNamespace(
        publish=lambda topic, payload, qos=0, retain=False:
            gui3.append((topic, json.loads(payload))))

    nhan_duoc.clear()
    ag3._khi_co_lenh(None, None, types.SimpleNamespace(payload=json.dumps({
        "cmd_id": "run777", "action": "start_test", "test_case": "bai_thu",
        "report_url": f"http://127.0.0.1:{cong_bc}/api/runs/{{cmd_id}}/report",
    }).encode()))

    for _ in range(100):
        if any(t.endswith("/result") for t, _ in gui3):
            break
        time.sleep(0.05)

    ack3 = [b for t, b in gui3 if t.endswith("/ack")]
    kq3 = [b for t, b in gui3 if t.endswith("/result")]
    Check(len(ack3) == 1 and ack3[0]["status"] == "accepted",
          "phải ack accepted ngay, trước khi chạy")
    Check(len(kq3) == 1, "chạy xong phải gửi đúng một gói result")
    Check(kq3[0]["cmd_id"] == "run777", "result phải ghép về đúng lệnh")
    # Điểm quan trọng nhất của cả phép kiểm này.
    Check(kq3[0]["verdict"] == "unknown",
          "chạy giả lập TUYỆT ĐỐI không được báo pass — đó là bịa kết quả test")
    Check("giả lập" in (kq3[0].get("reason") or ""),
          "result phải nói rõ đây là chạy giả lập")
    Check(nhan_duoc.get("duong_dan") == "/api/runs/run777/report",
          "phải nộp báo cáo vào đúng đường dẫn mang cmd_id thật")
    Check(ag3.dang_gia_lap is None, "chạy xong phải xoá cờ đang chạy")
finally:
    may_bc.shutdown()
    may_bc.server_close()

# ------------------------------------------------------------------ tổng kết
print("\n" + "=" * 51)
if loi:
    print(f"{len(loi)}/{so_phep} phép kiểm tra HỎNG:")
    for l in loi:
        print("  - " + l)
    sys.exit(1)
print(f"TẤT CẢ {so_phep} phép kiểm tra ĐẠT")
