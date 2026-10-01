// ============================================================================
// demo/register-device.js — "Register Device" flow: pick a kind, connect to
// the tool on the device PC, show what the tool returns (DIDs for an MHU, the
// ECU diagram for a Full Bench), then name the device to classify it.
// The tool round trip goes through the backend; until it is connected,
// Connect / Scan answers "not connected" and nothing can be registered.
// ============================================================================
import { icon, renderIcons } from '../icons.js';
import { openModal } from '../components/modal.js';
import { showToast } from '../components/toast.js';
import { escapeHtml } from '../utils.js';
import { createDevice, updateDevice, deviceIdExists, getDevices } from './store.js';
import { TOOLS } from './data.js';
import { NOT_CONNECTED } from './store.js';
import { KIND_ICON } from './ui.js';
import { ecuTreeHtml, scanSummary } from './ecu-tree.js';

const KINDS = [
    { kind: 'MHU', title: 'MHU', desc: 'A standalone Media Head Unit rig.' },
    { kind: 'FULL_BENCH', title: 'Full Bench', desc: 'A full test bench built from several ECUs.' },
];

export function openRegisterDeviceFlow({ onCreated } = {}) {
    const handle = openModal({
        title: 'Register Device',
        size: 'md',
        bodyHtml: `
            <p class="form-hint" style="margin-bottom:14px">Choose what you want to register. The form adapts to the type.</p>
            <div class="stack-8">
                ${KINDS.map((k) => `
                    <button class="pick-card" data-kind="${k.kind}" style="text-align:left;width:100%">
                        <div class="inline-8">
                            <span class="dev-icon">${icon(KIND_ICON[k.kind])}</span>
                            <span>
                                <span class="dev-name">${k.title}</span>
                                <span class="tc-sub" style="display:block">${k.desc}</span>
                            </span>
                        </div>
                    </button>`).join('')}
            </div>`,
        footerHtml: `<button class="btn btn-outline" data-close>Cancel</button>`,
    });
    renderIcons();
    handle.root.querySelectorAll('[data-kind]').forEach((btn) => {
        btn.addEventListener('click', () => {
            handle.close();
            openDeviceForm(btn.getAttribute('data-kind'), { onCreated });
        });
    });
}

// Registered names follow <kind>-<name>-<market>, e.g. "MHU-VF9 Lab 03-VN".
const NAME_KIND = { MHU: 'MHU', FULL_BENCH: 'FullBench' };
const composeName = (kind, name, market) => `${NAME_KIND[kind]}-${name.trim()}-${market.trim()}`;

/** Name field with the kind in front and the typed market behind it. */
function nameField(kind, label, placeholder) {
    return `
        <div class="form-group">
            <label class="form-label" for="f-name">${label}<span class="required">*</span></label>
            <div class="input-prefix"><span>${NAME_KIND[kind]}-</span><input class="input" id="f-name" placeholder="${placeholder}"><span class="suffix" id="f-name-suffix">-&lt;market&gt;</span></div>
            <div class="form-hint" id="f-name-preview">Saved as ${NAME_KIND[kind]}-&lt;name&gt;-&lt;market&gt;</div>
        </div>`;
}

/** Keeps the market suffix and the "Saved as" line in step with the fields. */
function bindNameField(root, kind) {
    const name = root.querySelector('#f-name');
    const market = root.querySelector('#f-market');
    const paint = () => {
        root.querySelector('#f-name-suffix').textContent = `-${market.value.trim() || '<market>'}`;
        root.querySelector('#f-name-preview').textContent = name.value.trim() && market.value.trim()
            ? `Saved as ${composeName(kind, name.value, market.value)}`
            : `Saved as ${NAME_KIND[kind]}-<name>-<market>`;
    };
    name.addEventListener('input', paint);
    market.addEventListener('input', paint);
    paint();
}

export function openDeviceForm(kind, { onCreated } = {}) {
    if (kind === 'MHU') openMhuForm({ onCreated });
    else openBenchForm({ onCreated });
}

// ---------------------------------------------------------------------------
// Shared pieces of the two register forms. Technical data belongs to the
// tool: nothing is shown until Connect / Scan succeeds, and the name fields
// stay locked until then. The user only names the device to classify it.
// ---------------------------------------------------------------------------

