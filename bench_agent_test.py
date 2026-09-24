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

import os
import sys
import tempfile
import types
import zipfile

from bench_agent import (BenchAgent, DocLogQauto, GoiHong, LuotChay,
                         bung_goi_test_case, danh_gia_qauto, doi_verdict,
                         ma_model, mo_ta_qauto, suy_trang_thai)

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

# ------------------------------------------------------------------ tổng kết
print("\n" + "=" * 51)
if loi:
    print(f"{len(loi)}/{so_phep} phép kiểm tra HỎNG:")
    for l in loi:
        print("  - " + l)
    sys.exit(1)
print(f"TẤT CẢ {so_phep} phép kiểm tra ĐẠT")
