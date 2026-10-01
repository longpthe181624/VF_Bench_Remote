// ============================================================================
// demo/reminders.js — Reminders of the signed-in user's own bookings:
//   A. a bell in the top bar with the list of upcoming bookings
//   C. a pop-up in the corner shortly before a booking starts, and when it
//      starts
// Checks the store on a timer and on every change.
// ============================================================================
import { icon, renderIcons } from '../icons.js';
import { escapeHtml } from '../utils.js';
import { DEMO_USER } from './data.js';
import { getBookings, getDevice, onStoreChange } from './store.js';

// How long before a booking starts the "starts soon" pop-up appears.
export const REMIND_BEFORE_MIN = 15;
// How far ahead the bell list looks.
const LIST_AHEAD_DAYS = 7;
// Which pop-ups were already shown, so a reload does not repeat them.
const SHOWN_KEY = 'bench_console_reminders_shown';

const hm = (d) => d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });

/** The user's bookings that have not ended yet, soonest first. */
export function myUpcomingBookings(now = Date.now()) {
    const until = now + LIST_AHEAD_DAYS * 86400e3;
    return getBookings()
        .filter((b) => b.requester === DEMO_USER.name && b.status !== 'Cancelled')
        .map((b) => {
            const start = new Date(b.startAt);
            return { b, start, end: new Date(start.getTime() + (b.durationMin || 0) * 60e3) };
        })
        .filter((x) => x.end.getTime() > now && x.start.getTime() < until)
        .sort((a, c) => a.start - c.start);
}

/** "In progress · ends 15:49", "Starts in 12 min", "Today 14:00", "Tomorrow 09:00", "Fri 2 Oct 09:00". */
function whenLabel({ start, end }, now = new Date()) {
    const mins = Math.round((start - now) / 60e3);
    if (start <= now) return { text: `In progress · ends ${hm(end)}`, tone: 'live' };
    if (mins < 60) return { text: `Starts in ${Math.max(mins, 1)} min`, tone: 'soon' };
    const day = new Date(start); day.setHours(0, 0, 0, 0);
    const today = new Date(now); today.setHours(0, 0, 0, 0);
    const diff = Math.round((day - today) / 86400e3);
    if (diff === 0) return { text: `Today ${hm(start)}`, tone: '' };
    if (diff === 1) return { text: `Tomorrow ${hm(start)}`, tone: '' };
    return { text: `${start.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })} ${hm(start)}`, tone: '' };
}

// ---------------------------------------------------------------------------
// A. Bell in the top bar
// ---------------------------------------------------------------------------
export function bellHtml() {
    const n = myUpcomingBookings().length;
    return `
        <div class="bell-wrap">
            <button type="button" class="btn btn-ghost btn-icon bell-btn" id="btn-bell" aria-label="Your upcoming tests">
                ${icon('bell')}${n ? `<span class="bell-count">${n}</span>` : ''}
            </button>
            <div class="bell-menu" id="bell-menu" hidden></div>
        </div>`;
}

export function bindBell(root, navigate) {
    const btn = root.querySelector('#btn-bell');
    const menu = root.querySelector('#bell-menu');
    if (!btn || !menu) return;

    function paint() {
        const list = myUpcomingBookings();
        menu.innerHTML = `
            <div class="bell-head">Your upcoming tests <span class="muted">${list.length}</span></div>
            ${list.length ? list.map((x) => {
                const dev = getDevice(x.b.targetId);
                const w = whenLabel(x);
                return `
                    <button type="button" class="bell-item" data-go="${escapeHtml(x.b.targetId)}">
                        <span class="bell-bar ${w.tone}"></span>
                        <span class="grow">
                            <span class="bell-dev">${escapeHtml(x.b.targetId)}${dev ? ` <span class="muted">· ${escapeHtml(dev.name)}</span>` : ''}</span>
                            <span class="bell-when ${w.tone}">${w.text}</span>
                            <span class="bell-sub">${hm(x.start)}–${hm(x.end)} · ${escapeHtml(x.b.requestName)}</span>
                        </span>
                    </button>`;
            }).join('') : `<div class="bell-empty">${icon('calendar-check')}<span>You have no upcoming test.</span></div>`}`;
        renderIcons();
        menu.querySelectorAll('[data-go]').forEach((el) => el.addEventListener('click', () => {
            menu.hidden = true;
            navigate(`/devices/${el.getAttribute('data-go')}`);
        }));
    }

    btn.addEventListener('click', (e) => {
        e.stopPropagation();
        if (menu.hidden) paint();
        menu.hidden = !menu.hidden;
    });
    document.addEventListener('click', (e) => {
        if (!menu.hidden && !menu.contains(e.target)) menu.hidden = true;
    });
}

