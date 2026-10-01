// ============================================================================
// demo/store.js — The app's data, in one place. Every screen goes through
// this module; no screen keeps its own copy of data.
// No sample data and no backend yet: the store starts empty, and every action
// that needs the server (register, book, run, flash, upload) answers
// NOT_CONNECTED. Connecting the backend means filling these functions with
// REST calls; the screens and the rules below stay as they are.
// ============================================================================
import { DEMO_USER } from './data.js';
import { api, getCurrentUser } from './api-client.js';

const STORAGE_KEY = 'bench_console_demo_v2';
// v3: device kinds reduced to MHU and FULL_BENCH (ECU / Vehicle removed).
// v4: device status is Available / In Use / Scheduled; remote/robot flags removed.
// v5: device state derived from runs + bookings; bookings relative to now.
// v6: bench ECUs carry their CAN line (ECU diagram on the ECUs tab).
// v7: VF9 Connectivity Bench uses the VF9_LAC_HONG ECU set.
// v8: a booking of the demo user starting soon, to show the reminders.
// v9: sample devices renamed <kind>-<name>-<market>, with a market.
// v10: a finished quick run of the demo user (uploaded files, FAIL).
// v11: test case files uploaded on the Test Cases page (.zip / .7z).
// v12: runs simulated app-wide (flag `simulated`), log metadata per finished
//      run, flashing kept on the device.
// v13: no sample data and no simulation; the store starts empty.
const SCHEMA_VERSION = 13;

/** Answer of every action that needs the backend, until it is connected. */
export const NOT_CONNECTED = 'Not connected to the backend yet.';

/** Whether a backend answers. False until the API client is added. */
export const isBackendConnected = () => false;

/** The REST API can be used for the read-only slices already integrated. */
export const isApiConnected = () => !!getCurrentUser();

const listeners = new Set();
let db = null;
// Tests run the store in memory so they never touch the demo's saved data.
let memoryOnly = false;

function freshDb() {
    return {
        version: SCHEMA_VERSION,
        devices: [],
        testCases: [],
        requests: [],
        runs: [],
        bookings: [],
        testCaseFiles: [],
    };
}

function read() {
    if (memoryOnly) return null;
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (!raw) return null;
        const parsed = JSON.parse(raw);
        // A stored schema from an older demo build is dropped instead of patched.
        if (!parsed || parsed.version !== SCHEMA_VERSION || !Array.isArray(parsed.devices)) return null;
        return parsed;
    } catch (e) {
        return null; // private mode / blocked storage: fall back to memory only
    }
}

function write() {
    if (memoryOnly) return;
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(db));
    } catch (e) {
        /* storage unavailable — the demo keeps working from memory */
    }
}

function notify() {
    listeners.forEach((fn) => { try { fn(); } catch (e) { console.warn('demo store listener failed', e); } });
}

function commit() { write(); notify(); }

/** `{ memory: true }` keeps the store in memory only (used by the tests). */
export async function initStore({ memory = false } = {}) {
    memoryOnly = memory;
    db = read() || freshDb();
    if (!memoryOnly && !read()) write();
    if (!memoryOnly && isApiConnected()) await refreshDevices();
}

function deviceFromApi(row) {
    const kind = String(row.loai || '').toLowerCase() === 'bench' ? 'FULL_BENCH' : 'MHU';
    const state = String(row.state || '').toLowerCase();
    return {
        id: row.code,
        name: row.code,
        kind,
        model: row.model,
        market: '',
        room: row.workshop || '—',
        floor: row.tang || '—',
        firmware: row.firmware || null,
        softwareVersion: row.firmware || null,
        connection: state === 'offline' || state === 'unknown' ? 'Offline' : 'Online',
        operational: state === 'running' ? 'In Use' : 'Available',
        lastSeenAt: row.lastSeenAt,
        isActive: true,
        projects: row.duAns || [],
        toolId: row.tenMay || null,
        parentId: row.thuocVeCode || null,
        supportsRemote: row.hoTroRemote,
        supportsRobot: row.hoTroRobot,
        backendState: state,
        note: row.note || null,
        ecus: [], dids: [], canLines: {},
    };
}

