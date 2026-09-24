#!/usr/bin/env python3
"""
Điều khiển Qauto bằng UI Automation — ĐÃ CHỐT BỎ 24/09.

**Đây KHÔNG phải code đang dùng.** Nó chạy được thật (Invoke lúc 10:46:29.243
→ Qauto ghi `Run Test Case` lúc 10:46:29.312), nhưng đường UIA đã bị loại: nút
không có `Name` lẫn `AutomationId` nên chỉ nhận được theo vị trí, mà trong hai
ngày 23–24/09 cửa sổ Qauto đã dời chỗ và đổi tỉ lệ ba lần. Bảo trì không nổi.

Đường thay thế đã chốt: **xin đội Qauto build bản có MQTT sẵn**, đặc tả ở
`docs/yeu-cau-qauto-mqtt.md`. File này giữ lại làm **bằng chứng kèm yêu cầu
đó** — chứng minh luồng nhận lệnh → chạy → trả kết quả là khả thi. Đừng nối nó
vào `bench_agent.py`, đừng bỏ công sửa tiếp.

--- Nội dung gốc ---

Điều khiển Qauto bằng UI Automation.

Qauto không có dòng lệnh, không mở cổng mạng, không có named pipe, `.tc` không
gắn file association. Đường duy nhất là điều khiển giao diện — nó là ứng dụng
WPF nên phơi ra cây UIA đầy đủ. Xem mục "Điều khiển Qauto bằng UIA" trong
CLAUDE.md để biết đã loại những đường nào và vì sao.

**Gọi UIA qua PowerShell chứ không qua thư viện Python.** Lý do: máy bench
trong xưởng thường bị khoá, cài thêm gói là phiền; còn PowerShell thì Windows
nào cũng có sẵn. Mỗi lần gọi tốn vài trăm mili giây, nhưng cả chuỗi này vốn
mất hàng chục giây nên không đáng kể.

Tự kiểm tra mà KHÔNG chạy test nào:
    python qauto_dieu_khien.py --kiem-tra
"""

import argparse
import json
import os
import subprocess
import time

QAUTO_EXE_MAC_DINH = r"D:\Qauto_2610\Qauto_2610\Qauto.exe"

# Bản đã hiệu chỉnh. Toạ độ các nút biểu tượng lấy theo bản này; bản khác thì
# bố cục có thể đổi, nên agent phải TỪ CHỐI chạy chứ đừng bấm mò — bấm nhầm
# nút trên một bench thật đắt hơn nhiều so với việc không chạy.
PHIEN_BAN_HIEU_CHINH = "QAuto_V2.6.10.2"

# Các nút thanh công cụ KHÔNG có Name lẫn AutomationId, nên phải nhận theo vị
# trí. Nhưng TUYỆT ĐỐI không chốt cứng toạ độ màn hình: chỉ trong hai ngày
# 23–24/09, cửa sổ Qauto đã dời chỗ và đổi tỉ lệ ba lần (nút rộng 44 px thành
# 35 px, gốc cửa sổ từ (20,17) sang (-7,-7) rồi (0,0)).
#
# Cách bền: lọc nút VUÔNG (nút biểu tượng), lấy HÀNG TRÊN CÙNG, sắp theo x rồi
# đếm thứ tự. Sống được qua việc dời cửa sổ, đổi tỉ lệ và đổi độ phân giải.
#
# Thứ tự từ trái sang: 0=xe, 1=?, 2=CHẠY, 3=Cài đặt, 4=Log
THU_TU_NUT_CHAY = 2
THU_TU_NUT_SETTINGS = 3
THU_TU_NUT_DUNG = 0          # nút 🚗, vô hiệu khi rảnh — dấu hiệu test đang chạy

THIET_BI_CAN_MAC_DINH = "PCAN_USB:FD 1 (51h)"


class QautoLoi(Exception):
    """Một bước trong chuỗi không làm được. Thông điệp phải đủ để Console
    hiện cho người ở xa biết nên gọi ai ra bench."""


