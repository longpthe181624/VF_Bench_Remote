// ============================================================================
// utils.js — Hàm tiện ích dùng chung: format ngày giờ, dung lượng, nhãn enum.
// ============================================================================

export function formatDateTime(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    return d.toLocaleString('en-GB', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export function relativeTime(iso) {
    if (!iso) return '—';
    const diffMs = Date.now() - new Date(iso).getTime();
    const sec = Math.round(diffMs / 1000);
    if (sec < 5) return 'just now';
    if (sec < 60) return `${sec}s ago`;
    const min = Math.round(sec / 60);
    if (min < 60) return `${min}m ago`;
    const hr = Math.round(min / 60);
    if (hr < 24) return `${hr}h ago`;
    const day = Math.round(hr / 24);
    return `${day}d ago`;
}

export function formatBytes(bytes) {
    if (bytes == null) return '—';
    if (bytes < 1024) return `${bytes} B`;
    const kb = bytes / 1024;
    if (kb < 1024) return `${kb.toFixed(0)} KB`;
    const mb = kb / 1024;
    if (mb < 1024) return `${mb.toFixed(1)} MB`;
    return `${(mb / 1024).toFixed(2)} GB`;
}

export function formatDuration(sec) {
    if (sec == null) return '—';
    if (sec < 60) return `${sec}s`;
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    if (m < 60) return `${m}m ${s}s`;
    const h = Math.floor(m / 60);
    return `${h}h ${m % 60}m`;
}

// CHỮ: theo yêu cầu chỉnh lại ngày 2026-09-18 ("Bỏ hẳn Vehicle khỏi Devices"), Device Type gồm
// ĐÚNG 2 loại: Bench/MHU (MHU ở đây là 1 THIẾT BỊ độc lập, đăng ký/quản lý riêng trên trang
// Devices — khác với "MHU" khi nó chỉ là 1 Part Type nằm bên trong cấu hình của 1 Bench, xem
// PART_TYPES ở mock-data.js).
export const DEVICE_TYPE_LABELS = { FULL_BENCH: 'Bench', MHU: 'MHU' };
export function deviceTypeLabel(type) { return DEVICE_TYPE_LABELS[type] || type; }

export const OUTPUT_TYPE_META = {
    CAN_LOG: { label: 'CAN Log', icon: 'file-text' },
    MHU_LOG: { label: 'MHU Log', icon: 'file-text' },
    TOOL_LOG: { label: 'Tool Log', icon: 'file-text' },
    TOOL_REPORT: { label: 'Tool Report', icon: 'file-chart-column' },
    SCREENSHOT: { label: 'Screenshot', icon: 'image' },
    VIDEO: { label: 'Video', icon: 'video' },
    OTHER: { label: 'Other', icon: 'file' },
};

export function truncateMiddle(str, max = 18) {
    if (!str || str.length <= max) return str;
    const half = Math.floor((max - 3) / 2);
    return str.slice(0, half) + '...' + str.slice(str.length - half);
}

export function downloadBlob(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 2000);
}

export function escapeHtml(str) {
    return String(str ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
