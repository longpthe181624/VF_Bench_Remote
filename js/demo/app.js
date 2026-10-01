// ============================================================================
// demo/app.js — Entry point of the interactive demo.
// Boots the store, mounts the shared shell and drives the hash router.
// No backend: every screen reads and writes through js/demo/store.js.
// ============================================================================
import { initStore, applyDeviceUpdate, refreshDevices } from './store.js';
import { startRouter, parseHash, navigate, crumbFor } from './router.js';
import { mountSidebar, mountHeader, headerCrumb } from './shell.js';
import { startReminders } from './reminders.js';
import { initGlobalDropdowns } from '../components/dropdown.js';
import { renderIcons } from '../icons.js';
import { skeletonBlock } from '../components/ui-states.js';
import { getCurrentUser, restoreSession } from './api-client.js';
import { mountLogin } from './auth-view.js';
import { startRealtime } from './realtime.js';

initGlobalDropdowns();

const authView = document.getElementById('auth-view');
const shellView = document.getElementById('shell-view');
const content = document.getElementById('app-content');

authView.hidden = true;
shellView.hidden = true;

let unmountCurrent = null;
let navToken = 0;

// How many in-app navigations happened since the page was loaded. The Back
// button uses history.back() only when there is somewhere to go back to.
window.__demoNavDepth = 0;
window.addEventListener('hashchange', () => { window.__demoNavDepth += 1; });

async function handleRoute({ route, params, query }) {
    const token = ++navToken;

    mountSidebar(route.nav);
    const crumb = headerCrumb(route, crumbFor(route, params));
    const isSubPage = crumb.includes(' / ');
    mountHeader(crumb, isSubPage ? { path: route.parent, label: route.parentLabel || 'Back' } : null);
    document.getElementById('sidebar').classList.remove('is-open');
    document.getElementById('sidebar-backdrop').hidden = true;

    if (typeof unmountCurrent === 'function') {
        try { unmountCurrent(); } catch (e) { console.warn('cleanup failed', e); }
    }
    unmountCurrent = null;

    content.innerHTML = `<div class="section-gap">${skeletonBlock(4)}</div>`;
    try {
        const mod = await route.page();
        if (token !== navToken) return;                       // a newer navigation won
        const ctx = { params, query, navigate };
        const cleanup = await mod.mount(content, ctx);
        if (token !== navToken) {
            if (typeof cleanup === 'function') { try { cleanup(); } catch (e) { /* no-op */ } }
            return;
        }
        if (typeof cleanup === 'function') unmountCurrent = cleanup;
    } catch (e) {
        if (token !== navToken) return;
        console.error('Could not mount page', route.pattern, e);
        content.innerHTML = `<div class="error-state">This demo screen could not be loaded. Open the browser console for details.</div>`;
    }
    if (token !== navToken) return;
    renderIcons();
    window.scrollTo({ top: 0 });
}

let appStarted = false;

async function enterApp() {
    authView.hidden = true;
    shellView.hidden = false;
    content.innerHTML = `<div class="section-gap">${skeletonBlock(4)}</div>`;
    await initStore();
    try {
        await startRealtime({
            onBenchUpdated: (device) => applyDeviceUpdate(device),
            onRunFinished: () => refreshDevices().catch(() => {}),
            onCommandUpdated: () => refreshDevices().catch(() => {}),
        });
    } catch (e) {
        console.warn('SignalR is not available; REST data still works.', e);
    }
    if (!appStarted) {
        appStarted = true;
        startRouter(handleRoute);
        startReminders(navigate);
    } else {
        handleRoute(parseHash());
    }
}

async function boot() {
    const user = getCurrentUser() || await restoreSession();
    if (user) { await enterApp(); return; }
    shellView.hidden = true;
    authView.hidden = false;
    mountLogin(authView, { onAuthenticated: enterApp });
}

boot().catch((e) => {
    console.error('Application startup failed', e);
    shellView.hidden = true;
    authView.hidden = false;
    mountLogin(authView, { onAuthenticated: enterApp });
});

// Re-render the current screen on demand.
export function refreshCurrentRoute() {
    handleRoute(parseHash());
}
window.__demoRefresh = refreshCurrentRoute;
