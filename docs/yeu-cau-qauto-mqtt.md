# Yêu cầu Qauto làm client của Bench Console

Gửi đội phát triển Qauto. Cập nhật 25/09/2026.

Mục tiêu: **Qauto tự nói chuyện với Bench Console**, không cần phần mềm trung
gian nào trên máy bench. Hiện chúng tôi có một agent Python chạy tạm để chứng
minh luồng này hoạt động — nó đã chạy thật và sẽ được bỏ đi khi Qauto làm được.

Tài liệu chỉ gồm bốn việc: **địa chỉ server**, **thông tin gửi lên**, **nhận
test plan**, **trả kết quả**.

Hai chỗ đánh dấu **[cần xác nhận]** là giả định của chúng tôi, mong đội Qauto
phản hồi nếu thấy không hợp.

## Nguyên tắc chung

**File đi REST, trạng thái và lệnh đi MQTT.** Chúng tôi đo thật qua đường mạng
đang dùng: gói MQTT 1,6 MB mất ~0,9 giây, nhưng **2 MB thì tắc hẳn** và còn
làm nghẽn broker. Nên mọi thứ là file — gói test case, log, trace CAN — phải
đi HTTP.

**Bench phải được đăng ký trước trên Console.** Qauto gửi lên với một mã bench
chưa đăng ký thì Console **bỏ qua trong im lặng**, không báo lỗi. Đây là cố ý
(gõ sai một ký tự sẽ sinh bench rác), nhưng xin lưu ý khi gỡ lỗi.

**Hiện chưa có xác thực.** Không API key, không token. Sẽ có sau, và sẽ báo
trước.

---

## 1. Địa chỉ server

Xin thêm **một ô cấu hình duy nhất** trong Settings, cạnh ô `Vincode`:

| Ô | Ví dụ |
| --- | --- |
| `Server` | `vinfast.tail1cbef5.ts.net` |
| `BenchId` | `VIVI-01` |

Từ `Server`, Qauto tự dựng hai địa chỉ: **[cần xác nhận]**

```
MQTT : tcp://<Server>:1883
REST : http://<Server>:5000
```

Một ô thay vì ba, vì hiện hai dịch vụ nằm cùng một máy. Nếu đội Qauto muốn
tách riêng thì cho ba ô cũng được, chúng tôi không vướng gì.

`BenchId` là mã do Console cấp khi đăng ký bench. **Không dùng `Vincode`** làm
định danh: Vincode đi theo xe/MHU, mà thay MHU thì bench vẫn giữ nguyên danh
tính — lấy Vincode làm khoá sẽ làm lịch sử test đứt làm hai mảnh.

Hai điều mong Qauto làm khi nối:

- **Thử lại có giãn dần** khi broker chưa lên, đừng chết ở lần nối đầu.
- **Đặt Last Will** (xem mục 2) — đây là thứ tạo ra trạng thái "Mất kết nối"
  trên Console.

---

## 2. Thông tin Qauto gửi lên

```
Topic : bench/<model>/<BenchId>/status
QoS   : 1        Retained : có
```

`<model>` là dòng xe trong bench, viết thường và thay khoảng trắng bằng gạch
nối: `VF8New ME` → `vf8new-me`. Lấy từ một ô cấu hình nữa, hoặc để trống cũng
được — Console khớp theo `BenchId`, không theo model.

```json
{
  "bench_id": "VIVI-01",
  "state": "idle",
  "host": "DESKTOP-A300NSF",
  "qauto_version": "QAuto_V2.6.10.2",
  "vincode": "VF3xxxxxxxxxxxxxxx",
  "can_connected": true,
  "mhu_connected": false,
  "dbc_loaded": 1,
  "dbc_missing": 16,
  "current": { "cmd_id": null, "test_case": null, "step": null },
  "detail": "Rảnh, sẵn sàng nhận lệnh",
  "ts": 1758700000000
}
```

`state` nhận đúng năm giá trị: `idle`, `running`, `busy_local`, `error`,
`offline`. Dùng `busy_local` khi có người đang ngồi thao tác tay tại bench.

**Gửi khi trạng thái đổi, và ít nhất mỗi 30 giây** dù không đổi.

**Last Will** — broker tự phát khi Qauto tắt hoặc đứt mạng, cùng topic,
retained:

```json
{ "bench_id": "VIVI-01", "host": "DESKTOP-A300NSF", "state": "offline", "ts": null }
```

### Bốn trường chúng tôi tha thiết nhất

Đây là những thứ **chỉ Qauto biết**, và mỗi cái đang là một lỗ hổng im lặng:

| Trường | Vì sao cần |
| --- | --- |
| `can_connected` | Không có nó, Console báo "Sẵn sàng" cho cả bench **chưa cắm dây vào xe**. Chúng tôi đo được adapter cắm vào máy tính, nhưng không biết máy có nối với bench hay không |
| `dbc_loaded` / `dbc_missing` | Trên một máy bench thật, **16 trong 17 đường dẫn DBC trong `user.config` trỏ vào file không tồn tại**. Qauto vẫn mở, vẫn chạy, vẫn báo `Pass`. Không ai biết |
| `mhu_connected` | Bài test dựa vào thao tác ADB sẽ không làm gì cả mà vẫn báo `Pass` |
| `qauto_version` | Bench trong xưởng không cùng phiên bản |

`host` là tên máy tính (`Environment.MachineName`). Console đối chiếu với tên
máy đã khai cho bench để phát hiện có người mang máy sang bench khác mà quên
đổi cấu hình.

---

## 3. Nhận tin có test plan để tải

```
Topic : bench/+/<BenchId>/cmd      ← nghe ký tự đại diện ở chỗ model
QoS   : 1        Retained : không
```

Nghe `+` ở chỗ model là **quan trọng**: khi người dùng đổi dòng xe của bench
trên Console, topic đổi theo. Bám cứng model thì lệnh rơi vào topic không ai
nghe, và Console sẽ báo nhầm thành "bench không phản hồi".

### Lệnh

```json
{
  "cmd_id": "a1b2c3d4e5f6g7h8",
  "action": "run_plan",
  "plan": {
    "id": 12,
    "ten": "Warning VF8 — bộ đầy đủ",
    "goi": {
      "url": "http://vinfast.tail1cbef5.ts.net:5000/api/test-cases/1/tai",
      "sha256": "cb53ab8f8316384e6ac3cddb0707485e08a54ae6a4b9dabef06e274410b00308",
      "ten_thu_muc": "Warning_VF8"
    },
    "test_cases": [
      "Warning_VF8/[TC002-VF89FL-ID801] Verify warning.tc",
      "Warning_VF8/[TC003-VF89FL-ID802] Verify warning.tc"
    ],
    "dung_khi_fail": false,
    "lap_lai": 1
  },
  "issued_by": "long.pt",
  "ts": 1758700000000
}
```

Các bước mong Qauto làm:

1. Tải `plan.goi.url` qua HTTP, **kiểm `sha256`**. Lệch thì dừng, báo lỗi, và
   **tuyệt đối không giải nén**. Gói hỏng mà vẫn chạy thì Qauto vẫn báo `Pass`
   trên một bài không còn đúng nữa.
2. Giải nén vào `AutoTests/<ten_thu_muc>/`, ghi đè nếu đã có. Gói là **ZIP**.
3. Chạy lần lượt các bài trong `test_cases`, theo đúng thứ tự.

`test_cases` rỗng nghĩa là **chạy toàn bộ** thư mục vừa giải nén.

Danh sách bài do Console giữ và gửi xuống, **không** nằm trong gói **[cần xác
nhận]** — để sửa được plan trên web mà không phải nén lại gói.

`action` cần tối thiểu: `run_plan`, `stop`, `get_info`.

### Trả lời ngay — phần quan trọng nhất

```
Topic : bench/<model>/<BenchId>/ack      QoS 1, không retained
```

```json
{ "cmd_id": "a1b2c3d4e5f6g7h8", "status": "rejected",
  "reason": "mhu_not_connected",
  "detail": "There is no connected device MHU",
  "ts": 1758700000400 }
```

`status`: `accepted` hoặc `rejected`. Trả **trước khi bắt đầu chạy**.

**Xin Qauto kiểm điều kiện rồi mới nhận lệnh**, và từ chối tường minh khi
thiếu:

| `reason` | Khi nào |
| --- | --- |
| `can_not_connected` | Chưa nối CAN, hoặc chưa cắm vào bench |
| `mhu_not_connected` | Chưa thấy MHU |
| `dbc_missing` | Thiếu file DBC mà bài test cần |
| `test_case_not_found` | Không có bài đó trong gói |
| `busy` | Đang chạy bài khác, hoặc có người thao tác tại chỗ |
| `resource_missing` | Thiếu tài nguyên khác, ví dụ thư viện ảnh mẫu |

Lý do xin điều này: chúng tôi đã chạy một bài kiểm tra cảnh báo trên bench
**chưa cắm MHU**. Qauto báo `RESULT: Fail` với `WRONG CONTENT in Car Buddy`.
Người đọc kết quả từ xa sẽ hiểu là xe sai, trong khi thật ra là **không có gì
để nhìn**. Thông tin phân biệt hai chuyện nằm trong log từng bước, không nằm
trong verdict.

Từ chối trước khi chạy thì không sinh ra kết quả sai lệch nào cả.

---

## 4. Trả kết quả chạy

Kết quả đi **hai đường**, vì tóm tắt thì nhỏ còn bằng chứng thì lớn.

### 4a. Tóm tắt — MQTT

```
Topic : bench/<model>/<BenchId>/result      QoS 1, không retained
```

```json
{
  "cmd_id": "a1b2c3d4e5f6g7h8",
  "test_case": "Warning_VF8/[TC002-VF89FL-ID801] Verify warning.tc",
  "verdict": "pass",
  "duration_s": 11.3,
  "reason": null,
  "ts": 1758700045000
}
```

Gửi **một gói cho mỗi bài**, ngay khi bài đó xong — đừng gom lại cuối plan.
Người ở xa cần thấy tiến độ.

`verdict` nhận bốn giá trị: `pass`, `fail`, `warning`, `unknown`.

**`unknown` không phải `fail`.** Nếu Qauto ghi ra một trạng thái mà tài liệu
này chưa liệt kê, xin gửi `unknown` kèm chuỗi gốc trong `reason`, đừng quy về
`fail`. Chúng tôi chưa từng thấy một lượt trượt trong log nên chưa biết Qauto
ghi chữ gì khi hỏng.

### 4b. Bằng chứng chi tiết — REST

```
POST http://<Server>:5000/api/runs/<cmd_id>/report
Content-Type: multipart/form-data
```

| Trường | Nội dung |
| --- | --- |
| `test_case` | Tên bài, khớp với gói tóm tắt |
| `file` | Một hoặc nhiều file. Nhận nhiều lần cùng tên trường |

Xin gửi những file Qauto vốn đã sinh ra cho mỗi lượt chạy:

```
Output/OutputLog/<...>/<TenTestCase>/<...>/
    TestCaseLog.txt      ~35 KB
    CAN1.txt             2 MB cho lượt 40 giây, hàng chục MB cho lượt 20 phút
    Can_<...>.blf
```

**Vì sao không đi MQTT:** `CAN1.txt` một lượt 40 giây đã 2 MB, mà chúng tôi đo
được gói MQTT 2 MB là tắc. Lượt dài 20 phút sinh 828.726 frame thì còn lớn hơn
nhiều.

Gửi được lúc nào thì gửi, **không cần đúng lúc bài vừa xong**. Mạng đứt thì
gửi lại sau; Console chống trùng theo `cmd_id` và tên bài, nên gửi lại hai lần
không sao.

Endpoint này Console **chưa làm xong** — sẽ báo khi sẵn sàng. Mục 4a thì đã
chạy được ngay.

---

## Tóm tắt những gì cần ở Qauto

| Việc | Đường |
| --- | --- |
| Một ô `Server` + một ô `BenchId` trong Settings | — |
| Gửi `status` khi đổi và mỗi 30 giây, kèm Last Will | MQTT, retained |
| Nghe `bench/+/<BenchId>/cmd`, trả `ack` ngay | MQTT |
| Tải gói, kiểm sha256, giải nén, chạy theo danh sách | HTTP + tại chỗ |
| Gửi `result` từng bài | MQTT |
| Upload log và trace CAN | HTTP multipart |

Mọi câu hỏi xin liên hệ nhóm Bench Console. Chúng tôi có sẵn một agent Python
chạy đúng giao thức này để đội Qauto đối chiếu khi cần.