export async function refreshDevices() {
    const rows = await api('/api/devices');
    db.devices = rows.map(deviceFromApi);
    commit();
    return getDevices();
}

export function applyDeviceUpdate(row) {
    if (!db) return;
    const mapped = deviceFromApi(row);
    const at = db.devices.findIndex((d) => d.id === mapped.id);
    if (at >= 0) db.devices[at] = { ...db.devices[at], ...mapped };
    else db.devices.unshift(mapped);
    commit();
}

/**
 * Nothing was written, but time passed: states that depend on the clock
 * (a booking starting, Scheduled → In Use) are re-read by the open screens.
 */
export function notifyClock() { notify(); }

export function onStoreChange(fn) {
    listeners.add(fn);
    return () => listeners.delete(fn);
}

export function resetDemoData() {
    db = freshDb();
    commit();
}

const clone = (v) => JSON.parse(JSON.stringify(v));

// ---------------------------------------------------------------------------
// Devices
// ---------------------------------------------------------------------------
export function getDevices(filter = {}) {
    let rows = clone(db.devices);
    if (filter.kind && filter.kind !== 'ALL') rows = rows.filter((d) => d.kind === filter.kind);
    if (filter.project) rows = rows.filter((d) => (d.projects || []).includes(filter.project));
    if (filter.connection) rows = rows.filter((d) => d.connection === filter.connection);
    if (filter.search) {
        const q = filter.search.trim().toLowerCase();
        rows = rows.filter((d) => d.id.toLowerCase().includes(q) || d.name.toLowerCase().includes(q));
    }
    return rows;
}

export function getDevice(id) {
    const d = db.devices.find((x) => x.id === id);
    return d ? clone(d) : null;
}

export function deviceIdExists(id) {
    return db.devices.some((d) => d.id.toLowerCase() === String(id).trim().toLowerCase());
}

export function createDevice(device) {
    const row = {
        connection: 'Offline', operational: 'Available',
        lastSeenAt: null, isActive: true, projects: [], ecus: [],
        ...device,
        createdAt: new Date().toISOString(),
    };
    db.devices.unshift(row);
    commit();
    return clone(row);
}

// A booking starting within this window makes the device "Scheduled".
export const SCHEDULED_WINDOW_MIN = 120;

/** Whether a name is the signed-in user (later: the user from the auth token). */
export const isMe = (name) => name === DEMO_USER.name;

/** Bookings that still count (not cancelled) as { b, start, end } in ms. */
function bookingSlots(deviceId) {
    return db.bookings
        .filter((b) => b.targetId === deviceId && b.status !== 'Cancelled')
        .map((b) => ({ b, start: new Date(b.startAt).getTime(), end: new Date(b.startAt).getTime() + (b.durationMin || 0) * 60e3 }));
}

/** The run going on a device, if any (there is never more than one). */
export function getActiveRun(deviceId) {
    const r = db.runs.find((x) => x.targetId === deviceId && x.status === 'Running');
    return r ? clone(r) : null;
}

/** Who started a run (the requester of its request). */
export function runOwner(run) {
    const req = run ? db.requests.find((r) => r.id === run.requestId) : null;
    return req ? req.requester : null;
}

/** The booking whose time has come on a device, if any. */
export function getActiveBooking(deviceId, now = Date.now()) {
    const s = bookingSlots(deviceId).find((x) => x.start <= now && now < x.end);
    return s ? clone(s.b) : null;
}

/** The next booking that has not started yet on a device, if any. */
export function getNextBooking(deviceId, now = Date.now()) {
    const s = bookingSlots(deviceId).filter((x) => x.start > now).sort((a, b) => a.start - b.start)[0];
    return s ? clone(s.b) : null;
}