# --------------------------------------------------------------------------
# Khung gọi PowerShell
# --------------------------------------------------------------------------

_MO_DAU = r"""
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function Cua-So {
  $p = Get-Process Qauto -ErrorAction SilentlyContinue
  if (-not $p) { return $null }
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
}
function Tim-Theo-Id($w, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  return $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function Nut-Thanh-Cong-Cu($w, $thuTu) {
  # Nut bieu tuong la nut VUONG; nut thu nho/phong to/dong cua so thi rong hon cao.
  $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
  $vuong = @()
  foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
    $r = $e.Current.BoundingRectangle
    if ($r.Width -gt 10 -and [math]::Abs($r.Width - $r.Height) -le 4) {
      $vuong += [pscustomobject]@{ E = $e; X = $r.X; Y = $r.Y }
    }
  }
  if ($vuong.Count -eq 0) { return $null }
  $dinh = ($vuong | Measure-Object -Property Y -Minimum).Minimum
  $hang = $vuong | Where-Object { [math]::Abs($_.Y - $dinh) -le 6 } | Sort-Object X
  # Chi lay cum lien tuc dau tien: con vai nut vuong khac o goc phai man hinh,
  # cach thanh cong cu hang nghin pixel.
  $cum = @($hang[0])
  for ($i = 1; $i -lt $hang.Count; $i++) {
    if (($hang[$i].X - $hang[$i-1].X) -gt 60) { break }
    $cum += $hang[$i]
  }
  if ($thuTu -ge $cum.Count) { return $null }
  return $cum[$thuTu].E
}
function Combo-Thiet-Bi-Can($w) {
  # Nhan theo NGHIA chu khong theo toa do: o chon thiet bi CAN co muc dang chon
  # ten la "Common.Core.CanHelpers.CanDevice". Lay cai tren cung, trai nhat —
  # do la hang "1. Info Can".
  $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ComboBox)
  $ds = @()
  foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
    $v = ''
    try { $v = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch {}
    if ($v -like '*CanDevice*') {
      $r = $e.Current.BoundingRectangle
      $ds += [pscustomobject]@{ E = $e; X = $r.X; Y = $r.Y }
    }
  }
  if ($ds.Count -eq 0) { return $null }
  return ($ds | Sort-Object Y, X)[0].E
}
function Bam($e) {
  $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
"""


def _ps(than: str, timeout: float = 60.0) -> dict:
    """Chạy một đoạn PowerShell, đoạn đó phải tự in ra JSON một dòng."""
    try:
        r = subprocess.run(["powershell", "-NoProfile", "-Command", _MO_DAU + than],
                           capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired as ex:
        raise QautoLoi(f"PowerShell quá hạn {timeout:.0f}s") from ex
    ra = (r.stdout or "").strip().splitlines()
    for d in reversed(ra):                     # lấy dòng JSON cuối cùng
        d = d.strip()
        if d.startswith("{"):
            try:
                return json.loads(d)
            except json.JSONDecodeError:
                continue
    raise QautoLoi(f"Không đọc được kết quả PowerShell. stderr={r.stderr.strip()[:300]!r}")


# --------------------------------------------------------------------------
# Trạng thái
# --------------------------------------------------------------------------

def dang_mo() -> bool:
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Qauto.exe", "/NH"],
                         capture_output=True, text=True).stdout
    return "qauto.exe" in out.lower()


def phien_ban() -> str | None:
    """Đọc chuỗi phiên bản hiện trên giao diện, ví dụ 'QAuto_V2.6.10.2'."""
    kq = _ps(r"""
$w = Cua-So
if (-not $w) { '{"co":false}'; exit }
$c = New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::Text)
$v = ''
foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
  if ($e.Current.Name -match '^QAuto_V') { $v = $e.Current.Name }
}
@{co=$true; phien_ban=$v} | ConvertTo-Json -Compress
""")
    return kq.get("phien_ban") or None if kq.get("co") else None


