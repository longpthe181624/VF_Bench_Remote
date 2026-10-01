// ============================================================================
// demo/pages/bench-session.js — Bench session (#/devices/:id/session).
// "Join" on a device card lands here. Everything happens on this one page:
// upload test case files, press Run, watch the Processing bar and the
// results fill in, and see Finished when the run ends. The page opens Ready
// with a one-line link to the user's last run on this bench (past runs live in
// Test History); a run of the user's own still going is picked up again. The
// Upload card also has a Software tab: drop a software file and press Flash
// to update the device before running tests.
//
// The page keeps no copy of the run or the flash: it reads them from the
// store on every change; the agent (through the backend) moves them on.
// Only the files dropped for the next run are local to the page.
// ============================================================================
import { icon, renderIcons } from '../../icons.js';
import { escapeHtml } from '../../utils.js';
import { emptyState } from '../../components/ui-states.js';
import { showToast } from '../../components/toast.js';
import { confirmDialog } from '../../components/modal.js';
import {
    getDevice, getRun, getRuns, onStoreChange, joinDecision, runOwner, isMe, stopRun, startFlash,
    TEST_CASE_EXT, FLASH_EXT,
} from '../store.js';
import { pageHeader, deviceRunInfo, dayLabel, KIND_ICON } from '../ui.js';
import { downloadRunLog } from '../run-logs.js';
import { startQuickRun } from '../quick-run.js';
import { progressHtml, resultCardHtml, resultsCountText, resultsRowsHtml, resultsCardHtml, runResult, clock } from '../run-view.js';

