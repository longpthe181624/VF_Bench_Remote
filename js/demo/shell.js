// ============================================================================
// demo/shell.js — Sidebar + topbar shared by every demo screen.
// Same markup and classes as the original app shell so the look is identical.
// ============================================================================
import { icon, renderIcons } from '../icons.js';
import { navigate } from './router.js';
import { confirmDialog } from '../components/modal.js';
import { DEMO_USER } from './data.js';
import { bellHtml, bindBell } from './reminders.js';
import { getCurrentUser, hasPermission, logout } from './api-client.js';

const NAV = [
    {
        section: 'Console',
        items: [
            { nav: 'dashboard', label: 'Dashboard', icon: 'layout-dashboard', path: '/dashboard' },
            { nav: 'devices', label: 'Devices', icon: 'cpu', path: '/devices' },
        ],
    },
    {
        section: 'Test Management',
        items: [
            { nav: 'testcases', label: 'Test Cases', icon: 'file-code', path: '/testcases' },
            { nav: 'runs', label: 'Test History', icon: 'history', path: '/runs' },
        ],
    },
    {
        section: 'Administration',
        permission: 'USER.VIEW',
        items: [
            { nav: 'users', label: 'Users', icon: 'users', path: '/users' },
        ],
    },
];

function signedInUser() {
    const user = getCurrentUser();
    if (!user) return DEMO_USER;
    const words = String(user.hoTen || user.email || '?').trim().split(/\s+/);
    return {
        name: user.hoTen || user.email,
        role: (user.vaiTro || []).join(', ') || 'User',
        initials: words.slice(-2).map((x) => x[0]).join('').toUpperCase(),
    };
}

function navItem(nav) {
    for (const sec of NAV) {
        const it = sec.items.find((x) => x.nav === nav);
        if (it) return it;
    }
    return null;
}

/**
 * Topbar breadcrumb: the sidebar label alone on a sidebar screen, otherwise
 * the sidebar label followed by the sub-page part of the route crumb
 * (e.g. "Devices / BENCH-VF6-L1-02", "Test History / RUN-… / Live").
 */
export function headerCrumb(route, crumb) {
    const it = navItem(route.nav);
    if (!it) return crumb;
    if (route.pattern === it.path) return it.label;
    const parts = String(crumb).split(' / ');
    const at = parts.indexOf(it.label);
    return [it.label, ...parts.slice(at + 1)].join(' / ');
}

export { DEMO_USER };

export function mountSidebar(activeNav) {
    const el = document.getElementById('sidebar');
    const user = signedInUser();
    const sections = NAV.filter((sec) => !sec.permission || hasPermission(sec.permission)).map((sec) => {
        const items = sec.items.map((it) => `
            <button class="sidebar-item ${it.nav === activeNav ? 'active' : ''}" data-path="${it.path}">
                ${icon(it.icon)}<span class="label">${it.label}</span>
            </button>`).join('');
        return `<div class="sidebar-section-title">${sec.section}</div>${items}`;
    }).join('');

    el.innerHTML = `
        <div class="sidebar-head">
            <div class="logo-mark"><img src="img/vinfast-logo.jpg" alt="VinFast" class="logo-mark-img"></div>
            <div class="logo-text">Bench Console<small>Remote Testing Web</small></div>
        </div>
        <nav class="sidebar-nav">${sections}</nav>
        <div class="sidebar-user">
            <span class="user-avatar">${user.initials}</span>
            <span class="sidebar-user-info">
                <span class="sidebar-user-name">${user.name}</span>
                <span class="sidebar-user-role">${user.role}</span>
            </span>
        </div>
        <div class="sidebar-foot">
            <button class="btn btn-ghost btn-sm sidebar-collapse-btn" id="btn-sign-out">
                ${icon('log-out')}<span class="label">Sign out</span>
            </button>
        </div>`;
    renderIcons();

    el.querySelectorAll('[data-path]').forEach((btn) => {
        btn.addEventListener('click', () => navigate(btn.getAttribute('data-path')));
    });
    el.querySelector('#btn-sign-out').addEventListener('click', async () => {
        const ok = await confirmDialog({
            title: 'Sign out?',
            description: 'You will need to enter your account credentials again.',
            confirmText: 'Sign out', variant: 'danger',
        });
        if (!ok) return;
        logout();
        location.reload();
    });
}

// Sidebar screens show only their name. Sub-screens (detail, new, live...)
// show "Sidebar name / sub-page" plus a Back button to the parent screen.
export function mountHeader(breadcrumb, back = null) {
    const el = document.getElementById('header');
    const user = signedInUser();
    const parts = String(breadcrumb).split(' / ').filter(Boolean);
    const crumbHtml = parts.map((p, i) => (i === parts.length - 1
        ? `<span class="crumb-current">${p}</span>`
        : `<span>${p}</span>`)).join('<span class="crumb-sep">/</span>');

    el.innerHTML = `
        <div class="header-left">
            <button class="btn btn-ghost btn-icon header-hamburger" id="btn-hamburger" aria-label="Open navigation menu">${icon('menu')}</button>
            ${back ? `<button class="btn btn-outline btn-sm back-btn" id="btn-back-nav" title="Back to ${back.label}">${icon('arrow-left')}<span class="back-label">Back</span></button>` : ''}
            <div class="header-breadcrumb">${crumbHtml}</div>
        </div>
        <div class="header-right">
            ${bellHtml()}
            <div class="user-menu-trigger" style="cursor:default">
                <span class="user-avatar">${user.initials}</span>
                <span class="user-menu-info">
                    <span class="user-name">${user.name}</span>
                    <span class="user-role">${user.role}</span>
                </span>
            </div>
        </div>`;
    renderIcons();
    bindBell(el, navigate);

    // Back goes one step in the browser history when the demo has one, otherwise
    // it falls back to the parent screen of the current route.
    el.querySelector('#btn-back-nav')?.addEventListener('click', () => {
        if (window.__demoNavDepth > 0) { window.history.back(); return; }
        navigate(back.path);
    });

    el.querySelector('#btn-hamburger').addEventListener('click', () => {
        document.getElementById('sidebar').classList.toggle('is-open');
        const backdrop = document.getElementById('sidebar-backdrop');
        backdrop.hidden = !document.getElementById('sidebar').classList.contains('is-open');
        backdrop.onclick = () => {
            document.getElementById('sidebar').classList.remove('is-open');
            backdrop.hidden = true;
        };
    });
}
