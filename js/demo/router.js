// ============================================================================
// demo/router.js — Hash router with path params (#/devices/BENCH-VF6-L1-02).
// Keeps the hash-based navigation of the original app so the demo still runs
// from a plain static server and survives a page refresh on any route.
// ============================================================================

export const ROUTES = [
    { pattern: '/dashboard', nav: 'dashboard', title: 'Dashboard', crumb: 'Console / Dashboard', page: () => import('./pages/dashboard.js') },
    { pattern: '/devices', nav: 'devices', title: 'Devices', crumb: 'Console / Devices', parent: '/dashboard', parentLabel: 'Dashboard', page: () => import('./pages/devices.js') },
    { pattern: '/devices/:id/session', nav: 'devices', title: 'Bench Session', crumb: 'Console / Devices / :id / Session', parent: '/devices', parentLabel: 'Devices', page: () => import('./pages/bench-session.js') },
    { pattern: '/devices/:id', nav: 'devices', title: 'Device Detail', crumb: 'Console / Devices / :id', parent: '/devices', parentLabel: 'Devices', page: () => import('./pages/device-detail.js') },
    { pattern: '/testcases', nav: 'testcases', title: 'Test Cases', crumb: 'Test Management / Test Cases', parent: '/dashboard', parentLabel: 'Dashboard', page: () => import('./pages/testcases.js') },
    { pattern: '/requests/new', nav: 'devices', title: 'Book Test', crumb: 'Console / Devices / Book test', parent: '/devices', parentLabel: 'Devices', page: () => import('./pages/request-new.js') },
    { pattern: '/runs/:id/live', nav: 'runs', title: 'Test Run', crumb: 'Test History / :id', parent: '/runs', parentLabel: 'Test History', page: () => import('./pages/run-result.js') },
    { pattern: '/runs/:id/result', nav: 'runs', title: 'Test Run', crumb: 'Test History / :id', parent: '/runs', parentLabel: 'Test History', page: () => import('./pages/run-result.js') },
    { pattern: '/runs', nav: 'runs', title: 'Test History', crumb: 'Test History', parent: '/dashboard', parentLabel: 'Dashboard', page: () => import('./pages/runs.js') },
    { pattern: '/users', nav: 'users', title: 'Users', crumb: 'Administration / Users', parent: '/dashboard', parentLabel: 'Dashboard', page: () => import('./pages/users.js') },
];

const DEFAULT_PATH = '/dashboard';

function matchRoute(path) {
    for (const route of ROUTES) {
        const rp = route.pattern.split('/').filter(Boolean);
        const pp = path.split('/').filter(Boolean);
        if (rp.length !== pp.length) continue;
        const params = {};
        let ok = true;
        for (let i = 0; i < rp.length; i += 1) {
            if (rp[i].startsWith(':')) params[rp[i].slice(1)] = decodeURIComponent(pp[i]);
            else if (rp[i].toLowerCase() !== pp[i].toLowerCase()) { ok = false; break; }
        }
        if (ok) return { route, params };
    }
    return null;
}

export function parseHash() {
    const raw = location.hash.replace(/^#/, '') || DEFAULT_PATH;
    const [path, qs] = raw.split('?');
    const query = {};
    if (qs) new URLSearchParams(qs).forEach((v, k) => { query[k] = v; });
    const found = matchRoute(path || DEFAULT_PATH) || matchRoute(DEFAULT_PATH);
    return { ...found, path, query };
}

export function navigate(path, query = {}) {
    const qs = new URLSearchParams(query).toString();
    location.hash = `#${path}${qs ? `?${qs}` : ''}`;
}

export function startRouter(onRoute) {
    window.addEventListener('hashchange', () => onRoute(parseHash()));
    if (!location.hash) location.replace(`#${DEFAULT_PATH}`);
    onRoute(parseHash());
}

/** Breadcrumb text with :id replaced by the real id. */
export function crumbFor(route, params) {
    return route.crumb.replace(':id', params.id || '');
}
