// ============================================================================
// demo/ui.js — Small presentation helpers shared by the demo screens so every
// badge, icon and label means the same thing everywhere.
// ============================================================================
import { renderBadge } from '../components/badge.js';
import { icon } from '../icons.js';
import { escapeHtml } from '../utils.js';
import { getDeviceState } from './store.js';

// Operational status (what the device / request is doing right now)
const STATUS_META = {
    // devices
    Idle: { cls: 'badge-green', icon: 'circle-check' },
    Available: { cls: 'badge-green', icon: 'circle-check' },
    Running: { cls: 'badge-blue', icon: 'loader' },
    Processing: { cls: 'badge-blue', icon: 'loader' },
    'In Use': { cls: 'badge-amber', icon: 'user' },
    Offline: { cls: 'badge-gray', icon: 'circle-slash' },
    Inactive: { cls: 'badge-orange', icon: 'circle-slash' },
    Maintenance: { cls: 'badge-orange', icon: 'wrench' },
    // requests
    Draft: { cls: 'badge-gray', icon: 'file-pen-line' },
    Submitted: { cls: 'badge-blue', icon: 'send' },
    Scheduled: { cls: 'badge-purple', icon: 'calendar-clock' },
    Waiting: { cls: 'badge-amber', icon: 'clock' },
    Completed: { cls: 'badge-green', icon: 'circle-check' },
    Failed: { cls: 'badge-red', icon: 'circle-x' },
    Cancelled: { cls: 'badge-gray', icon: 'ban' },
    // verdicts
    Passed: { cls: 'badge-green', icon: 'circle-check' },
    Skipped: { cls: 'badge-purple', icon: 'skip-forward' },
    'Not run': { cls: 'badge-gray', icon: 'ban' },
    Blocked: { cls: 'badge-gray', icon: 'ban' },
    Active: { cls: 'badge-green', icon: 'circle-check' },
    Archived: { cls: 'badge-gray', icon: 'archive' },
};

export function statusBadge(status) {
    const m = STATUS_META[status] || { cls: 'badge-gray', icon: 'circle' };
    return renderBadge(escapeHtml(String(status)), m.cls, m.icon);
}

export function connectionBadge(connection) {
    return connection === 'Online'
        ? renderBadge('Online', 'badge-green', 'wifi')
        : renderBadge('Offline', 'badge-gray', 'wifi-off');
}

/**
 * Device badges shown next to a device: always Online/Offline, plus a red
 * "In Use" or an orange "Scheduled". Available shows no extra badge.
 */
export function deviceBadges(d) {
    const s = getDeviceState(d);
    const extra = s.state === 'In Use' ? renderBadge('In Use', 'badge-red', 'circle-dot')
        : s.state === 'Scheduled' ? renderBadge('Scheduled', 'badge-orange', 'calendar-clock')
        : '';
    return connectionBadge(d.connection) + extra;
}

/**
 * Colour and one-line hint of a device's Join button, from the store's
 * getDeviceState (the same state joinDecision uses, so colour and access
 * never disagree):
 *   go     green   — free, or held by the signed-in user (booking, run, flash)
 *   sched  orange  — free now, but someone else's booking starts soon
 *   busy   red     — held by someone else; Join only says it is busy
 *   off    gray    — offline, Join is locked
 */
export function deviceRunInfo(d) {
    const s = getDeviceState(d);
    if (s.state === 'Offline') return { mode: 'off', icon: 'unplug', hint: 'Disconnected', state: s };
    if (s.state === 'Scheduled') {
        if (s.mine) return { mode: 'go', icon: 'calendar-check', hint: `Your booking starts at ${timeLabel(s.freeUntil)}`, state: s };
        return { mode: 'sched', icon: 'clock', hint: `Free until ${timeLabel(s.freeUntil)} · ${minutesLabel(s.minutesLeft)} left`, state: s };
    }
    if (s.state === 'In Use') {
        if (s.mine) {
            const hint = s.reason === 'booking' ? `Your booking · ${timeLabel(s.from)}–${timeLabel(s.until)}`
                : s.reason === 'flash' ? 'Your software update is in progress'
                    : `Your test is running · ends ~${timeLabel(s.until)}`;
            return { mode: 'go', icon: 'user-check', hint, state: s };
        }
        return { mode: 'busy', icon: 'lock', hint: `Busy until ${timeLabel(s.until)} · ${escapeHtml(s.by)}`, state: s };
    }
    return { mode: 'go', icon: 'circle-check', hint: 'Available now', state: s };
}

/** "Lab A · Floor 2", or just "Lab A" when no floor is recorded. Escaped. */
export function locationLabel(d) {
    const floor = d.floor && d.floor !== '—' ? ` · Floor ${escapeHtml(String(d.floor))}` : '';
    return `${escapeHtml(d.room || '—')}${floor}`;
}

/**
 * Name without the kind in front ("FullBench-VF6-VN" → "VF6-VN"), for places
 * that already show the type next to it.
 */
export function nameWithoutKind(d) {
    const prefix = { MHU: 'MHU-', FULL_BENCH: 'FullBench-' }[d.kind];
    return prefix && d.name.startsWith(prefix) ? d.name.slice(prefix.length) : d.name;
}

export const KIND_LABEL = { MHU: 'MHU', FULL_BENCH: 'Full Bench' };
export const KIND_ICON = { MHU: 'monitor-smartphone', FULL_BENCH: 'car' };

export function kindBadge(kind) {
    return `<span class="kind-badge">${KIND_LABEL[kind] || kind}</span>`;
}

export function projectTags(projects = []) {
    return projects.map((p) => `<span class="tag">${escapeHtml(p)}</span>`).join(' ');
}

/** Short duration label from minutes: 42 -> "42 min", 90 -> "1h 30m". */
export function minutesLabel(min) {
    if (!min && min !== 0) return '—';
    if (min < 60) return `${min} min`;
    const h = Math.floor(min / 60);
    const m = min % 60;
    return m ? `${h}h ${m}m` : `${h}h`;
}

export function secondsLabel(sec) {
    if (sec === null || sec === undefined) return '—';
    const m = Math.floor(sec / 60);
    const s = Math.round(sec % 60);
    return m ? `${m}m ${String(s).padStart(2, '0')}s` : `${s}s`;
}

export function timeLabel(isoStr) {
    if (!isoStr) return '—';
    const d = new Date(isoStr);
    return d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
}

export function dayLabel(isoStr) {
    if (!isoStr) return '—';
    const d = new Date(isoStr);
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const day = new Date(d); day.setHours(0, 0, 0, 0);
    const diff = Math.round((day - today) / 86400e3);
    if (diff === 0) return 'Today';
    if (diff === 1) return 'Tomorrow';
    return d.toLocaleDateString('en-GB', { weekday: 'short', day: '2-digit', month: 'short' });
}

/** Builds a small text/CSV/JSON file in the browser — no server involved. */

export function pageHeader({ title, subtitle = '', meta = '', actions = '', iconName = '' }) {
    return `
        <div class="page-header">
            <div>
                <h2>${iconName ? icon(iconName) : ''} ${title}</h2>
                ${subtitle ? `<p class="page-subtitle">${subtitle}</p>` : ''}
                ${meta ? `<div class="page-meta">${meta}</div>` : ''}
            </div>
            ${actions ? `<div class="page-header-actions">${actions}</div>` : ''}
        </div>`;
}
