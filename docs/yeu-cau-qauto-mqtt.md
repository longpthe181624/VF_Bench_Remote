# Đề nghị đội Qauto: kênh MQTT nhận lệnh chạy test

Gửi đội phát triển Qauto — từ nhóm làm **Bench Console** (chạy test bench từ xa).
Ngày 24/09/2026. Đối chiếu trên bản `QAuto_V2.6.10.2`.

## Tóm tắt một đoạn

Chúng tôi cần **một kênh để gửi lệnh "chạy test case này" tới Qauto và nhận kết
quả về**. Hiện Qauto không có đường nào như vậy, nên chúng tôi đang phải điều
khiển giao diện bằng UI Automation — cách đó mong manh và sẽ hỏng mỗi khi Qauto
đổi bố cục.

Qauto **đã có sẵn** hạ tầng MQTT v5, chứng chỉ mTLS cho 5 vùng và 14 topic đang
chạy. Đề nghị này chỉ xin **thêm 4 topic** vào thứ đã có, không phải xây mới.

## Vì sao không dùng được cách khác

Chúng tôi đã thử hết và ghi lại kết quả:

| Cách | Kết quả |
| --- | --- |
| Tham số dòng lệnh | Đã thử. `Qauto.exe --help` không in gì; truyền đường dẫn `.tc` vào bản đang chạy lẫn bản mới khởi động đều **không được nhận** |
| Cổng mạng / named pipe | Qauto không mở cái nào |
| File association `.tc` | Không đăng ký |
| Bỏ file vào `AutoTests/` | Qauto **chỉ quét lúc khởi động**, phải tắt mở lại mới thấy |
| UI Automation | Chạy được, nhưng xem mục "Vì sao cách hiện tại không bền" bên dưới |

## Đề nghị: 4 topic

Đặt dưới namespace riêng để không lẫn với 14 topic xe hiện có.

```
autolab/{bench_id}/cmd       Console → Qauto
autolab/{bench_id}/ack       Qauto → Console
autolab/{bench_id}/status    Qauto → Console   (retained + Last Will)
autolab/{bench_id}/result    Qauto → Console
```

`{bench_id}` là **mã bench do Console cấp** khi đăng ký, ví dụ `QAUTO-01`.
Cần **một ô cấu hình mới trong Settings** để tester điền, đặt cạnh ô `Vincode`.

**Vì sao không dùng luôn `{vincode}` sẵn có.** Đã cân nhắc và loại: `Vincode`
đi theo **chiếc xe / con MHU**, còn bench thì **giữ nguyên danh tính khi thay
MHU** — đây là việc xảy ra thật trong xưởng. Lấy `vincode` làm khoá topic thì
mỗi lần thay MHU là Console mất dấu con bench, lịch sử test đứt làm hai mảnh
mà không có gì báo. Mã bench do người đăng ký thì bền qua việc thay linh kiện.

Vẫn **gửi `vincode` trong payload `status`** (xem dưới) — Console dùng nó để
đối chiếu và cảnh báo khi xe trong bench khác với hồ sơ đã đăng ký, chứ không
dùng làm khoá định danh.

Hai điều kiện cần bảo đảm:

- Mỗi máy đều đã điền mã bench trong Settings.
- Hai bench không trùng mã. Console cấp mã nên chuyện này do Console lo.

### `cmd` — Console gửi xuống

```json
{
  "cmd_id": "9f3a1c20",
  "action": "run",
  "params": {
    "test_cases": ["AutoTests/9VN_all_language/9VI_VN/[TC002-...].tc"],
    "folder": null,
    "repeat": 1
  },
  "issued_by": "long.pt",
  "ts": 1758700000000
}
```

`action` cần tối thiểu: `run`, `stop`, `get_info`.

Trong `params` của `run`, **một trong hai**:
- `test_cases` — danh sách đường dẫn cụ thể, chạy theo thứ tự
- `folder` — chạy **toàn bộ** test case trong thư mục đó

Hai kiểu này ứng với hai cách người dùng thao tác trên Console: chọn từng bài,
hoặc chọn cả thư mục.

### `ack` — Qauto trả lời ngay, trước khi chạy

```json
{ "cmd_id": "9f3a1c20", "status": "rejected",
  "reason": "mhu_not_connected",
  "detail": "There is no connected device MHU",
  "ts": 1758700000400 }
```

**Đây là phần chúng tôi tha thiết nhất.** Xin Qauto **kiểm điều kiện rồi mới
nhận lệnh**, và từ chối tường minh khi thiếu:

