// ============================================================================
// components/dropdown.js — Dropdown menu tổng quát (user menu, row actions...).
// Markup quy ước:
//   <div class="dropdown">
//     <button data-dropdown-trigger>...</button>
//     <div class="dropdown-menu" hidden>...</div>
//   </div>
// Chỉ cần initGlobalDropdowns() MỘT LẦN lúc bootstrap — dùng event delegation
// nên không cần bind lại mỗi khi re-render danh sách/table.
// ============================================================================

let initialized = false;

export function initGlobalDropdowns() {
    if (initialized) return;
    initialized = true;

    document.addEventListener('click', (e) => {
        const trigger = e.target.closest('[data-dropdown-trigger]');
        if (trigger) {
            const wrap = trigger.closest('.dropdown');
            const menu = wrap?.querySelector('.dropdown-menu');
            const willOpen = menu?.hasAttribute('hidden');
            closeAllDropdowns();
            if (willOpen && menu) menu.removeAttribute('hidden');
            e.stopPropagation();
            return;
        }
        // click bên trong menu (vào 1 item) -> tự đóng sau khi xử lý xong ở nơi khác
        if (e.target.closest('.dropdown-menu')) return;
        closeAllDropdowns();
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') closeAllDropdowns();
    });
}

export function closeAllDropdowns() {
    document.querySelectorAll('.dropdown-menu').forEach((m) => m.setAttribute('hidden', ''));
}