/**
 * Round trip to the tool on the device PC. Needs the backend (it relays the
 * request to the agent); until it is connected every call fails.
 */
function callTool() {
    return Promise.reject(new Error(NOT_CONNECTED));
}

function toolOptions(kind) {
    return TOOLS.filter((t) => t.kind === kind)
        .map((t) => `<option value="${t.id}">${t.id} · ${t.host}</option>`).join('');
}

function stepTitle(n, text) {
    return `<div class="reg-step"><span class="reg-num">${n}</span>${text}</div>`;
}

function emptyArea(iconName, text, cls = '') {
    return `<div class="reg-area ${cls}">${icon(iconName)}<span>${text}</span></div>`;
}

function statusPill(tone, iconName, text) {
    return `<span class="reg-status is-${tone}">${icon(iconName)}${text}</span>`;
}

/** A tool already registered as a device must not be registered twice. */
function registeredWith(toolId) {
    return getDevices().find((d) => d.toolId === toolId) || null;
}

/**
 * Wires the connect → data → name flow. `cfg` gives what differs per kind:
 * labels, how to read from the tool, how to draw the result and how to build
 * the device from it.
 */
function bindToolFlow(root, handle, cfg, onCreated) {
    const els = {
        connect: root.querySelector('#reg-connect'),
        status: root.querySelector('#reg-status'),
        data: root.querySelector('#reg-data'),
        name: root.querySelector('#reg-name-fields'),
        save: root.querySelector('#btn-save'),
    };
    const currentTool = () => TOOLS.find((t) => t.id === root.querySelector('#reg-tool').value);
    let phase = 'idle';
    let result = null;
    let dup = null;

    function paint() {
        const tool = currentTool();
        if (phase === 'idle') {
            els.status.innerHTML = statusPill('idle', 'circle-dashed', cfg.idleText);
            els.data.innerHTML = emptyArea(cfg.emptyIcon, cfg.emptyText);
            els.connect.innerHTML = `${icon(cfg.connectIcon)} ${cfg.connectLabel}`;
        } else if (phase === 'busy') {
            els.status.innerHTML = statusPill('busy', 'loader', cfg.busyText);
            els.data.innerHTML = emptyArea('loader', `Reading from ${escapeHtml(tool ? tool.id : 'the tool')}…`, 'is-busy');
        } else if (phase === 'err') {
            els.status.innerHTML = statusPill('err', 'plug-zap', cfg.errText);
            els.data.innerHTML = emptyArea('triangle-alert', cfg.errHint(tool), 'is-err');
            els.connect.innerHTML = `${icon('refresh-cw')} Retry`;
        } else {
            els.status.innerHTML = statusPill('ok', 'circle-check', cfg.okText(result));
            els.data.innerHTML = (dup
                ? `<div class="note red" style="margin-bottom:10px">${icon('ban')}<span>This tool is already registered as <b>${escapeHtml(dup.id)}</b> (${escapeHtml(dup.name)}).</span></div>`
                : '') + cfg.dataHtml(result);
            els.connect.innerHTML = `${icon('refresh-cw')} ${cfg.againLabel}`;
        }
        els.connect.disabled = phase === 'busy';
        els.name.disabled = phase !== 'ok' || !!dup;
        renderIcons();
        validate();
    }

    function validate() {
        const ready = phase === 'ok' && !dup && cfg.required.every((sel) => root.querySelector(sel).value.trim());
        els.save.disabled = !ready;
        return ready;
    }

    // Changing what to connect to invalidates what was read before.
    root.querySelectorAll('[data-reset]').forEach((el) => el.addEventListener('change', () => {
        phase = 'idle'; result = null; dup = null; paint();
    }));
    cfg.required.forEach((sel) => root.querySelector(sel).addEventListener('input', validate));

    els.connect.addEventListener('click', async () => {
        const tool = currentTool();
        phase = 'busy'; result = null; dup = null; paint();
        try {
            result = await callTool(tool);
            dup = registeredWith(tool.id);
            phase = 'ok';
        } catch (e) {
            phase = 'err';
        }
        if (root.isConnected) paint();
    });

    els.save.addEventListener('click', () => {
        if (!validate()) return;
        const device = createDevice({
            toolId: currentTool().id,
            connection: 'Online',
            lastSeenAt: new Date().toISOString(),
            ...cfg.build(result, root),
        });
        handle.close();
        showToast({ title: `${cfg.label} registered`, description: `${device.id} was added to the device list.`, variant: 'success' });
        if (onCreated) onCreated(device);
    });

    paint();
}