/**
 * What a device is doing right now, derived — never typed in by a user:
 *   Offline    agent link is down
 *   In Use     software is being flashed, a test is running, or a booking's
 *              time has come (the device stays reserved for the whole booking
 *              even if nobody starts)
 *   Scheduled  free now, but a booking starts within SCHEDULED_WINDOW_MIN
 *   Available  otherwise
 * `mine` tells whether the flash / run / booking belongs to the signed-in user.
 * This is the only place these rules live; every screen reads them from here.
 */
export function getDeviceState(device, now = Date.now()) {
    const row = db.devices.find((d) => d.id === device.id) || device;
    if (row.connection !== 'Online') return { state: 'Offline' };

    if (row.flashing) {
        const left = Math.max(1, Math.ceil((100 - row.flashing.pct) / FLASH_STEP_PCT)) * 1000 * FLASH_TICK_SEC;
        return { state: 'In Use', reason: 'flash', by: row.flashing.by, mine: isMe(row.flashing.by), until: new Date(now + left).toISOString() };
    }

    const run = db.runs.find((r) => r.targetId === row.id && r.status === 'Running');
    if (run) {
        const req = db.requests.find((r) => r.id === run.requestId) || {};
        const estMs = (req.estimatedMin || 30) * 60e3;
        const until = Math.max(new Date(run.startedAt).getTime() + estMs, now + estMs * (1 - (run.progress || 0) / 100));
        const by = req.requester || '—';
        return { state: 'In Use', reason: 'run', until: new Date(until).toISOString(), by, mine: isMe(by), runId: run.id };
    }

    const slots = bookingSlots(row.id);
    const active = slots.find((s) => s.start <= now && now < s.end);
    if (active) {
        return {
            state: 'In Use', reason: 'booking', by: active.b.requester, mine: isMe(active.b.requester),
            from: active.b.startAt, until: new Date(active.end).toISOString(), minutesLeft: Math.floor((active.end - now) / 60e3),
        };
    }
    const next = slots.filter((s) => s.start > now).sort((a, b) => a.start - b.start)[0];
    if (next && next.start - now <= SCHEDULED_WINDOW_MIN * 60e3) {
        return { state: 'Scheduled', by: next.b.requester, mine: isMe(next.b.requester), freeUntil: next.b.startAt, minutesLeft: Math.floor((next.start - now) / 60e3) };
    }
    return { state: 'Available' };
}

/**
 * Whether the signed-in user may Join the bench, and why not. Join is open
 * while the device is online and not held by someone else: available, free
 * until the next booking, or held by the user (own booking, run or flash).
 */
export function joinDecision(device, now = Date.now()) {
    const s = getDeviceState(device, now);
    if (s.state === 'Offline') return { allowed: false, state: s, message: `${device.id} is offline.` };
    if (s.state === 'In Use' && !s.mine) {
        const until = new Date(s.until).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
        return { allowed: false, state: s, message: `${device.id} is in use until ${until}.` };
    }
    return { allowed: true, state: s, message: '' };
}

/**
 * Minutes a new run may take right now: up to someone else's next booking
 * when the device is Scheduled, up to the end of the user's own booking
 * while it is on; no limit otherwise.
 */
export function freeMinutes(device, now = Date.now()) {
    const s = getDeviceState(device, now);
    if (s.state === 'Scheduled' && !s.mine) return s.minutesLeft;
    if (s.state === 'In Use' && s.reason === 'booking' && s.mine) return s.minutesLeft;
    return null;
}

export function updateDevice(id, patch) {
    const row = db.devices.find((d) => d.id === id);
    if (!row) return null;
    Object.assign(row, patch);
    commit();
    return clone(row);
}

// ---------------------------------------------------------------------------
// Flash software. The state lives on the device so every screen,
// and a page reload, sees the same flash. Later: POST /api/devices/{id}/flash
// and progress events from the agent.
// ---------------------------------------------------------------------------
export const FLASH_EXT = ['.hex', '.bin', '.zip', '.7z'];
const FLASH_STEP_PCT = 12;          // progress per simulation step
export const FLASH_TICK_SEC = 0.5;  // one step every half second

/** The version a software file installs: its name without the extension. */
export const versionFromFile = (name) => String(name).replace(/\.(hex|bin|zip|7z)$/i, '');

