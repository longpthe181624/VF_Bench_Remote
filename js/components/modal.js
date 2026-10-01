// ============================================================================
// components/modal.js — Dialog/Modal dùng chung + Confirm/Alert dialog dựng
// trên cùng nền tảng modal.
// ============================================================================
import { icon, renderIcons } from '../icons.js';

let activeHandle = null;

/**
 * Mở một modal.
 * @param {{title:string, bodyHtml:string, footerHtml?:string, size?:'sm'|'md'|'lg', onOpen?:(root:HTMLElement)=>void, onClose?:()=>void, closeOnOverlay?:boolean}} opts
 * @returns {{root:HTMLElement, close:()=>void}}
 */
export function openModal(opts) {
    closeModal(); // chỉ cho phép 1 modal tại 1 thời điểm để tránh chồng lớp
    const host = document.getElementById('modal-host');
    const overlay = document.createElement('div');
    overlay.className = 'modal-overlay';
    overlay.innerHTML = `
        <div class="modal-box ${opts.size ? 'size-' + opts.size : ''}" role="dialog" aria-modal="true" aria-label="${opts.title || ''}">
            <!-- KHỐI: tiêu đề modal — chữ do tham số "title" truyền vào openModal(...) ở nơi gọi quyết định -->
            <div class="modal-header">
                <div class="modal-title">${opts.title || ''}</div>
                <button class="modal-close-btn" data-close aria-label="Close">${icon('x')}</button>
            </div>
            <!-- KHỐI: nội dung modal — chữ do tham số "bodyHtml" truyền vào openModal(...) ở nơi gọi quyết định -->
            <div class="modal-body">${opts.bodyHtml || ''}</div>
            <!-- THANH: dải nút cuối modal — chữ do tham số "footerHtml" truyền vào openModal(...) ở nơi gọi quyết định -->
            ${opts.footerHtml ? `<div class="modal-footer">${opts.footerHtml}</div>` : ''}
        </div>
    `;
    host.appendChild(overlay);
    renderIcons();

    function doClose() {
        overlay.removeEventListener('keydown', onKeydown);
        overlay.remove();
        if (activeHandle && activeHandle.root === overlay) activeHandle = null;
        if (opts.onClose) opts.onClose();
    }
    function onKeydown(e) { if (e.key === 'Escape') doClose(); }

    overlay.addEventListener('keydown', onKeydown);
    // Mọi nút [data-close] đều đóng modal: dấu × ở header lẫn nút Cancel ở footer.
    overlay.querySelectorAll('[data-close]').forEach((el) => el.addEventListener('click', doClose));
    if (opts.closeOnOverlay !== false) {
        overlay.addEventListener('click', (e) => { if (e.target === overlay) doClose(); });
    }

    const handle = { root: overlay, close: doClose };
    activeHandle = handle;
    // Đưa focus vào modal để hỗ trợ điều hướng bàn phím & Escape hoạt động ngay.
    overlay.setAttribute('tabindex', '-1');
    overlay.focus();
    if (opts.onOpen) opts.onOpen(overlay);
    return handle;
}

export function closeModal() {
    if (activeHandle) activeHandle.close();
}

/**
 * Confirm dialog (dùng lại cho cả Alert Dialog cảnh báo hành động nhạy cảm).
 * @returns {Promise<boolean>}
 */
// ---- CHỮ: nhãn nút mặc định của hộp thoại Xác nhận (Confirm/Alert Dialog) ----
// confirmText/cancelText bên dưới là giá trị MẶC ĐỊNH dùng khi nơi gọi confirmDialog(...)
// không tự truyền chữ riêng. Đa số nơi gọi trong app (Stop execution, Deactivate...)
// đã tự truyền confirmText/title/description riêng — tìm confirmDialog({ ... }) trong
// từng trang (vd: js/pages/execution-detail.js, js/pages/testplans.js) để sửa đúng chỗ.
export function confirmDialog({ title, description, confirmText = 'Confirm', cancelText = 'Cancel', variant = 'default' }) {
    return new Promise((resolve) => {
        let settled = false;
        const finish = (val) => { if (settled) return; settled = true; resolve(val); };
        const handle = openModal({
            title,
            size: 'sm',
            bodyHtml: `
                <div class="alert-dialog-icon-row">
                    <div class="alert-dialog-icon ${variant === 'danger' ? '' : 'warning'}">
                        ${icon(variant === 'danger' ? 'trash-2' : 'triangle-alert')}
                    </div>
                    <div class="text-muted text-sm">${description || ''}</div>
                </div>`,
            footerHtml: `
                <button class="btn btn-outline" data-cancel>${cancelText}</button>
                <button class="btn ${variant === 'danger' ? 'btn-danger' : 'btn-primary'}" data-confirm>${confirmText}</button>
            `,
            onClose: () => finish(false),
        });
        handle.root.querySelector('[data-cancel]').addEventListener('click', () => { finish(false); handle.close(); });
        handle.root.querySelector('[data-confirm]').addEventListener('click', () => { finish(true); handle.close(); });
    });
}