// ---------------------------------------------------------------------------
// Register MHU: connect to the tool → it returns the DIDs → name, market,
// location. The ID comes from the serial number the tool read.
// ---------------------------------------------------------------------------
function openMhuForm({ onCreated } = {}) {
    const handle = openModal({
        title: 'Register MHU',
        size: 'lg',
        bodyHtml: `
            <form novalidate class="reg-form">
                ${stepTitle(1, 'Connect to the tool on the MHU PC')}
                <div class="reg-connect-row">
                    <div class="form-group">
                        <label class="form-label" for="reg-tool">Tool / Agent<span class="required">*</span></label>
                        <select class="select" id="reg-tool" data-reset>${toolOptions('MHU')}</select>
                    </div>
                    <button type="button" class="btn btn-primary" id="reg-connect"></button>
                </div>
                <div id="reg-status"></div>

                ${stepTitle(2, 'DIDs read by the tool')}
                <div id="reg-data"></div>

                ${stepTitle(3, 'Name it to classify')}
                <fieldset id="reg-name-fields" class="reg-fieldset" disabled>
                    ${nameField('MHU', 'MHU Name', 'e.g. VF9 Lab 03')}
                    <div class="form-row">
                        <div class="form-group">
                            <label class="form-label" for="f-market">Market<span class="required">*</span></label>
                            <input class="input" id="f-market" placeholder="e.g. VN">
                        </div>
                        <div class="form-group">
                            <label class="form-label" for="f-location">Location<span class="required">*</span></label>
                            <input class="input" id="f-location" placeholder="e.g. Infotainment Lab">
                        </div>
                    </div>
                </fieldset>
            </form>`,
        footerHtml: `
            <button class="btn btn-outline" data-close>Cancel</button>
            <button class="btn btn-primary" id="btn-save" disabled>${icon('check')} Register</button>`,
    });

    bindNameField(handle.root, 'MHU');
    bindToolFlow(handle.root, handle, {
        label: 'MHU',
        connectLabel: 'Connect', connectIcon: 'plug', againLabel: 'Read again',
        idleText: 'Not connected', busyText: 'Connecting…', errText: 'Tool not responding',
        okText: (r) => `Connected · ${r.dids.length} DIDs read`,
        emptyIcon: 'table-2', emptyText: 'No data yet. Press Connect to read the DIDs from the tool.',
        errHint: (tool) => (tool ? `The tool did not respond, so there is nothing to show. Check that the tool is running on ${escapeHtml(tool.id)} and try again.` : NOT_CONNECTED),
        required: ['#f-name', '#f-market', '#f-location'],
        dataHtml: (r) => `
            <div class="table-wrap reg-table">
                <table class="data-table compact">
                    <thead><tr><th style="width:44px">No</th><th>Description</th><th>Value</th></tr></thead>
                    <tbody>${r.dids.map((d, i) => `<tr><td class="num">${i + 1}</td><td>${escapeHtml(d.description)}</td><td class="id-mono fw6">${escapeHtml(d.value)}</td></tr>`).join('')}</tbody>
                </table>
            </div>`,
        build: (r, root) => {
            const market = root.querySelector('#f-market').value.trim();
            const sw = r.dids.find((d) => d.description.startsWith('ECU Software Part Number'));
            return {
                id: uniqueId(`MHU-${market}-${r.serial}`),
                kind: 'MHU',
                name: composeName('MHU', root.querySelector('#f-name').value, market),
                market,
                room: root.querySelector('#f-location').value.trim(),
                floor: '',
                serial: r.serial,
                dids: r.dids,
                softwareVersion: sw ? sw.value : '—',
                projects: [],
            };
        },
    }, onCreated);
}