/** Starts flashing `file` ({ name, size }) on a device. Returns { device } or { error }. */
export function startFlash(deviceId, file, by = DEMO_USER.name) {
    const row = db.devices.find((d) => d.id === deviceId);
    if (!row) return { error: 'Device not found.' };
    if (!file || !FLASH_EXT.some((ext) => file.name.toLowerCase().endsWith(ext))) return { error: `Use a ${FLASH_EXT.join(', ')} file.` };
    if (row.flashing) return { error: 'This device is already being flashed.' };
    if (db.runs.some((r) => r.targetId === deviceId && r.status === 'Running')) return { error: 'A test is running on this device.' };
    const join = joinDecision(row);
    if (!join.allowed) return { error: join.message };
    // Later: POST /api/devices/{id}/flash; the agent reports progress.
    if (!isBackendConnected()) return { error: NOT_CONNECTED };
    row.flashing = {
        name: file.name, size: file.size || 0, by,
        from: row.softwareVersion || row.firmware || '—', to: versionFromFile(file.name),
        pct: 0, startedAt: new Date().toISOString(),
    };
    commit();
    return { device: clone(row) };
}

/** One progress step of a flash; at 100 % the device runs the new version. */
export function advanceFlash(deviceId) {
    const row = db.devices.find((d) => d.id === deviceId);
    if (!row || !row.flashing) return null;
    row.flashing.pct = Math.min(100, row.flashing.pct + FLASH_STEP_PCT);
    let done = null;
    if (row.flashing.pct >= 100) {
        done = { ...row.flashing, finishedAt: new Date().toISOString() };
        row.softwareVersion = row.flashing.to;
        row.lastFlash = done;
        row.flashing = null;
    }
    commit();
    return done;
}

// ---------------------------------------------------------------------------
// Test cases
// ---------------------------------------------------------------------------
export function getTestCases(filter = {}) {
    let rows = clone(db.testCases);
    if (filter.project) rows = rows.filter((t) => t.project === filter.project);
    if (filter.type) rows = rows.filter((t) => t.type === filter.type);
    if (filter.status) rows = rows.filter((t) => t.status === filter.status);
    if (filter.search) {
        const q = filter.search.trim().toLowerCase();
        rows = rows.filter((t) => t.id.toLowerCase().includes(q) || t.name.toLowerCase().includes(q) || t.file.toLowerCase().includes(q));
    }
    return rows;
}

export function getTestCase(id) {
    const t = db.testCases.find((x) => x.id === id);
    return t ? clone(t) : null;
}

/** Test case files uploaded on the Test Cases page, newest first: { name, size, category, uploadedAt }. */
export function getTestCaseFiles() {
    return clone(db.testCaseFiles || []);
}

export const TEST_CASE_EXT = ['.zip', '.7z'];
export const TEST_CASE_CATEGORIES = ['Disable warning', 'Vivi', 'Warning message', 'Other'];

/**
 * The test case library: files uploaded on the Test Cases page plus the
 * packages already in the catalogue. Not the files dropped for one bench
 * session, and not the test cases copied into a run.
 * Rows: { name, category (or null), size, at, source: 'upload' | 'catalog' }.
 */
export function getLibraryTestCases() {
    const seen = new Set();
    return [
        ...(db.testCaseFiles || []).map((f) => ({ name: f.name, category: f.category || null, size: f.size || 0, at: f.uploadedAt, source: 'upload' })),
        ...db.testCases.filter((t) => t.file && t.status !== 'Archived')
            .map((t) => ({ name: t.file, category: t.category || null, size: t.sizeBytes || 0, at: t.updatedAt, source: 'catalog' })),
    ].filter((f) => {
        const key = f.name.toLowerCase();
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
    });
}