| `reason` | Khi nào |
| --- | --- |
| `can_not_connected` | Chưa nối CAN |
| `mhu_not_connected` | Chưa thấy MHU |
| `test_case_not_found` | Không có bài đó |
| `busy` | Đang chạy bài khác, hoặc có người thao tác tại chỗ |
| `resource_missing` | Thiếu tài nguyên, ví dụ thư viện ảnh mẫu `C:\Tools\WMC_icon\` |

Lý do xin điều này: hôm 24/09 chúng tôi chạy bài
`[TC002-VF89FL-ID801] Verify warning ...` trên một bench **chưa cắm MHU**. Qauto
báo:

```
RESULT: Fail:
WRONG CONTENT in Car Buddy
WRONG CONTENT in Alerts App
WRONG ICON in Car Buddy
WRONG ICON in Alerts App
```

Người ngồi tại bench nhìn là biết ngay bench chưa sẵn sàng. Nhưng **người ở xa
chỉ nhận được bốn dòng đó và sẽ mở bug cho sản phẩm** — trong khi thật ra chưa
hề nhìn được màn hình. Thông tin phân biệt hai chuyện nằm ở dòng
`End UI: There is no connected device MHU` trong log từng bước, không nằm trong
verdict.

Từ chối trước khi chạy thì không sinh ra kết quả sai lệch nào cả.

### `status` — retained, kiêm Last Will

```json
{
  "bench_id": "QAUTO-01",
  "vincode": "VF3xxxxxxxxxxxxxxx",
  "state": "idle",
  "qauto_version": "QAuto_V2.6.10.2",
  "can_connected": true,
  "mhu_connected": false,
  "current": { "cmd_id": null, "test_case": null, "step": null },
  "ts": 1758700000000
}
```

`state`: `idle` | `running` | `busy_local` | `error` | `offline`.

`busy_local` nghĩa là có kỹ sư đang ngồi thao tác trực tiếp — Console sẽ không
gửi lệnh vào, tránh giành máy với người.

Xin đặt **Last Will** trên chính topic này với `state: "offline"`, để Console
biết ngay khi máy bench mất điện hay đứt mạng, không phải chờ hết hạn.

### `result` — mỗi bài một bản tin

```json
{
  "cmd_id": "9f3a1c20",
  "test_case": "[TC002-VF89FL-ID801-Not apply for Legacy] Verify warning ...",
  "verdict": "fail",
  "reasons": ["WRONG CONTENT in Car Buddy", "WRONG ICON in Alerts App"],
  "steps": [
    { "no": 4, "name": "Check warning icon in Car Buddy",
      "result": "fail", "note": "There is no connected device MHU" }
  ],
  "duration_ms": 13400,
  "artifacts": { "folder": "Output/OutputLog/2026-09-24-09-17-20/..." },
  "ts": 1758700013400
}
```

Hai chỗ xin lưu ý:

**`verdict` xin có 4 giá trị**: `pass`, `fail`, `warning`, `unknown`. Hiện chúng
tôi chỉ thấy `Pass` và `Fail`; `warning` và `unknown` giúp phân biệt "chạy xong
nhưng có ngờ vực" với "không kết luận được".

**`steps` xin kèm `note` của từng bước.** Đây là chỗ chứa câu giải thích thật.
Chỉ có verdict thì người ở xa không phân biệt được "sản phẩm sai" với "bench
chưa sẵn sàng".

## Hai thứ xin thêm nếu được, không bắt buộc

**`action: "reload"`** — nạp lại danh mục test case mà không phải tắt mở Qauto.
Hiện mỗi lần đẩy bộ test case mới xuống bench, chúng tôi phải tắt Qauto, mở lại,
vào Settings chọn CAN, `Connect All`, `Apply` — chín bước, vài chục giây, và
đóng mất cửa sổ của người đang dùng.

**`action: "get_info"`** → trả về danh sách test case hiện có và phiên bản
Qauto, để Console không phải đoán trên bench đang có gì.

## Vì sao cách hiện tại không bền

Chúng tôi đã chạy được bằng UI Automation: gọi `InvokePattern.Invoke()` lên nút
Chạy, 69 mili giây sau Qauto chạy thật. Nhưng:

- **19 nút trên giao diện đều không có `Name` lẫn `AutomationId`**, chỉ phân
  biệt được bằng toạ độ. Trong vòng vài tiếng ngày 24/09, cửa sổ Qauto dời chỗ
  và đổi tỉ lệ (nút rộng 44 px thành 35 px) — toạ độ chúng tôi đo buổi sáng đã
  sai buổi chiều.
- **Cây test case không chọn được bằng UIA.** Các mục chỉ là `Text` trần, không
  có `SelectionItemPattern` lẫn `InvokePattern`. Muốn chọn một bài cụ thể thì
  phải bấm chuột theo toạ độ — tức giành con trỏ của kỹ sư đang ngồi đó.
- Mỗi bản Qauto mới, chúng tôi phải đo lại toạ độ từ đầu.

Có 4 topic trên thì toàn bộ phần này biến mất.

## Liên hệ

Nhóm Bench Console. Sẵn sàng trao đổi chi tiết payload, và sẵn sàng thử bản
nháp trên bench thật ngay khi có.