// ---------------------------------------------------------------------------
// Register Full Bench: link the tool and scan → the tool returns the gateway
// and the ECUs on each CAN line (it handles the VCI on its own PC) → the user
// names it, types the model and the location.
// ---------------------------------------------------------------------------
function openBenchForm({ onCreated } = {}) {
    const handle = openModal({
        title: 'Register Full Bench',
        size: 'lg',
        bodyHtml: `
            <form novalidate class="reg-form">
                ${stepTitle(1, 'Connect the bench and scan')}
                <div class="reg-connect-row">
                    <div class="form-group">
                        <label class="form-label" for="reg-tool">Tool / Agent<span class="required">*</span></label>
                        <select class="select" id="reg-tool" data-reset>${toolOptions('FULL_BENCH')}</select>
                    </div>
                    <button type="button" class="btn btn-primary" id="reg-connect"></button>
                </div>
                <div id="reg-status"></div>

                ${stepTitle(2, 'ECUs found by the tool')}
                <div id="reg-data"></div>

                ${stepTitle(3, 'Name it to classify')}
                <fieldset id="reg-name-fields" class="reg-fieldset" disabled>
                    ${nameField('FULL_BENCH', 'Full Bench Name', 'e.g. VF9 Lac Hong')}
                    <div class="form-row">
                        <div class="form-group">
                            <label class="form-label" for="f-market">Market<span class="required">*</span></label>
                            <input class="input" id="f-market" placeholder="e.g. VN">
                        </div>
                        <div class="form-group">
                            <label class="form-label" for="f-model">Model<span class="required">*</span></label>
                            <input class="input" id="f-model" placeholder="e.g. VF9_LAC_HONG">
                        </div>
                    </div>
                    <div class="form-row">
                        <div class="form-group">
                            <label class="form-label" for="f-location">Location<span class="required">*</span></label>
                            <input class="input" id="f-location" placeholder="e.g. Lab A · Floor 2">
                        </div>
                    </div>
                </fieldset>
            </form>`,
        footerHtml: `
            <button class="btn btn-outline" data-close>Cancel</button>
            <button class="btn btn-primary" id="btn-save" disabled>${icon('check')} Register</button>`,
    });

    bindNameField(handle.root, 'FULL_BENCH');
    bindToolFlow(handle.root, handle, {
        label: 'Full Bench',
        connectLabel: 'Connect &amp; scan', connectIcon: 'radar', againLabel: 'Scan again',
        idleText: 'Not scanned', busyText: 'Scanning…', errText: 'Scan failed',
        okText: (r) => `Scan complete · ${scanSummary(r.scan)}`,
        emptyIcon: 'network', emptyText: 'No ECU diagram yet. Press Connect &amp; scan.',
        errHint: (tool) => (tool ? 'Could not read any ECU, so there is nothing to show. Check the VCI and the bench power, then try again.' : NOT_CONNECTED),
        required: ['#f-name', '#f-market', '#f-model', '#f-location'],
        dataHtml: (r) => ecuTreeHtml(r.scan),
        build: (r, root) => {
            const model = root.querySelector('#f-model').value.trim();
            // "VF9_LAC_HONG" → "VF9": used for the ID and to match a project.
            const series = (model.match(/^[A-Za-z0-9]+/) || [''])[0].toUpperCase();
            return {
                id: uniqueId(series ? `BENCH-${series}` : 'BENCH', true),
                kind: 'FULL_BENCH',
                name: composeName('FULL_BENCH', root.querySelector('#f-name').value, root.querySelector('#f-market').value),
                market: root.querySelector('#f-market').value.trim(),
                room: root.querySelector('#f-location').value.trim(),
                floor: '',
                model,
                gateway: r.scan.gateway,
                canLines: r.scan.lines,
                ecus: Object.entries(r.scan.lines).flatMap(([can, list]) => list.map((code) => ({ code, name: code, softwareVersion: '—', can }))),
                projects: [],
                firmware: '—',
            };
        },
    }, onCreated);
}

/** `base` itself if free, else base-001, base-002… (always numbered when `numbered`). */
function uniqueId(base, numbered = false) {
    if (!numbered && !deviceIdExists(base)) return base;
    for (let n = 1; ; n += 1) {
        const id = `${base}-${String(n).padStart(3, '0')}`;
        if (!deviceIdExists(id)) return id;
    }
}

