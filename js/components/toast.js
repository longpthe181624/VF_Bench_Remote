// ============================================================================
// components/toast.js — Toast notification dùng chung toàn app.
// ============================================================================
import { icon, renderIcons } from '../icons.js';

const ICONS = { success: 'circle-check', error: 'circle-x', warning: 'triangle-alert', info: 'info' };

export function showToast({ title, description = '', variant = 'info', duration = 4200 }) {
    const host = document.getElementById('toast-host');
    if (!host) return;
    const el = document.createElement('div');
    el.className = `toast variant-${variant}`;
    el.setAttribute('role', 'status');
    // KHỐI: 1 thông báo toast (góc màn hình) — "title"/"description" do nơi gọi showToast({...})
    // truyền vào, KHÔNG sửa ở đây. Tìm showToast({ title: '...', ... }) trong từng trang để sửa chữ.
    el.innerHTML = `
        <span class="toast-icon">${icon(ICONS[variant] || 'info')}</span>
        <div>
            <div class="toast-title">${title}</div>
            ${description ? `<div class="toast-desc">${description}</div>` : ''}
        </div>
        <button class="toast-close" aria-label="Close notification">${icon('x')}</button>
    `;
    const remove = () => { el.remove(); };
    el.querySelector('.toast-close').addEventListener('click', remove);
    host.appendChild(el);
    renderIcons();
    if (duration > 0) setTimeout(remove, duration);
}