/** Adds a file to the library. Returns { file } or { error }. */
export function addTestCaseFile({ name, size, category }) {
    const n = String(name || '').trim();
    if (!n || TEST_CASE_EXT.every((ext) => !n.toLowerCase().endsWith(ext)) || TEST_CASE_EXT.includes(n.toLowerCase())) return { error: 'Enter a name' };
    if (!TEST_CASE_CATEGORIES.includes(category)) return { error: 'Choose a category' };
    if (getLibraryTestCases().some((f) => f.name.toLowerCase() === n.toLowerCase())) return { error: 'A file with this name already exists' };
    // Later: POST /api/test-cases (multipart).
    if (!isBackendConnected()) return { error: NOT_CONNECTED };
    db.testCaseFiles = db.testCaseFiles || [];
    const row = { name: n, size: size || 0, category, uploadedAt: new Date().toISOString() };
    db.testCaseFiles.unshift(row);
    commit();
    return { file: clone(row) };
}

export function createTestCase(tc) {
    const seq = db.testCases.length + 1;
    const row = {
        id: tc.id || `TC-NEW-${String(seq).padStart(3, '0')}`,
        source: 'Web Upload', status: 'Draft', durationMin: 10,
        ...tc,
        updatedAt: new Date().toISOString(),
    };
    db.testCases.unshift(row);
    commit();
    return clone(row);
}

export function updateTestCase(id, patch) {
    const row = db.testCases.find((t) => t.id === id);
    if (!row) return null;
    Object.assign(row, patch, { updatedAt: new Date().toISOString() });
    commit();
    return clone(row);
}

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------
export function getRequests(filter = {}) {
    let rows = clone(db.requests);
    if (filter.project) rows = rows.filter((r) => r.project === filter.project);
    if (filter.status) rows = rows.filter((r) => r.status === filter.status);
    if (filter.targetType) rows = rows.filter((r) => r.targetType === filter.targetType);
    if (filter.search) {
        const q = filter.search.trim().toLowerCase();
        rows = rows.filter((r) => r.id.toLowerCase().includes(q) || r.name.toLowerCase().includes(q) || (r.requester || '').toLowerCase().includes(q));
    }
    return rows.sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1));
}

export function getRequest(id) {
    const r = db.requests.find((x) => x.id === id);
    return r ? clone(r) : null;
}

function nextRequestId() {
    const nums = db.requests.map((r) => parseInt(String(r.id).split('-').pop(), 10)).filter((n) => !Number.isNaN(n));
    const next = (nums.length ? Math.max(...nums) : 90) + 1;
    return `REQ-2026-${String(next).padStart(4, '0')}`;
}

export function createRequest(data) {
    const nowIso = new Date().toISOString();
    const row = {
        id: nextRequestId(),
        status: data.status || 'Submitted',
        createdAt: nowIso,
        runId: null,
        timeline: [
            { at: nowIso, text: 'Request created', by: data.requester || 'Demo User' },
            { at: nowIso, text: `Test cases attached (${(data.testCaseIds || []).length})`, by: data.requester || 'Demo User' },
            { at: nowIso, text: `Target assigned — ${data.targetId}`, by: data.requester || 'Demo User' },
            ...(data.status === 'Draft' ? [] : [
                { at: nowIso, text: 'Request submitted', by: data.requester || 'Demo User' },
                { at: nowIso, text: 'Execution ready', by: 'System' },
            ]),
        ],
        ...data,
    };
    row.id = row.id || nextRequestId();
    db.requests.unshift(row);
    commit();
    return clone(row);
}

export function updateRequestStatus(id, status, extra = {}) {
    const row = db.requests.find((r) => r.id === id);
    if (!row) return null;
    row.status = status;
    Object.assign(row, extra);
    row.timeline = row.timeline || [];
    row.timeline.push({ at: new Date().toISOString(), text: `Status changed to ${status}`, by: 'System' });
    commit();
    return clone(row);
}

// ---------------------------------------------------------------------------
// Runs
// ---------------------------------------------------------------------------
export function getRuns(filter = {}) {
    let rows = clone(db.runs);
    if (filter.targetId) rows = rows.filter((r) => r.targetId === filter.targetId);
    if (filter.requestId) rows = rows.filter((r) => r.requestId === filter.requestId);
    return rows.sort((a, b) => (a.startedAt < b.startedAt ? 1 : -1));
}

