// ============================================================================
// components/badge.js — Badge trạng thái, luôn kết hợp màu + icon + text
// (không dựa màu đơn thuần) để đáp ứng yêu cầu accessibility/visual style.
// ============================================================================
import { icon } from '../icons.js';

// ---- CHỮ: nhãn hiển thị trên các Badge (nhãn trạng thái có màu) khắp app ----
// Sửa "label" bên dưới để đổi chữ hiển thị trên badge — không đổi "cls"/"icon" nếu
// không muốn đổi màu/icon đi kèm. 3 nhóm dưới đây tương ứng với 3 loại badge:
// trạng thái Device, trạng thái Execution, và kết quả Tool (PASS/FAIL...).
export const DEVICE_STATUS_META = {
    // Real-API connection statuses (heartbeat-based). "hint" = tooltip: đây là trạng thái kết nối
    // của Bench Agent tới server, KHÔNG phải tình trạng MHU/Tool/CAN/ECU.
    ONLINE:      { label: 'Online',      cls: 'badge-green',  icon: 'circle-check', hint: 'Bench Agent heartbeat received within the last 15 s. Agent connection only — not MHU/Tool/CAN health.' },
    OFFLINE:     { label: 'Offline',     cls: 'badge-gray',   icon: 'circle-slash', hint: 'No Bench Agent heartbeat in the last 15 s (or never). Agent connection only.' },
    INACTIVE:    { label: 'Inactive',    cls: 'badge-orange', icon: 'circle-slash', hint: 'Deactivated manually. Heartbeats are rejected until it is activated again.' },
    // Mock statuses kept for mock-api flow
    AVAILABLE:   { label: 'Available',   cls: 'badge-green',  icon: 'circle-check' },
    BUSY:        { label: 'Busy',        cls: 'badge-blue',   icon: 'loader' },
    MAINTENANCE: { label: 'Maintenance', cls: 'badge-orange', icon: 'wrench' },
    ERROR:       { label: 'Error',       cls: 'badge-red',    icon: 'circle-x' },
};

export const EXECUTION_STATUS_META = {
    QUEUED:     { label: 'Queued',     cls: 'badge-amber',  icon: 'clock' },
    RUNNING:    { label: 'Running',    cls: 'badge-blue',   icon: 'loader' },
    COMPLETED:  { label: 'Completed',  cls: 'badge-green',  icon: 'circle-check' },
    CANCELLING: { label: 'Cancelling', cls: 'badge-orange', icon: 'loader' },
    CANCELLED:  { label: 'Cancelled',  cls: 'badge-gray',   icon: 'circle-slash' },
    ERROR:      { label: 'Error',      cls: 'badge-red',    icon: 'circle-x' },
};

export const TOOL_RESULT_META = {
    PASS:         { label: 'PASS',         cls: 'badge-green',  icon: 'circle-check' },
    FAIL:         { label: 'FAIL',         cls: 'badge-red',    icon: 'circle-x' },
    SKIPPED:      { label: 'Skipped',      cls: 'badge-purple', icon: 'skip-forward' },
    INCONCLUSIVE: { label: 'Inconclusive', cls: 'badge-purple', icon: 'help-circle' },
};

export function renderBadge(label, cls, iconName) {
    return `<span class="badge ${cls}">${iconName ? icon(iconName) : ''}${label}</span>`;
}

export function deviceStatusBadge(status) {
    const m = DEVICE_STATUS_META[status] || DEVICE_STATUS_META.OFFLINE;
    const badge = renderBadge(m.label, m.cls, m.icon);
    return m.hint ? `<span title="${m.hint}">${badge}</span>` : badge;
}
export function executionStatusBadge(status) {
    const m = EXECUTION_STATUS_META[status] || EXECUTION_STATUS_META.QUEUED;
    return renderBadge(m.label, m.cls, m.icon);
}
export function toolResultBadge(result) {
    if (!result) return `<span class="badge badge-outline">${icon('minus')}None yet</span>`; // CHỮ: badge khi chưa có kết quả Tool
    const m = TOOL_RESULT_META[result] || TOOL_RESULT_META.INCONCLUSIVE;
    return renderBadge(m.label, m.cls, m.icon);
}
export function activeBadge(active) {
    return active
        ? renderBadge('Active', 'badge-green', 'circle-check')
        : renderBadge('Inactive', 'badge-gray', 'circle-slash');
}
export function roleBadge(role) {
    return role === 'ADMIN' ? renderBadge('ADMIN', 'badge-blue', 'shield') : renderBadge('EMPLOYEE', 'badge-outline', 'user');
}
