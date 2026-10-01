// ============================================================================
// demo/pages/device-detail.js — One device (MHU / Full Bench) with tabs:
// Overview, ECUs / DID, Running, History. Re-reads the device, its runs and
// bookings from the store on every change; a part is redrawn only when what it
// shows changed (so the calendar keeps its week while a run moves on).
// ============================================================================
import { icon, renderIcons } from '../../icons.js';
import { escapeHtml, formatDateTime, relativeTime } from '../../utils.js';
import { emptyState } from '../../components/ui-states.js';
import { getDevice, getBookings, getRuns, getRequest, onStoreChange } from '../store.js';
import { statusBadge, deviceBadges, locationLabel, nameWithoutKind, kindBadge, KIND_ICON, pageHeader, secondsLabel, deviceRunInfo } from '../ui.js';
import { handleJoin } from '../quick-run.js';
import { ecuTreeHtml, scanSummary } from '../ecu-tree.js';
import { mountScheduleCalendar } from '../schedule-calendar.js';
import { resultBadge } from '../run-view.js';

export async function mount(container, ctx) {
    const id = ctx.params.id;
    let device = getDevice(id);

    if (!device) {
        container.innerHTML = emptyState({
            icon: 'search-x', title: 'Device not found',
            desc: `No device with ID ${escapeHtml(id)} exists in the demo data.`,
            action: '<button class="btn btn-outline btn-sm" data-back>Back to Devices</button>',
        });
        container.querySelector('[data-back]')?.addEventListener('click', () => ctx.navigate('/devices'));
        return;
    }

    const isMhu = device.kind === 'MHU';
    let runs = getRuns({ targetId: device.id });
    let activeRun = runs.find((r) => r.status === 'Running');
    // Overview opens first for both kinds and shows the booking calendar, so
    // everyone sees who has the device and when; there is no Schedule tab.
    // An MHU has no ECUs tab: its cards only hold registration data (name,
    // market, location) plus what the system detects (ID, status, DIDs).
    let tab = ctx.query.tab || 'overview';
    if (tab === 'schedule') tab = 'overview';
    if (isMhu && (tab === 'ecus' || tab === 'frs')) tab = 'did';
    if (!isMhu && tab === 'did') tab = 'overview';

    const kpi = (ic, label, value, sub) => `
        <div class="kpi-card"><div class="kpi-card-label">${icon(ic)}<span>${label}</span></div><div class="kpi-card-value" style="font-size:17px;margin-top:12px">${value}</div><div class="kpi-card-sub">${sub}</div></div>`;

    // Join is coloured by the device state like on the device card: green
    // available, orange free until a booking, red in use, gray offline.
    const headHtml = () => {
        const joinInfo = deviceRunInfo(device);
        return `
        ${pageHeader({
            iconName: KIND_ICON[device.kind],
            title: escapeHtml(device.name),
            meta: isMhu
                ? `<span class="id-mono">${escapeHtml(device.id)}</span>${deviceBadges(device)}`
                : `
                <span class="id-mono">${escapeHtml(device.id)}</span>
                ${kindBadge(device.kind)}
                ${deviceBadges(device)}
                <span class="sep">·</span><span>${locationLabel(device)}</span>`,
            actions: `
                <button class="btn run-btn is-${joinInfo.mode}" id="btn-join" title="${escapeHtml(joinInfo.hint.replace(/<[^>]+>/g, ''))}" ${joinInfo.mode === 'off' ? 'disabled' : ''}>${icon('log-in')} Join</button>`,
        })}

        ${isMhu ? `
        <div class="kpi-grid kpi-3 section-gap">
            ${kpi('globe', 'Market', escapeHtml(device.market || '—'), 'Target market of this MHU')}
            ${kpi('map-pin', 'Location', locationLabel(device), 'Where the MHU is installed')}
            ${kpi('radio-tower', 'Status', `<div class="inline-8">${deviceBadges(device)}</div>`, 'Detected automatically')}
        </div>` : `
        <div class="kpi-grid kpi-3 section-gap">
            <div class="kpi-card"><div class="kpi-card-label">${icon('folder')}<span>Projects</span></div><div class="kpi-card-value" style="font-size:17px;margin-top:12px">${(device.projects || []).map(escapeHtml).join(', ') || '—'}</div><div class="kpi-card-sub">Used by these programs</div></div>
            <div class="kpi-card"><div class="kpi-card-label">${icon('map-pin')}<span>Location</span></div><div class="kpi-card-value" style="font-size:17px;margin-top:12px">${escapeHtml(device.room)}</div><div class="kpi-card-sub">${device.floor && device.floor !== '—' ? `Floor ${escapeHtml(String(device.floor))}` : 'Lab / room'}</div></div>
            <div class="kpi-card"><div class="kpi-card-label">${icon('disc')}<span>Software</span></div><div class="kpi-card-value id-mono" style="font-size:17px;margin-top:12px">${escapeHtml(device.softwareVersion || device.firmware || '—')}</div><div class="kpi-card-sub">Installed version</div></div>
        </div>`}`;
    };

    container.innerHTML = `
        <div id="dd-head">${headHtml()}</div>

        <div class="tabs-list" id="tabs">
            ${isMhu
                ? '<button class="tabs-trigger" data-tab="overview">Overview</button><button class="tabs-trigger" data-tab="did">DID</button>'
                : '<button class="tabs-trigger" data-tab="overview">Overview</button><button class="tabs-trigger" data-tab="ecus">ECUs</button>'}
            <button class="tabs-trigger" data-tab="running">Running</button>
            <button class="tabs-trigger" data-tab="history">History</button>
        </div>
        <div id="tab-host"></div>`;
    renderIcons();

    const tabHost = container.querySelector('#tab-host');

    // ---- tab contents -----------------------------------------------------
    function overviewHtml() {
        const ecuCount = (device.ecus || []).length;
        // The MHU cards above already show everything registered for it, so
        // its Overview is the calendar alone.
        if (isMhu) return scheduleHtml();
        return `
            ${scheduleHtml()}
            <div class="card" style="margin-top:var(--space-4)">
                <div class="card-header"><div class="card-title">${icon('info')} General information</div></div>
                <div class="card-body">
                    <div class="meta-list">
                        <div class="meta-item"><div class="meta-label">Name</div><div class="meta-value">${escapeHtml(nameWithoutKind(device))}</div></div>
                        <div class="meta-item"><div class="meta-label">Type</div><div class="meta-value">${kindBadge(device.kind)}</div></div>
                        <div class="meta-item"><div class="meta-label">Status</div><div class="meta-value inline-8">${deviceBadges(device)}</div></div>
                        <div class="meta-item"><div class="meta-label">Software</div><div class="meta-value id-mono">${escapeHtml(device.softwareVersion || device.firmware || '—')}</div></div>
                        <div class="meta-item"><div class="meta-label">ECUs</div><div class="meta-value">${ecuCount}</div></div>
                        <div class="meta-item"><div class="meta-label">Active in system</div><div class="meta-value">${device.isActive === false ? 'No' : 'Yes'}</div></div>
                    </div>
                    <div class="note" style="margin-top:14px">${icon('info')}<span>Online/Offline reflects the agent link only. It does not prove that the ECUs or tools on this bench are healthy.</span></div>
                    <div class="card-title" style="margin:16px 0 8px;font-size:13px">${icon('history')} Recent activity</div>
                    ${runs.slice(0, 3).map((r) => `
                        <div class="tc-item">
                            <div class="grow"><div class="tc-name">${escapeHtml(r.id)}</div><div class="tc-sub">${formatDateTime(r.startedAt)}</div></div>
                            ${statusBadge(r.verdict || r.status)}
                        </div>`).join('') || `<div class="tc-sub">No runs yet on this device.</div>`}
                </div>
            </div>`;
    }

    // ECU diagram of the bench, same drawing as at registration. A bench
    // registered through the tool keeps the scan result (canLines); older
    // benches are grouped by the CAN line stored on each ECU.
    function ecusHtml() {
        const ecus = device.ecus || [];
        if (!ecus.length) return `<div class="card"><div class="card-body">${emptyState({ icon: 'network', title: 'No ECU listed', desc: 'Scan the bench with the tool to read its ECUs.' })}</div></div>`;
        const lines = device.canLines || ecus.reduce((acc, e) => {
            if (e.can) (acc[e.can] = acc[e.can] || []).push(e.code);
            return acc;
        }, {});
        const scan = { gateway: device.gateway || 'XGW', lines };
        const unplaced = ecus.filter((e) => !device.canLines && !e.can);
        return `
            <div class="card ecu-card">
                <div class="card-header">
                    <div class="card-title">${icon('network')} ECU diagram</div>
                    <span class="muted text-xs">${scanSummary(scan)}</span>
                </div>
                <div class="card-body">
                    ${ecuTreeHtml(scan)}
                    ${unplaced.length ? `<div class="note" style="margin-top:10px">${icon('info')}<span>Not on a known CAN line: ${unplaced.map((e) => escapeHtml(e.code)).join(', ')}</span></div>` : ''}
                </div>
            </div>`;
    }

    // DIDs are read by the agent when the MHU is connected.
    function didHtml() {
        const rows = device.dids || [];
        if (!rows.length) {
            return `<div class="card"><div class="card-body">${emptyState({ icon: 'scan-search', title: 'No DID read yet', desc: 'DIDs are read automatically once the agent connects to this MHU.' })}</div></div>`;
        }
        const live = device.connection === 'Online';
        return `
            <div class="card">
                <div class="card-header">
                    <div class="card-title">${icon('scan-search')} Identification read from the MHU</div>
                    <span class="muted text-xs">${live ? 'Read automatically · live' : `Last read ${relativeTime(device.lastSeenAt)}`}</span>
                </div>
                <div class="table-wrap">
                    <table class="data-table">
                        <thead><tr><th style="width:60px">No</th><th style="width:45%">Description</th><th>Value</th></tr></thead>
                        <tbody>
                            ${rows.map((r, i) => `
                                <tr>
                                    <td class="num">${i + 1}</td>
                                    <td>${escapeHtml(r.description)}</td>
                                    <td class="id-mono fw6">${escapeHtml(r.value)}</td>
                                </tr>`).join('')}
                        </tbody>
                    </table>
                </div>
                <div class="card-body" style="padding-top:0">
                    <div class="note" style="margin-top:12px">${icon('info')}<span>Sample values. In the real system the agent reads these DIDs (UDS 0x22) when the MHU is plugged in${live ? '' : '; the values shown were read the last time it was online'}.</span></div>
                </div>
            </div>`;
    }

    // Bookings as a Google Calendar style week grid (mounted in renderTab),
    // shown at the top of the Overview tab.
    function scheduleHtml() {
        return '<div id="sched-cal"></div>';
    }

    function runningHtml() {
        if (!activeRun) {
            return `<div class="card"><div class="card-body">${emptyState({
                icon: 'play-circle', title: 'No active test',
                desc: 'This device is not running a test request right now.',
                action: `<button class="btn btn-outline btn-sm" data-new-request>Create Test Request</button>`,
            })}</div></div>`;
        }
        const req = getRequest(activeRun.requestId);
        const done = activeRun.testCases.filter((t) => t.verdict === 'Passed' || t.verdict === 'Failed').length;
        return `
            <div class="card">
                <div class="card-header"><div class="card-title">${icon('activity')} Test in progress</div>${statusBadge('Running')}</div>
                <div class="card-body">
                    <div class="row-between" style="margin-bottom:10px">
                        <div>
                            <div class="fw6">${escapeHtml(req ? req.name : activeRun.requestId)}</div>
                            <div class="tc-sub"><span class="id-mono">${escapeHtml(activeRun.id)}</span> · started ${formatDateTime(activeRun.startedAt)}</div>
                        </div>
                        <span class="big-pct">${activeRun.progress}%</span>
                    </div>
                    <div class="progress"><div class="progress-bar" style="width:${activeRun.progress}%"></div></div>
                    <div class="tc-sub" style="margin-top:8px">${done} of ${activeRun.testCases.length} test cases completed</div>
                    <div style="margin-top:14px"><button class="btn btn-primary" data-live="${escapeHtml(activeRun.id)}">${icon('radio')} View Live Run</button></div>
                </div>
            </div>`;
    }

    function historyHtml() {
        const finished = runs.filter((r) => r.status !== 'Running');
        if (!finished.length) {
            return `<div class="card"><div class="card-body">${emptyState({ icon: 'history', title: 'No run yet', desc: 'Runs executed on this device will show up here.' })}</div></div>`;
        }
        return `
            <div class="card">
                <div class="card-header"><div class="card-title">${icon('history')} Run history</div><span class="muted text-xs">${finished.length} run(s)</span></div>
                <div class="table-wrap">
                    <table class="data-table">
                        <thead><tr><th>Run ID</th><th>Request</th><th>Started</th><th>Duration</th><th>Result</th><th style="text-align:right">Actions</th></tr></thead>
                        <tbody>
                            ${finished.map((r) => {
                                const req = getRequest(r.requestId);
                                const dur = r.finishedAt ? (new Date(r.finishedAt) - new Date(r.startedAt)) / 1000 : null;
                                return `
                                    <tr>
                                        <td class="id-mono">${escapeHtml(r.id)}</td>
                                        <td>${escapeHtml(req ? req.name : r.requestId)}</td>
                                        <td class="num nowrap">${formatDateTime(r.startedAt)}</td>
                                        <td class="num">${secondsLabel(dur)}</td>
                                        <td>${resultBadge(r)}</td>
                                        <td style="text-align:right"><button class="btn btn-outline btn-sm" data-result="${escapeHtml(r.id)}">View Result</button></td>
                                    </tr>`;
                            }).join('')}
                        </tbody>
                    </table>
                </div>
            </div>`;
    }

    function renderTab() {
        container.querySelectorAll('#tabs .tabs-trigger').forEach((b) => b.classList.toggle('active', b.getAttribute('data-tab') === tab));
        const map = { overview: overviewHtml, ecus: ecusHtml, did: didHtml, running: runningHtml, history: historyHtml };
        tabHost.innerHTML = (map[tab] || overviewHtml)();
        renderIcons();
        const cal = tabHost.querySelector('#sched-cal');
        if (cal) mountScheduleCalendar(cal, { device, bookings: getBookings(device.id), navigate: ctx.navigate });

        tabHost.querySelectorAll('[data-live]').forEach((b) => b.addEventListener('click', () => ctx.navigate(`/runs/${b.getAttribute('data-live')}/live`)));
        tabHost.querySelectorAll('[data-result]').forEach((b) => b.addEventListener('click', () => ctx.navigate(`/runs/${b.getAttribute('data-result')}/result`)));
        tabHost.querySelectorAll('[data-new-request]').forEach((b) => b.addEventListener('click', () => ctx.navigate('/requests/new', { target: device.id })));
    }

    container.querySelectorAll('#tabs .tabs-trigger').forEach((b) => {
        b.addEventListener('click', () => { tab = b.getAttribute('data-tab'); renderTab(); });
    });
    const bindJoin = () => container.querySelector('#btn-join').addEventListener('click', () => handleJoin(device, { navigate: ctx.navigate }));
    bindJoin();

    renderTab();

    // What each part shows; a part is redrawn only when this changes.
    const headSig = () => JSON.stringify([device, deviceRunInfo(device).mode, deviceRunInfo(device).hint]);
    const tabSig = () => JSON.stringify(tab === 'running' ? activeRun
        : tab === 'history' ? runs.filter((r) => r.status !== 'Running').map((r) => [r.id, r.status, r.finishedAt])
            : tab === 'overview' ? [getBookings(device.id), device, deviceRunInfo(device).mode]
                : device);
    let lastHead = headSig();
    let lastTab = tabSig();
    const unsubscribe = onStoreChange(() => {
        if (!container.isConnected) return;
        device = getDevice(id) || device;
        runs = getRuns({ targetId: device.id });
        activeRun = runs.find((r) => r.status === 'Running');
        if (headSig() !== lastHead) {
            lastHead = headSig();
            container.querySelector('#dd-head').innerHTML = headHtml();
            renderIcons();
            bindJoin();
        }
        if (tabSig() !== lastTab) { lastTab = tabSig(); renderTab(); }
    });
    // Switching tab paints that tab now; remember what it showed.
    container.querySelectorAll('#tabs .tabs-trigger').forEach((b) => b.addEventListener('click', () => { lastTab = tabSig(); }));

    return () => unsubscribe();
}