// ---------------------------------------------------------------------------
// Edit (demo scope): name, location and firmware of an existing device.
// ID, kind and linked ECUs stay unchanged; status is never edited by hand.
// ---------------------------------------------------------------------------
export function openEditDeviceForm(device, { onSaved } = {}) {
    if (device.kind === 'MHU') { openEditMhuForm(device, { onSaved }); return; }
    const isBench = device.kind === 'FULL_BENCH';

    const handle = openModal({
        title: `Edit ${escapeHtml(device.id)}`,
        size: 'md',
        bodyHtml: `
            <form novalidate>
                <div class="form-group">
                    <label class="form-label" for="e-name">Name<span class="required">*</span></label>
                    <input class="input" id="e-name" value="${escapeHtml(device.name)}">
                    <div class="form-error" data-err="name"></div>
                </div>
                <div class="form-row">
                    <div class="form-group"><label class="form-label" for="e-room">Room</label><input class="input" id="e-room" value="${escapeHtml(device.room || '')}"></div>
                    <div class="form-group"><label class="form-label" for="e-floor">Floor</label><input class="input" id="e-floor" value="${escapeHtml(String(device.floor || ''))}"></div>
                </div>
                ${isBench ? `<div class="form-group"><label class="form-label" for="e-fw">Firmware</label><input class="input font-mono" id="e-fw" value="${escapeHtml(device.firmware || '')}"></div>` : ''}
            </form>`,
        footerHtml: `<button class="btn btn-outline" data-close>Cancel</button><button class="btn btn-primary" id="btn-update">Save changes</button>`,
    });
    renderIcons();

    const root = handle.root;
    const v = (sel) => root.querySelector(sel).value.trim();
    root.querySelector('#btn-update').addEventListener('click', () => {
        if (!v('#e-name')) { root.querySelector('[data-err="name"]').textContent = 'Name is required'; return; }
        const patch = {
            name: v('#e-name'), room: v('#e-room'), floor: v('#e-floor') || '—',
            ...(isBench ? { firmware: v('#e-fw') } : {}),
        };
        const updated = updateDevice(device.id, patch);
        handle.close();
        showToast({ title: 'Device updated', description: `${device.id} was saved in the demo data.`, variant: 'success' });
        if (onSaved) onSaved(updated);
    });
}

// Edit an MHU: the same three fields as registration. The ID stays unchanged.
function openEditMhuForm(device, { onSaved } = {}) {
    const handle = openModal({
        title: `Edit ${escapeHtml(device.id)}`,
        size: 'md',
        bodyHtml: `
            <form novalidate>
                <div class="form-group">
                    <label class="form-label" for="e-name">MHU Name<span class="required">*</span></label>
                    <input class="input" id="e-name" value="${escapeHtml(device.name)}">
                    <div class="form-error" data-err="name"></div>
                </div>
                <div class="form-group">
                    <label class="form-label" for="e-market">Market<span class="required">*</span></label>
                    <input class="input" id="e-market" value="${escapeHtml(device.market || '')}" placeholder="e.g. VN">
                    <div class="form-error" data-err="market"></div>
                </div>
                <div class="form-group">
                    <label class="form-label" for="e-location">Location<span class="required">*</span></label>
                    <input class="input" id="e-location" value="${escapeHtml(device.room || '')}">
                    <div class="form-error" data-err="location"></div>
                </div>
            </form>`,
        footerHtml: `<button class="btn btn-outline" data-close>Cancel</button><button class="btn btn-primary" id="btn-update">Save changes</button>`,
    });
    renderIcons();

    const root = handle.root;
    const v = (sel) => root.querySelector(sel).value.trim();
    root.querySelector('#btn-update').addEventListener('click', () => {
        root.querySelector('[data-err="name"]').textContent = v('#e-name') ? '' : 'Name is required';
        root.querySelector('[data-err="market"]').textContent = v('#e-market') ? '' : 'Market is required';
        root.querySelector('[data-err="location"]').textContent = v('#e-location') ? '' : 'Location is required';
        if (!v('#e-name') || !v('#e-market') || !v('#e-location')) return;
        const updated = updateDevice(device.id, { name: v('#e-name'), market: v('#e-market'), room: v('#e-location'), floor: '' });
        handle.close();
        showToast({ title: 'Device updated', description: `${device.id} was saved in the demo data.`, variant: 'success' });
        if (onSaved) onSaved(updated);
    });
}