def kiem_phien_ban() -> None:
    """Từ chối làm gì nếu bản Qauto khác bản đã hiệu chỉnh toạ độ."""
    pb = phien_ban()
    if pb is None:
        raise QautoLoi("Không đọc được phiên bản Qauto — cửa sổ chưa sẵn sàng?")
    if pb != PHIEN_BAN_HIEU_CHINH:
        raise QautoLoi(
            f"Qauto là {pb}, toạ độ nút chỉ hiệu chỉnh cho {PHIEN_BAN_HIEU_CHINH}. "
            "Từ chối điều khiển để khỏi bấm nhầm nút. Hiệu chỉnh lại rồi sửa "
            "PHIEN_BAN_HIEU_CHINH trong qauto_dieu_khien.py.")


def dang_chay_test() -> bool:
    """Nút 🚗 ở x=20 bị vô hiệu khi rảnh và bật lên khi có test chạy.

    Dùng nó làm dấu hiệu thay vì đoán theo thời gian — thời gian chạy của mỗi
    bài khác nhau rất xa, từ một giây tới hàng phút.
    """
    kq = _ps(r"""
$w = Cua-So
if (-not $w) { '{"co":false}'; exit }
$b = Nut-Thanh-Cong-Cu $w 0
if (-not $b) { '{"co":false}'; exit }
@{co=$true; dang_chay=$b.Current.IsEnabled} | ConvertTo-Json -Compress
""")
    return bool(kq.get("dang_chay")) if kq.get("co") else False


# --------------------------------------------------------------------------
# Tắt mở
# --------------------------------------------------------------------------

def dong_qauto(cho: float = 20.0) -> None:
    """Đóng tử tế trước, hết hạn mới giết.

    Đóng tử tế để Qauto kịp ghi nốt log và đóng sổ lượt chạy dở. Giết ngay có
    thể làm mất thư mục kết quả của lượt cuối.
    """
    if not dang_mo():
        return
    subprocess.run(["taskkill", "/IM", "Qauto.exe"], capture_output=True)
    het = time.time() + cho
    while time.time() < het:
        if not dang_mo():
            return
        time.sleep(0.5)
    subprocess.run(["taskkill", "/F", "/IM", "Qauto.exe"], capture_output=True)
    time.sleep(1.0)
    if dang_mo():
        raise QautoLoi("Không đóng được Qauto, kể cả khi giết cứng")


def mo_qauto(exe: str = QAUTO_EXE_MAC_DINH, cho: float = 120.0) -> float:
    """Mở Qauto và chờ tới khi giao diện thật sự sẵn sàng.

    Chờ theo DẤU HIỆU chứ không theo đồng hồ: đợi tới khi đọc được chuỗi phiên
    bản trên giao diện. Log cho thấy thời gian khởi động dao động 13–35 giây
    tuỳ máy và tuỳ lúc, nên đặt một con số cứng là hoặc chờ thừa, hoặc chạy
    tiếp khi cửa sổ chưa dựng xong.
    """
    if not os.path.exists(exe):
        raise QautoLoi(f"Không thấy {exe}")
    subprocess.Popen([exe], cwd=os.path.dirname(exe),
                     creationflags=getattr(subprocess, "DETACHED_PROCESS", 0))
    bat_dau = time.time()
    het = bat_dau + cho
    while time.time() < het:
        try:
            if phien_ban():
                return time.time() - bat_dau
        except QautoLoi:
            pass
        time.sleep(1.0)
    raise QautoLoi(f"Qauto không sẵn sàng sau {cho:.0f}s")


# --------------------------------------------------------------------------
# Settings: chọn thiết bị CAN rồi nối
# --------------------------------------------------------------------------

