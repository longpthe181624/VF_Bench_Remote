// ============================================================================
// demo/pages/dashboard.js — System overview. Keeps the KPI-card + table layout
// of the original Dashboard, fed by the demo store, and redraws itself when
// the store changes (a run ends, a device is registered, a booking is made).
// ============================================================================
import { icon, renderIcons } from '../../icons.js';
import { escapeHtml } from '../../utils.js';
import { getDashboardStats, getDevices, getDevice, getRuns, getLibraryTestCases, onStoreChange } from '../store.js';
import { deviceBadges } from '../ui.js';
import { historyRowsHtml } from '../run-view.js';

// Order and short labels of the categories under the Test Cases count.
const TC_SUMMARY = [['Warning message', 'Warning'], ['Vivi', 'Vivi'], ['Disable warning', 'Disable'], ['Other', 'Other']];

export async function mount(container, ctx) {
    let lastHtml = '';
    const render = () => {
        const s = getDashboardStats();
        // The test case library (Test Cases page), counted by category.
        const tcFiles = getLibraryTestCases();
        // The five runs that finished last (a run still going counts from its start).
        const history = getRuns()
            .map((r) => ({ run: r, device: getDevice(r.targetId) || { id: r.targetId, name: r.targetId } }))
            .sort((a, b) => new Date(b.run.finishedAt || b.run.startedAt) - new Date(a.run.finishedAt || a.run.startedAt))
            .slice(0, 5);
        const benches = getDevices({ kind: 'FULL_BENCH' });
        const runs = getRuns();

        const kpi = (ic, label, value, sub, color) => `
            <div class="kpi-card">
                <div class="kpi-card-label">${icon(ic)}<span>${label}</span></div>
                <div class="kpi-card-value" ${color ? `style="color:${color}"` : ''}>${value}</div>
                <div class="kpi-card-sub">${sub}</div>
            </div>`;

        const html = `
            <div class="page-header">
                <div>
                    <h2>${icon('layout-dashboard')} Dashboard</h2>
                    <p class="page-subtitle">A quick overview of the test devices and the test requests running right now.</p>
                </div>
            </div>

            <div class="kpi-grid section-gap">
                ${kpi('cpu', 'Total Devices', s.totalDevices, `${s.mhuCount} MHU · ${s.benchCount} Full Bench`)}
                ${kpi('circle-check', 'Online', s.online, `${s.offline} offline`, 'var(--color-green)')}
                ${kpi('play-circle', 'Running Tests', s.runningRequests, `${s.scheduled} scheduled`, 'var(--color-blue)')}
                ${kpi('file-code', 'Test Cases', tcFiles.length, TC_SUMMARY.map(([name, label]) => `${tcFiles.filter((f) => f.category === name).length} ${label}`).join(' · '))}
            </div>

            <div class="grid-2col">
                <div class="card">
                    <div class="card-header">
                        <div class="card-title">${icon('history')} Test History</div>
                        <button class="btn btn-outline btn-sm" data-go="/runs">View all</button>
                    </div>
                    <div class="table-wrap">
                        <table class="data-table">
                            <thead><tr><th>Device</th><th>Finished</th><th>Status</th><th style="text-align:right">Action</th></tr></thead>
                            <tbody>
                                ${history.length ? historyRowsHtml(history) : '<tr><td colspan="4" class="muted" style="text-align:center;padding:24px">No run yet</td></tr>'}
                            </tbody>
                        </table>
                    </div>
                </div>

                <div class="card">
                    <div class="card-header"><div class="card-title">${icon('car')} Bench status</div></div>
                    <div class="card-body stack-8">
                        ${benches.map((b) => `
                            <div class="tc-item clickable-row" data-go="/devices/${b.id}">
                                <div class="grow">
                                    <div class="tc-name">${escapeHtml(b.name)}</div>
                                    <div class="tc-sub"><span class="id-mono">${b.id}</span> · ${escapeHtml(b.room)}</div>
                                </div>
                                <div class="stack-8" style="align-items:flex-end">
                                    ${deviceBadges(b)}
                                </div>
                            </div>`).join('')}
                        <div class="note">${icon('info')}<span>${runs.filter((r) => r.status === 'Running').length} run(s) in progress. Online/Offline is the connection of the bench agent, not the health of the ECUs.</span></div>
                    </div>
                </div>
            </div>`;
        if (html === lastHtml) return;            // nothing on this screen changed
        lastHtml = html;
        container.innerHTML = html;

        renderIcons();
        container.querySelectorAll('[data-go]').forEach((el) => {
            el.addEventListener('click', () => ctx.navigate(el.getAttribute('data-go')));
        });
        container.querySelectorAll('tr[data-open]').forEach((tr) => tr.addEventListener('click', () => ctx.navigate(`/runs/${tr.getAttribute('data-open')}/result`)));
    };

    render();
    const unsubscribe = onStoreChange(() => { if (container.isConnected) render(); });
    return () => unsubscribe();
}