export function getRun(id) {
    const r = db.runs.find((x) => x.id === id);
    return r ? clone(r) : null;
}

function nextRunId() {
    const nums = db.runs.map((r) => parseInt(String(r.id).split('-').pop(), 10)).filter((n) => !Number.isNaN(n));
    const next = (nums.length ? Math.max(...nums) : 990) + 1;
    return `RUN-2026-${String(next).padStart(5, '0')}`;
}

/**
 * Creates the run of a request. The agent reports its progress through the
 * backend (not connected yet).
 * Returns null if the device already has a run going.
 */
export function createDemoRun(request) {
    if (db.runs.some((r) => r.targetId === request.targetId && r.status === 'Running')) return null;
    const testCases = (request.testCaseIds || []).map((id, i) => {
        const tc = getTestCase(id);
        return {
            id, name: tc ? tc.name : id,
            verdict: i === 0 ? 'Running' : 'Waiting',
            durationSec: null, startedAt: i === 0 ? new Date().toISOString() : null,
        };
    });
    const row = {
        id: nextRunId(),
        requestId: request.id,
        targetId: request.targetId,
        simulated: true,
        status: 'Running',
        startedAt: new Date().toISOString(),
        finishedAt: null,
        progress: 2,
        currentIndex: 0,
        currentStep: 1,
        totalSteps: 9,
        testCases,
        log: [
            { at: new Date().toISOString(), level: 'INFO', text: 'Remote command accepted (simulated)' },
            { at: new Date().toISOString(), level: 'INFO', text: `Test request ${request.id} initialized` },
            { at: new Date().toISOString(), level: 'INFO', text: `${testCases[0] ? testCases[0].id : '—'} started` },
        ],
        telemetry: { temp: 34.2, humidity: 41, voltage: 12.6, canLoad: 38, latency: 24 },
    };
    db.runs.unshift(row);
    updateDevice(request.targetId, { operational: 'In Use' });
    const req = db.requests.find((r) => r.id === request.id);
    if (req) { req.status = 'Running'; req.runId = row.id; req.timeline.push({ at: row.startedAt, text: `Execution started on ${request.targetId}`, by: 'System' }); }
    commit();
    return clone(row);
}

export function updateDemoRun(id, patch) {
    const row = db.runs.find((r) => r.id === id);
    if (!row) return null;
    Object.assign(row, patch);
    commit();
    return clone(row);
}

/** Closes a demo run: writes verdicts, result payload and updates request + device. */
/**
 * Ends a demo run. With `realTime` the clock times the simulation recorded
 * are kept and the run ends now; otherwise the durations are rebuilt from the
 * test case durations so a fast-forwarded run still looks believable.
 */
export function finishDemoRun(id, verdict = 'Failed', { realTime = false } = {}) {
    const row = db.runs.find((r) => r.id === id);
    if (!row) return null;
    if (row.status !== 'Running') return clone(row);     // already ended: nothing to redo
    row.status = verdict === 'Passed' ? 'Completed' : 'Failed';
    row.verdict = verdict;
    row.progress = 100;
    if (realTime) {
        const now = Date.now();
        row.testCases = row.testCases.map((tc) => {
            const started = tc.startedAt ? new Date(tc.startedAt).getTime() : null;
            const durationSec = tc.verdict === 'Not run' || !started ? 0
                : tc.durationSec != null ? tc.durationSec : Math.round((now - started) / 1000);
            return { ...tc, durationSec, startedAt: tc.verdict === 'Not run' ? null : tc.startedAt };
        });
        return closeRun(row, verdict, new Date(now).toISOString());
    }
    let cursor = new Date(row.startedAt).getTime();
    row.testCases = row.testCases.map((tc, i) => {
        const durationSec = tc.verdict === 'Not run' ? 0 : tc.durationSec || [489, 731, 604, 712][i % 4];
        const startedAt = new Date(cursor).toISOString();
        cursor += durationSec * 1000;
        return {
            ...tc,
            // Cases the simulation already decided keep their verdict.
            verdict: ['Passed', 'Failed', 'Not run'].includes(tc.verdict) ? tc.verdict
                : verdict === 'Failed' && i === row.testCases.length - 1 ? 'Failed' : 'Passed',
            durationSec,
            startedAt,
        };
    });
    return closeRun(row, verdict, new Date(cursor).toISOString());
}