def mo_settings() -> None:
    kq = _ps(f"""
$w = Cua-So
if (-not $w) {{ '{{"ok":false,"ly_do":"Qauto khong chay"}}'; exit }}
$b = Nut-Thanh-Cong-Cu $w {THU_TU_NUT_SETTINGS}
if (-not $b) {{ '{{"ok":false,"ly_do":"Khong thay nut Settings tren thanh cong cu"}}'; exit }}
Bam $b
Start-Sleep -Milliseconds 900
$ok = (Tim-Theo-Id $w 'ConnectAllButton') -ne $null
@{{ok=$ok; ly_do=$(if($ok){{''}}else{{'Bam roi nhung khong thay ConnectAllButton'}})}} | ConvertTo-Json -Compress
""")
    if not kq.get("ok"):
        raise QautoLoi("Mở Settings hỏng: " + str(kq.get("ly_do")))


def chon_thiet_bi_can(ten: str = THIET_BI_CAN_MAC_DINH) -> None:
    """Chọn thiết bị trong combobox Info Can.

    ComboBox bị WPF ảo hoá nên phải Expand() mới có mục. Tên của từng mục là
    `Common.Core.CanHelpers.CanDevice` — tên kiểu .NET, vô dụng; chữ thật nằm
    ở phần tử Text CON. Nên phải dò theo chữ của con.
    """
    kq = _ps(f"""
$w = Cua-So
if (-not $w) {{ '{{"ok":false,"ly_do":"Qauto khong chay"}}'; exit }}
$cb = Combo-Thiet-Bi-Can $w
if (-not $cb) {{ '{{"ok":false,"ly_do":"Khong thay combobox thiet bi CAN"}}'; exit }}
$ex = $cb.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
$ex.Expand(); Start-Sleep -Milliseconds 700
$ic = New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::ListItem)
$tc = New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::Text)
$thay = @(); $chon = $null
foreach ($it in $cb.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ic)) {{
  $chu = ''
  foreach ($t in $it.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tc)) {{
    if ($t.Current.Name.Trim()) {{ $chu = $t.Current.Name.Trim() }}
  }}
  $thay += $chu
  if ($chu -eq {json.dumps(ten)}) {{ $chon = $it }}
}}
if ($chon -eq $null) {{
  $ex.Collapse()
  @{{ok=$false; ly_do='Khong thay thiet bi'; co=$thay}} | ConvertTo-Json -Compress; exit
}}
$chon.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 300
@{{ok=$true; co=$thay}} | ConvertTo-Json -Compress
""")
    if not kq.get("ok"):
        raise QautoLoi(f"Không chọn được thiết bị CAN {ten!r}. "
                       f"Danh sách đang có: {kq.get('co')}")


def _bam_theo_id(aid: str, ten_viec: str, cho_ms: int = 800) -> None:
    kq = _ps(f"""
$w = Cua-So
if (-not $w) {{ '{{"ok":false,"ly_do":"Qauto khong chay"}}'; exit }}
$b = Tim-Theo-Id $w '{aid}'
if (-not $b) {{ '{{"ok":false,"ly_do":"Khong thay {aid}"}}'; exit }}
if (-not $b.Current.IsEnabled) {{ '{{"ok":false,"ly_do":"{aid} dang vo hieu"}}'; exit }}
Bam $b
Start-Sleep -Milliseconds {cho_ms}
@{{ok=$true}} | ConvertTo-Json -Compress
""")
    if not kq.get("ok"):
        raise QautoLoi(f"{ten_viec} hỏng: {kq.get('ly_do')}")


def connect_all() -> None:
    # Nối CAN mất một lúc, chờ lâu hơn các nút khác.
    _bam_theo_id("ConnectAllButton", "Connect All", cho_ms=3000)


def apply_settings() -> None:
    _bam_theo_id("ApplyButton", "Apply", cho_ms=1500)


def dong_settings() -> None:
    _bam_theo_id("CancelButton", "Đóng Settings", cho_ms=800)


# --------------------------------------------------------------------------
# Chạy
# --------------------------------------------------------------------------