// ---------------------------------------------------------------------------
// C. Pop-up in the corner when a booking is about to start / has started
// ---------------------------------------------------------------------------
function readShown() {
    try { return JSON.parse(sessionStorage.getItem(SHOWN_KEY) || '{}'); } catch (e) { return {}; }
}
function writeShown(v) {
    try { sessionStorage.setItem(SHOWN_KEY, JSON.stringify(v)); } catch (e) { /* storage blocked: may repeat */ }
}

function showReminder(x, kind, navigate) {
    const host = document.getElementById('toast-host');
    if (!host) return;
    const dev = getDevice(x.b.targetId);
    const mins = Math.max(1, Math.round((x.start - Date.now()) / 60e3));
    const el = document.createElement('div');
    el.className = `toast reminder-toast ${kind === 'start' ? 'variant-success' : 'variant-warning'}`;
    el.setAttribute('role', 'alert');
    el.innerHTML = `
        <span class="toast-icon">${icon(kind === 'start' ? 'play-circle' : 'alarm-clock')}</span>
        <div class="grow">
            <div class="toast-title">${kind === 'start' ? 'Your booking has started' : `Your booking starts in ${mins} min`}</div>
            <div class="toast-desc">${escapeHtml(x.b.targetId)}${dev ? ` · ${escapeHtml(dev.name)}` : ''}<br>${hm(x.start)}–${hm(x.end)} · ${escapeHtml(x.b.requestName)}</div>
            <div class="reminder-actions">
                <button type="button" class="btn btn-primary btn-sm" data-open>Open device</button>
                <button type="button" class="btn btn-outline btn-sm" data-dismiss>Dismiss</button>
            </div>
        </div>`;
    host.appendChild(el);
    renderIcons();
    el.querySelector('[data-dismiss]').addEventListener('click', () => el.remove());
    el.querySelector('[data-open]').addEventListener('click', () => { el.remove(); navigate(`/devices/${x.b.targetId}`); });
}

/** Starts the reminder loop once for the whole app. */
export function startReminders(navigate) {
    function check() {
        const now = Date.now();
        const shown = readShown();
        myUpcomingBookings(now).forEach((x) => {
            const startMs = x.start.getTime();
            const started = startMs <= now;
            const soon = !started && startMs - now <= REMIND_BEFORE_MIN * 60e3;
            const key = `${x.b.id}:${started ? 'start' : 'soon'}`;
            if ((started || soon) && !shown[key]) {
                shown[key] = true;
                showReminder(x, started ? 'start' : 'soon', navigate);
            }
        });
        writeShown(shown);
    }
    check();
    setInterval(check, 30e3);

    // A new booking (or one ending) changes the count on the bell right away.
    onStoreChange(() => {
        const btn = document.getElementById('btn-bell');
        if (!btn) return;
        const n = myUpcomingBookings().length;
        let badge = btn.querySelector('.bell-count');
        if (!n) { if (badge) badge.remove(); return; }
        if (!badge) { badge = document.createElement('span'); badge.className = 'bell-count'; btn.appendChild(badge); }
        if (badge.textContent !== String(n)) badge.textContent = String(n);
    });
}