function closeRun(row, verdict, finishedAt) {
    row.finishedAt = finishedAt;
    row.progress = 100;
    row.log = (row.log || []).concat([{ at: finishedAt, level: verdict === 'Failed' ? 'ERROR' : 'INFO', text: `Run finished — ${verdict}` }]);
    addRunLogs(row);

    const req = db.requests.find((r) => r.id === row.requestId);
    if (req) {
        req.status = verdict === 'Failed' ? 'Failed' : 'Completed';
        req.runId = row.id;
        req.timeline = req.timeline || [];
        req.timeline.push({ at: finishedAt, text: `Execution finished — ${verdict}`, by: 'System' });
    }
    const dev = db.devices.find((d) => d.id === row.targetId);
    if (dev) dev.operational = 'Available';
    commit();
    return clone(row);
}

/**
 * The CAN and MHU log files of a finished run, written once when it ends.
 * Later: the agent uploads them and the backend stores this metadata.
 */
function addRunLogs(row) {
    if (row.logs && row.logs.length) return;
    const at = row.finishedAt || row.startedAt;
    row.logs = ['CAN', 'MHU'].map((kind) => ({ kind, name: `${row.id}_${kind.toLowerCase()}.log`, createdAt: at }));
}

/** Log files of every finished run of one kind ('CAN' | 'MHU'): { name, kind, createdAt, run }. */
export function getRunLogs(kind) {
    return db.runs.flatMap((r) => (r.logs || []).filter((l) => l.kind === kind).map((l) => ({ ...clone(l), run: clone(r) })));
}

/**
 * Stops a run: the test case in progress fails, the ones still waiting are
 * not run, and the run ends Failed. Calling it again does nothing.
 */
export function stopRun(id) {
    const row = db.runs.find((r) => r.id === id);
    if (!row) return null;
    if (row.status !== 'Running') return clone(row);
    const now = Date.now();
    row.testCases.forEach((tc) => {
        if (tc.verdict === 'Running') {
            tc.verdict = 'Failed';
            tc.durationSec = tc.startedAt ? Math.max(0, Math.round((now - new Date(tc.startedAt).getTime()) / 1000)) : 0;
        } else if (tc.verdict !== 'Passed' && tc.verdict !== 'Failed') {
            tc.verdict = 'Not run';
        }
    });
    row.log = (row.log || []).concat([{ at: new Date(now).toISOString(), level: 'WARN', text: 'Stop requested by user' }]);
    return finishDemoRun(id, 'Failed', { realTime: true });
}

/**
 * Starts a run on a bench from the test case files dropped in its session
 * (each file = one test case). Checks Join, a run or flash already going,
 * and the free time before the next booking. Returns { run } or { error }.
 * Later: POST /api/devices/{id}/start (202 + cmdId).
 */