def bam_chay() -> None:
    kq = _ps(f"""
$w = Cua-So
if (-not $w) {{ '{{"ok":false,"ly_do":"Qauto khong chay"}}'; exit }}
$b = Nut-Thanh-Cong-Cu $w {THU_TU_NUT_CHAY}
if (-not $b) {{ '{{"ok":false,"ly_do":"Khong thay nut Chay tren thanh cong cu"}}'; exit }}
if (-not $b.Current.IsEnabled) {{ '{{"ok":false,"ly_do":"Nut Chay dang vo hieu"}}'; exit }}
Bam $b
@{{ok=$true}} | ConvertTo-Json -Compress
""")
    if not kq.get("ok"):
        raise QautoLoi("Bấm Chạy hỏng: " + str(kq.get("ly_do")))


def cho_chay_xong(toi_da: float = 600.0, cho_bat_dau: float = 15.0) -> float:
    """Chờ tới khi test chạy xong, theo dấu hiệu nút Dừng chứ không theo đồng hồ."""
    bat_dau = time.time()
    het_bd = bat_dau + cho_bat_dau
    while time.time() < het_bd and not dang_chay_test():
        time.sleep(0.5)
    het = bat_dau + toi_da
    while time.time() < het:
        if not dang_chay_test():
            return time.time() - bat_dau
        time.sleep(1.0)
    raise QautoLoi(f"Test vẫn chạy sau {toi_da:.0f}s, không chờ nữa")


# --------------------------------------------------------------------------
# Cả chuỗi
# --------------------------------------------------------------------------

def nap_lai(exe: str = QAUTO_EXE_MAC_DINH,
            thiet_bi_can: str = THIET_BI_CAN_MAC_DINH) -> dict:
    """Tắt mở Qauto rồi nối lại CAN — bắt buộc sau khi đổi nội dung AutoTests.

    Qauto không tự thấy thư mục mới, và mở lại thì cũng không tự nối CAN. Cả
    hai đều đã kiểm chứng trên máy thật, xem CLAUDE.md.
    """
    moc = {}
    t0 = time.time()
    dong_qauto()
    moc["dong_giay"] = round(time.time() - t0, 1)

    moc["khoi_dong_giay"] = round(mo_qauto(exe), 1)
    kiem_phien_ban()

    t1 = time.time()
    mo_settings()
    chon_thiet_bi_can(thiet_bi_can)
    connect_all()
    apply_settings()
    dong_settings()
    moc["noi_can_giay"] = round(time.time() - t1, 1)
    moc["tong_giay"] = round(time.time() - t0, 1)
    return moc


def main():
    ap = argparse.ArgumentParser(description="Điều khiển Qauto bằng UIA")
    ap.add_argument("--exe", default=QAUTO_EXE_MAC_DINH)
    ap.add_argument("--thiet-bi-can", default=THIET_BI_CAN_MAC_DINH)
    ap.add_argument("--kiem-tra", action="store_true",
                    help="soi trạng thái, KHÔNG bấm gì, không chạy test nào")
    ap.add_argument("--nap-lai", action="store_true",
                    help="tắt mở Qauto rồi nối lại CAN (KHÔNG chạy test)")
    args = ap.parse_args()

    if args.kiem_tra:
        print(json.dumps({
            "qauto_dang_mo": dang_mo(),
            "phien_ban": phien_ban(),
            "khop_ban_hieu_chinh": phien_ban() == PHIEN_BAN_HIEU_CHINH,
            "dang_chay_test": dang_chay_test() if dang_mo() else None,
            "exe_ton_tai": os.path.exists(args.exe),
        }, ensure_ascii=False, indent=2))
        return

    if args.nap_lai:
        print(json.dumps(nap_lai(args.exe, args.thiet_bi_can),
                         ensure_ascii=False, indent=2))
        return

    ap.print_help()


if __name__ == "__main__":
    main()