const DEMO_MIN_PER_FILE = 10;      // uploaded files carry no duration
const sizeLabel = (bytes) => bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB`
    : bytes >= 1024 ? `${Math.round(bytes / 1024)} KB` : `${bytes} B`;
const hasExt = (name, list) => list.some((ext) => name.toLowerCase().endsWith(ext));

export async function mount(container, ctx) {
    const deviceId = ctx.params.id;
    let device = getDevice(deviceId);
    if (!device) {
        container.innerHTML = emptyState({ icon: 'search-x', title: 'Device not found', desc: `No device with ID ${escapeHtml(deviceId)}.`, action: '<button class="btn btn-outline btn-sm" data-back>Back to Devices</button>' });
        container.querySelector('[data-back]')?.addEventListener('click', () => ctx.navigate('/devices'));
        return;
    }

    // The user's own run still going on this bench is picked up again.
    const myRunning = () => getRuns({ targetId: deviceId }).find((r) => r.status === 'Running' && isMe(runOwner(r)));
    let sessionRunId = (myRunning() || {}).id || null;
    let run = sessionRunId ? getRun(sessionRunId) : null;
    let lastStatus = run ? run.status : null;
    let lastFlashAt = (device.lastFlash || {}).finishedAt || null;

    // Files dropped for the next run ({ name, size }); each becomes one test case.
    let files = [];
    let upTab = 'tc';
    let swFile = null;

    container.innerHTML = `
        ${pageHeader({
            iconName: KIND_ICON[device.kind] || 'car',
            title: escapeHtml(device.name),
            meta: `
                <span class="id-mono">${escapeHtml(device.id)}</span>
                <span class="sep">·</span><span id="conn"></span>
                <span class="sep">·</span><span id="hint"></span>`,
            actions: `
                <button class="btn btn-primary" id="btn-run" disabled>${icon('play')} Run</button>
                <button class="btn btn-danger" id="btn-stop" hidden>${icon('square')} Stop</button>
                <button class="btn btn-outline" id="btn-log-can">${icon('download')} Log CAN</button>
                <button class="btn btn-outline" id="btn-log-mhu">${icon('download')} Log MHU</button>`,
        })}

        <div class="card section-gap"><div class="card-body" id="prog-host"></div></div>
        <div class="section-gap" id="result-host"></div>

        <div class="grid-halves bench-grid">
            <div class="card">
                <div class="card-header">
                    <div class="card-title">${icon('upload')} Upload <span class="text-xs muted" id="up-count"></span></div>
                    <div class="segmented" id="up-tabs">
                        <button data-up="tc" class="active">Test cases</button>
                        <button data-up="sw">Software</button>
                    </div>
                </div>
                <div class="card-body">
                    <div id="pane-tc">
                        <div class="dropzone" id="tc-drop" style="min-height:180px;display:flex;flex-direction:column;align-items:center;justify-content:center">
                            ${icon('upload-cloud')}
                            <div class="fw6">Drag test case files here, or <span style="color:var(--accent)">browse</span></div>
                            <input type="file" id="tc-file" accept="${TEST_CASE_EXT.join(',')}" multiple hidden>
                        </div>
                        <div class="form-error" id="tc-err"></div>
                        <div id="file-host" style="margin-top:8px"></div>
                    </div>
                    <div id="pane-sw" hidden>
                        <div class="sum-row" style="margin-bottom:10px"><span class="k">Current software</span><span class="v id-mono" id="sw-current"></span></div>
                        <div class="dropzone" id="sw-drop" style="min-height:140px;display:flex;flex-direction:column;align-items:center;justify-content:center">
                            ${icon('cpu')}
                            <div class="fw6">Drag a software file here, or <span style="color:var(--accent)">browse</span></div>
                            <input type="file" id="sw-input" accept="${FLASH_EXT.join(',')}" hidden>
                        </div>
                        <div class="form-error" id="sw-err"></div>
                        <div id="sw-host" style="margin-top:8px"></div>
                        <div class="row-between" style="margin-top:8px">
                            <span></span>
                            <button class="btn btn-primary" id="btn-flash" disabled>${icon('zap')} Flash</button>
                        </div>
                    </div>
                </div>
            </div>
            ${resultsCardHtml()}
        </div>`;
    renderIcons();

    const $ = (sel) => container.querySelector(sel);
    const runBtn = $('#btn-run');

    // ------------------------------------------------------- derived state
    const running = () => !!run && run.status === 'Running';
    const flashing = () => device.flashing || null;
    // Join rule from the store: held by someone else (or offline) = blocked.
    const blocked = () => !joinDecision(device).allowed;
    const locked = () => blocked() || running() || !!flashing();
    const lastRun = () => getRuns({ targetId: deviceId })
        .filter((r) => r.status !== 'Running' && r.id !== sessionRunId && isMe(runOwner(r)))
        .sort((a, b) => new Date(b.finishedAt || b.startedAt) - new Date(a.finishedAt || a.startedAt))[0] || null;

    // ------------------------------------------------------------- painting
    function paintHeader() {
        const info = deviceRunInfo(device);
        $('#conn').textContent = blocked() ? 'Not available' : 'Connected';
        $('#conn').style.color = blocked() ? 'var(--color-red)' : 'var(--color-green)';
        $('#hint').innerHTML = info.hint;
    }

    function paintFiles() {
        $('#tc-drop').style.cursor = locked() ? 'not-allowed' : 'pointer';
        $('#tc-drop').style.opacity = locked() ? '0.6' : '';
        $('#up-count').textContent = files.length ? `${files.length} file${files.length === 1 ? '' : 's'}` : '';
        $('#file-host').innerHTML = files.map((f, i) => `
            <div class="file-row">
                <span class="file-ico">${icon('file')}</span>
                <span class="grow"><span class="fw6 id-mono">${escapeHtml(f.name)}</span><br><span class="text-xs muted">${sizeLabel(f.size)}</span></span>
                <button class="btn btn-ghost btn-icon btn-sm" data-remove="${i}" title="Remove" ${locked() ? 'disabled' : ''}>${icon('x')}</button>
            </div>`).join('');
        container.querySelectorAll('[data-remove]').forEach((b) => b.addEventListener('click', () => {
            if (locked()) return;
            files.splice(Number(b.getAttribute('data-remove')), 1);
            paintAll();
        }));
    }

    function paintSoftware() {
        $('#pane-tc').hidden = upTab !== 'tc';
        $('#pane-sw').hidden = upTab !== 'sw';
        container.querySelectorAll('#up-tabs button').forEach((b) => b.classList.toggle('active', b.getAttribute('data-up') === upTab));
        $('#sw-current').textContent = device.softwareVersion || device.firmware || '—';
        $('#sw-drop').style.cursor = locked() ? 'not-allowed' : 'pointer';
        $('#sw-drop').style.opacity = locked() ? '0.6' : '';
        $('#sw-host').innerHTML = swFile ? `
            <div class="file-row">
                <span class="file-ico">${icon('file')}</span>
                <span class="grow"><span class="fw6 id-mono">${escapeHtml(swFile.name)}</span><br><span class="text-xs muted">${sizeLabel(swFile.size)}</span></span>
                <button class="btn btn-ghost btn-icon btn-sm" id="sw-remove" title="Remove" ${locked() ? 'disabled' : ''}>${icon('x')}</button>
            </div>` : '';
        $('#sw-remove')?.addEventListener('click', () => { if (!locked()) { swFile = null; paintAll(); } });
        $('#btn-flash').disabled = locked() || !swFile;
    }

    function paintRun() {
        const f = flashing();
        const last = !run && !f ? lastRun() : null;
        $('#prog-host').innerHTML = f ? `
            <div class="row-between" style="margin-bottom:10px">
                <div><b>Flashing</b> <span class="muted">· ${escapeHtml(f.name)} · ${escapeHtml(f.from)} → ${escapeHtml(f.to)}</span></div>
                <span class="big-pct">${f.pct}%</span>
            </div>
            <div class="progress" style="height:10px"><div class="progress-bar" style="width:${f.pct}%"></div></div>`
            : progressHtml(run, { idleText: 'upload test case files and press Run' })
                + (last ? `
                    <div class="text-xs muted" style="margin-top:10px">Last run:
                        <b style="color:${runResult(last) === 'Passed' ? 'var(--color-green)' : 'var(--color-red)'}">${runResult(last) === 'Passed' ? 'PASS' : 'FAIL'}</b>
                        · ${dayLabel(last.finishedAt || last.startedAt)} ${clock(last.finishedAt || last.startedAt)}
                        · <span class="id-link" id="last-run-view" data-run="${escapeHtml(last.id)}">View</span></div>` : '');
        $('#last-run-view')?.addEventListener('click', (e) => ctx.navigate(`/runs/${e.target.getAttribute('data-run')}/result`));
        $('#result-host').innerHTML = resultCardHtml(run);
        $('#res-count').textContent = resultsCountText(run);
        $('#res-body').innerHTML = resultsRowsHtml(run);
    }

    function paintButtons() {
        const hasLogs = !!run && (run.logs || []).length > 0;
        $('#btn-stop').hidden = !running();
        $('#btn-log-can').disabled = !hasLogs;
        $('#btn-log-mhu').disabled = !hasLogs;
        runBtn.disabled = locked() || !files.length;
        runBtn.innerHTML = `${icon('play')} Run${files.length ? ` (${files.length})` : ''}`;
    }

    function paintAll() { paintHeader(); paintFiles(); paintSoftware(); paintRun(); paintButtons(); renderIcons(); }

    // ------------------------------------------------- store → this screen
    const unsubscribe = onStoreChange(() => {
        if (!container.isConnected) return;
        device = getDevice(deviceId) || device;
        if (!sessionRunId) {
            const mine = myRunning();               // started from another tab of the app
            if (mine) sessionRunId = mine.id;
        }
        run = sessionRunId ? getRun(sessionRunId) : null;
        if (lastStatus === 'Running' && run && run.status !== 'Running') {
            const verdict = runResult(run);
            showToast({ title: 'Run finished', description: `${run.id} — ${verdict}`, variant: verdict === 'Failed' ? 'error' : 'success' });
        }
        lastStatus = run ? run.status : null;
        const flashedAt = (device.lastFlash || {}).finishedAt || null;
        if (flashedAt && flashedAt !== lastFlashAt && isMe(device.lastFlash.by)) {
            showToast({ title: 'Software updated', description: `${device.id} now runs ${device.softwareVersion}.`, variant: 'success' });
        }
        lastFlashAt = flashedAt;
        paintAll();
    });

    // ------------------------------------------------------------- actions
    runBtn.addEventListener('click', () => {
        if (!files.length || locked()) return;
        const res = startQuickRun(device, files.map((f) => ({ id: f.name, durationMin: DEMO_MIN_PER_FILE })));
        if (res.error) { upTab = 'tc'; paintAll(); $('#tc-err').textContent = res.error; return; }
        sessionRunId = res.run.id;
        files = [];
        $('#tc-err').textContent = '';
        showToast({ title: 'Test started', description: `${res.run.id} is running on ${device.id}.`, variant: 'success' });
        run = getRun(sessionRunId);
        lastStatus = run.status;
        paintAll();
    });
    $('#btn-stop').addEventListener('click', async () => {
        if (!running()) return;
        const ok = await confirmDialog({ title: 'Stop this test?', description: 'The run stops immediately and is marked as failed in the demo data.', confirmText: 'Stop test', variant: 'danger' });
        if (ok && run) stopRun(run.id);            // the store ignores a run that already ended
    });

    $('#btn-log-can').addEventListener('click', () => { if (run && (run.logs || []).length) downloadRunLog(run, 'CAN'); });
    $('#btn-log-mhu').addEventListener('click', () => { if (run && (run.logs || []).length) downloadRunLog(run, 'MHU'); });

    // -------------------------------------------------------------- upload
    function takeFiles(list) {
        const err = $('#tc-err');
        const problems = [];
        [...list].forEach((f) => {
            if (!hasExt(f.name, TEST_CASE_EXT)) { problems.push(`${f.name}: use a ${TEST_CASE_EXT.join(', ')} file`); return; }
            if (files.some((x) => x.name.toLowerCase() === f.name.toLowerCase())) { problems.push(`${f.name} is already in the list`); return; }
            files.push({ name: f.name, size: f.size });
        });
        paintAll();
        err.textContent = problems.join(' · ');
    }

    const drop = $('#tc-drop');
    const fileInput = $('#tc-file');
    drop.addEventListener('click', () => { if (!locked()) fileInput.click(); });
    fileInput.addEventListener('change', () => { takeFiles(fileInput.files); fileInput.value = ''; });
    drop.addEventListener('dragover', (e) => { e.preventDefault(); if (!locked()) drop.classList.add('is-dragover'); });
    drop.addEventListener('dragleave', () => drop.classList.remove('is-dragover'));
    drop.addEventListener('drop', (e) => {
        e.preventDefault();
        drop.classList.remove('is-dragover');
        if (!locked()) takeFiles(e.dataTransfer.files);
    });

    // ------------------------------------------------------------ software
    container.querySelectorAll('#up-tabs button').forEach((b) => b.addEventListener('click', () => { upTab = b.getAttribute('data-up'); paintAll(); }));

    const swDrop = $('#sw-drop');
    const swInput = $('#sw-input');
    const takeSoftware = (f) => {
        if (!f) return;
        $('#sw-err').textContent = '';
        if (!hasExt(f.name, FLASH_EXT)) { $('#sw-err').textContent = `${f.name}: use a ${FLASH_EXT.join(', ')} file`; return; }
        swFile = { name: f.name, size: f.size };
        paintAll();
    };
    swDrop.addEventListener('click', () => { if (!locked()) swInput.click(); });
    swInput.addEventListener('change', () => { takeSoftware(swInput.files[0]); swInput.value = ''; });
    swDrop.addEventListener('dragover', (e) => { e.preventDefault(); if (!locked()) swDrop.classList.add('is-dragover'); });
    swDrop.addEventListener('dragleave', () => swDrop.classList.remove('is-dragover'));
    swDrop.addEventListener('drop', (e) => {
        e.preventDefault();
        swDrop.classList.remove('is-dragover');
        if (!locked()) takeSoftware(e.dataTransfer.files[0]);
    });

    // Flash: the store keeps the flash on the device; the agent reports its progress.
    $('#btn-flash').addEventListener('click', () => {
        if (!swFile || locked()) return;
        const res = startFlash(device.id, swFile);
        if (res.error) { $('#sw-err').textContent = res.error; return; }
        swFile = null;
        $('#sw-err').textContent = '';
        device = res.device;
        paintAll();
    });

    paintAll();

    return () => unsubscribe();
}