export function startRun(deviceId, cases, { requester = DEMO_USER.name, minutesPerCase = 10 } = {}) {
    const device = db.devices.find((d) => d.id === deviceId);
    if (!device) return { error: 'Device not found.' };
    if (!cases || !cases.length) return { error: 'Add at least one test case file.' };
    if (device.flashing) return { error: 'Software is being flashed on this device.' };
    const active = db.runs.find((r) => r.targetId === deviceId && r.status === 'Running');
    if (active) return { error: `${active.id} is already running on this device.` };
    const join = joinDecision(device);
    if (!join.allowed) return { error: join.message };
    const minutes = cases.reduce((sum, c) => sum + (c.durationMin || minutesPerCase), 0);
    const limit = freeMinutes(device);
    if (limit != null && minutes > limit) return { error: `These test cases take about ${minutes} min, but only ${limit} min are free before the next booking.` };
    // Later: upload + deploy + POST /api/devices/{id}/start (202 + cmdId).
    if (!isBackendConnected()) return { error: NOT_CONNECTED };

    const projects = device.projects || [];
    const request = createRequest({
        name: `Quick run — ${device.name}`,
        requester,
        project: projects[0] || '—',
        priority: 'Medium',
        description: 'Started from the bench session page.',
        tags: ['Quick run'],
        ecu: device.kind === 'MHU' ? 'MHU' : '—',
        currentSoftware: device.softwareVersion || device.firmware || '—',
        requiredSoftware: device.softwareVersion || device.firmware || '—',
        softwareSource: '—',
        flashBeforeTest: false,
        testCaseIds: cases.map((c) => c.id),
        targetType: device.kind,
        targetId: device.id,
        executionMode: 'Run Now',
        scheduledAt: null,
        estimatedMin: minutes,
        status: 'Submitted',
    });
    const run = createDemoRun(request);
    return run ? { run } : { error: 'Could not start the run.' };
}

// ---------------------------------------------------------------------------
// Bookings (Device Detail → Schedule)
// ---------------------------------------------------------------------------
export function getBookings(targetId) {
    return clone(db.bookings)
        .filter((b) => !targetId || b.targetId === targetId)
        .sort((a, b) => (a.startAt > b.startAt ? 1 : -1));
}

/** A booking on the device that overlaps [startIso, endIso), if any (cancelled ones do not count). */
export function findBookingConflict(targetId, startIso, endIso) {
    const from = new Date(startIso).getTime();
    const to = new Date(endIso).getTime();
    const s = bookingSlots(targetId).find((x) => x.start < to && from < x.end);
    return s ? clone(s.b) : null;
}

/**
 * Books a device for { targetId, requestId, requestName, requester, startAt,
 * durationMin }. Returns { booking } or { error }. Later: POST /api/bookings.
 */
export function createBooking(booking) {
    if (!db.devices.some((d) => d.id === booking.targetId)) return { error: 'Device not found.' };
    if (!(booking.durationMin > 0)) return { error: 'End must be after start' };
    const end = new Date(new Date(booking.startAt).getTime() + booking.durationMin * 60e3).toISOString();
    const clash = findBookingConflict(booking.targetId, booking.startAt, end);
    if (clash) return { error: `Overlaps ${clash.requester}'s booking`, conflict: clash };
    if (!isBackendConnected()) return { error: NOT_CONNECTED };
    const ids = new Set(db.bookings.map((b) => b.id));
    let id = `BK-${Date.now()}`;
    while (ids.has(id)) id = `${id}-1`;
    const row = { id, status: 'Scheduled', ...booking };
    db.bookings.push(row);
    commit();
    return { booking: clone(row) };
}

// ---------------------------------------------------------------------------
// Dashboard aggregation
// ---------------------------------------------------------------------------
export function getDashboardStats(now = Date.now()) {
    const devices = db.devices;
    const requests = db.requests;
    return {
        totalDevices: devices.length,
        mhuCount: devices.filter((d) => d.kind === 'MHU').length,
        benchCount: devices.filter((d) => d.kind === 'FULL_BENCH').length,
        online: devices.filter((d) => d.connection === 'Online').length,
        offline: devices.filter((d) => d.connection === 'Offline').length,
        inUse: devices.filter((d) => getDeviceState(d, now).state === 'In Use').length,
        totalRequests: requests.length,
        // Runs going now, and bookings that have not started yet.
        runningRequests: db.runs.filter((r) => r.status === 'Running').length,
        scheduled: db.bookings.filter((b) => b.status !== 'Cancelled' && new Date(b.startAt).getTime() > now).length,
        completed: requests.filter((r) => r.status === 'Completed').length,
        failed: requests.filter((r) => r.status === 'Failed').length,
        testCases: db.testCases.length,
        activeTestCases: db.testCases.filter((t) => t.status === 'Active').length,
    };
}
