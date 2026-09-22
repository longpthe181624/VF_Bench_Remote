Bench Console — internal VinFast tool for running vehicle test benches over MQTT. Vietnamese UI: keep strings verbatim, never translate. 1440×900 frames, light + dark. Near-monochrome; colour ONLY on a 5.5px status dot and error text. 1px borders, no shadows, dense and calm. No gradients, no status pills, no KPI stat row, no charts on list screens.

TYPE: Archivo (fallback for Aptos), tabular figures, line-height 1.45. H1 20/700 · metric 25/700 · card title 13/700 · nav 13/500 (active 600) · body 12.5/500 · subtitle 12 · note 11 · timestamp 10.5 · meta 10 · tag 9.5.

LIGHT: bg #fafafa · card #fff (dim #fcfcfc) · border #e9e9e9 (hover #d4d4d4 + shadow 0 1px 3px rgba(0,0,0,.05)) · divider #f0f0f0 · active fill #eeeeee · text #0a0a0a/#525252/#737373/#a1a1a1/#b4b4b4/#c4c4c4/#d4d4d4 · ready #16a34a · running #0a0a0a · error dot #dc2626 text #b91c1c · offline hollow dot ring #c4c4c4 text #a1a1a1 · badge #92400e on #fef3c7 · primary btn #0a0a0a/#fff.

DARK: bg #0a0a0a · card #141414 (dim #111111, hover #181818/#333333) · border/active #1f1f1f · text #fafafa/#d4d4d4/#a1a1a1/#737373/#5c5c5c/#4d4d4d/#3d3d3d · ready #22c55e · running #fafafa · error #ef4444/#f87171 · ring #525252 · badge #fbbf24 on #2a2008 · primary btn #fafafa/#0a0a0a.

RADIUS card 10, button 7–8, tag 4–5. SPACING sidebar 236w pad 16/12/12 gap 15 · topbar 56h pad 28 · body pad 22/28/0 gap 16 · grid gap 13 · card pad 12/14/10.

SHELL (all but login): Sidebar 236px — "Bench Console" 14/700 + "v0.4.2" + badge "staging", no logo · search "Tìm bench, test case…" + "⌘K" · "Giám sát": Bench 37, Phiên đang chạy 9, Cảnh báo 3 (error colour) · "Cấu hình": Test Case 148, Test Plan 6 · "Báo cáo": Lịch sử, Thống kê · avatar "PL" + "Phạm Long" + "Kỹ sư bench · Xưởng 2" above a bottom rule, with "Broker hn-prod-01 · đồng bộ 10:41:26" over it. Nav row = icon + label + spacer + count; active = fill + 600.
Topbar — breadcrumb (parent subtle + "/", current 600); right "● MQTT ổn định · trễ 82 ms" + primary "+ Test mới". Header — H1 + muted subtitle, right secondary buttons. Filters — tab chips "Nhãn <count>" (pad 5/11, radius 7, active fill + 600); right "Hiển thị 15 / 37" + "Sắp xếp: trạng thái ⌄".

BENCH CARD 152px, 3-col grid: name 13/700 + model tag + spacer + dot & status 11/500 (hovered card adds "···") / meta 10 faint "Xưởng 2 · Rack B4 · FW 2.14.1" / spacer / value 25/700 + unit 12 + channel 10 ghost, e.g. 34.24 °C T_chamber, no data = "—" / note 11 muted, one line, ellipsis, error colour on the failing card / rule, footer: timestamp + optional "· T. Anh" + spacer + "Chi tiết" 11/600.

CONTENT RULES — what stops it looking generated:
IDs non-sequential, by rig type: HIL-A01/A02/A04/A05/A07/A09, HIL-C03/C07/C11, EOL-B01/B02/B04/B07/B09/B11.
Unbalanced: of 37 benches, 3 need attention, 9 running, 25 idle. Mixed precision: 36.8 · 34.24 · 31.7 · 1.62 · 1.21.
Times uneven, mixed: "4 giây trước", "34 giây trước", "2 phút trước", "41 phút trước", "từ 13/09".
Never repeat a status sentence — idle benches differ: "Rảnh từ 09:12 · chưa gán test plan" / "Chờ tới lượt trong TP-2026-011" / "FW cũ hơn bản chuẩn 2.13.0".
Errors name the fault: "CAN timeout ở step 2/3 — sensor_door không phản hồi". Running cards cite the plan: "TP-2026-014 · Cảnh báo đèn phanh (2/3)".
Clip the grid at the bottom: row 4 whole, row 5 sliced. One hovered card per screen. Keep debris: firmware and rack per bench, version + env badge, broker host, one bench on maintenance "Tháo cảm biến hiệu chuẩn · Nguyễn V. Hải".

SCREENS, one frame each, light and dark:
1 Bench — 15 cards, problems→running→idle, HIL-A02 hovered; buttons "Xưởng 2 & 3 ⌄" and "+ Thêm bench"; tabs Tất cả 37 / Cần chú ý 3 / Đang chạy 9 / Sẵn sàng 25.
2 Thêm bench — 560px modal: Mã bench, Model, Xưởng, rack, MQTT topic prefix, Kênh cảm biến chips, "Kiểm tra kết nối", Huỷ / Thêm bench.
3 Chi tiết bench — left "Phiên hiện tại" (bước 2/3, 18.6s/30s, bar) + 2×3 metric tiles + monochrome 5-min line chart; right 380px "Nhật ký MQTT" ● LIVE, clipped.
4 Test Case — 8-col table, no vertical rules, Mã TC-0412, one row selected. 5 Test Plan — 6 wide cards, TP-2026-014 "Kiểm định lô VF6 tuần 38", bar "5/8 hoàn tất". 6 Tạo Test Plan — 3 cols, ⠿-draggable "Trình tự chạy", "Tổng ước tính: 22 phút 40 giây", toggles "Chạy song song" / "Dừng khi gặp FAIL". 7 Phiên đang chạy — live run table + "Hàng chờ" rows "Chờ bench trống". 8 Cảnh báo — tabs Đang mở 3 / Đã xác nhận 4 / Đã đóng 12, actions "Xác nhận" / "Mở bench". 9 Lịch sử — run table + 380px detail panel + "Tải report chi tiết (.pdf)", PASS green / FAIL red. 10 Thống kê — monochrome panels, no pie charts. 11 Đăng nhập — centred 400px card, SSO secondary + primary "Đăng nhập".

DO NOT: coloured buttons/pills/badges, gradients, shadows, KPI rows, sparklines on cards, sequential IDs, evenly-spaced values, repeated sentences, lists ending at the frame edge, emoji. Never translate the Vietnamese or "fix" mixed VN/EN terms (FW, CAN timeout, telemetry, broker, PASS/FAIL).
