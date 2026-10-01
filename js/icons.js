// ============================================================================
// icons.js — Helper mỏng bọc quanh Lucide (UMD, load qua CDN trong index.html).
// Dùng thay cho emoji: <i data-lucide="check"></i> rồi gọi renderIcons().
// ============================================================================

export function icon(name, cls = '') {
    return `<i data-lucide="${name}" class="icon ${cls}"></i>`;
}

/** Gọi sau mỗi lần innerHTML thay đổi để Lucide "hydrate" các <i data-lucide>. */
export function renderIcons() {
    if (window.lucide && typeof window.lucide.createIcons === 'function') {
        try { window.lucide.createIcons(); } catch (e) { /* no-op nếu lucide chưa kịp load */ }
    }
}
