// ============================================================================
// components/ui-states.js — Empty state / Loading (skeleton) / Error state
// dùng cho các khối không phải bảng (card, list, dashboard...).
// ============================================================================
import { icon } from '../icons.js';

// CHỮ: tiêu đề/mô tả MẶC ĐỊNH khi 1 khối trống dữ liệu — nơi gọi thường tự truyền
// title/desc riêng (vd: trong js/pages/*.js), chỉ đổi ở đây nếu muốn đổi giá trị mặc định chung.
export function emptyState({ icon: iconName = 'inbox', title = 'No data', desc = '', action = '' }) {
    return `<div class="empty-state">${icon(iconName)}<div class="empty-title">${title}</div>${desc ? `<div class="empty-desc">${desc}</div>` : ''}${action}</div>`;
}

// CHỮ: tiêu đề/mô tả/nhãn nút MẶC ĐỊNH khi 1 khối bị lỗi tải dữ liệu
export function errorState({ title = 'Something went wrong', desc = 'Could not load data (mock). Please try again.', retryLabel = 'Retry' } = {}) {
    return `<div class="error-state">${icon('circle-alert')}<div class="empty-title">${title}</div><div class="empty-desc">${desc}</div><button class="btn btn-outline btn-sm" data-retry>${retryLabel}</button></div>`;
}

export function skeletonBlock(lines = 3) {
    return `<div class="page-loading-row">${Array.from({ length: lines }).map((_, i) => `<div class="skeleton skeleton-line" style="width:${90 - i * 12}%"></div>`).join('')}</div>`;
}

export function skeletonCards(count = 4) {
    return `<div class="grid-cols-4">${Array.from({ length: count }).map(() => `
        <div class="stat-card">
            <div class="skeleton skeleton-line" style="width:60%;height:10px;"></div>
            <div class="skeleton skeleton-line" style="width:40%;height:24px;"></div>
        </div>`).join('')}</div>`;
}
