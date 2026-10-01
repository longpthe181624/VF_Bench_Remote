// ============================================================================
// demo/schedule-calendar.js — Bookings of one device as a Google Calendar
// style week grid: who booked what, when, colored per person.
// Read-only view of the demo store; "Book slot" goes to the request wizard.
// ============================================================================
import { icon, renderIcons } from '../icons.js';
import { escapeHtml } from '../utils.js';
import { DEMO_USER } from './data.js';

const HOUR_PX = 40;
const ME_COLOR = '#2563eb';
const SYSTEM_COLOR = '#6b7280';
const PALETTE = ['#7c3aed', '#0f9f7a', '#d97706', '#db2777', '#0891b2', '#65a30d'];

function colorFor(name) {
    if (name === DEMO_USER.name) return ME_COLOR;
    if (/system/i.test(name)) return SYSTEM_COLOR;
    let h = 0;
    for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    return PALETTE[h % PALETTE.length];
}

function initials(name) {
    const parts = String(name).trim().split(/\s+/);
    return ((parts[0] || '')[0] + ((parts.length > 1 ? parts[parts.length - 1] : '')[0] || '')).toUpperCase();
}

const dayStart = (d) => { const x = new Date(d); x.setHours(0, 0, 0, 0); return x; };
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };
const sameDay = (a, b) => dayStart(a).getTime() === dayStart(b).getTime();
function weekStart(d) {
    const x = dayStart(d);
    return addDays(x, -((x.getDay() + 6) % 7)); // Monday
}
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const hm = (d) => d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });

const MIN_BLOCK_PX = 22;

/**
 * The part of each booking that falls on `day` (a booking past midnight
 * shows on both days, like Google Calendar), laid out so overlapping
 * bookings share the width side by side.
 */
function segmentsForDay(events, day) {
    const from = dayStart(day).getTime();
    const to = addDays(dayStart(day), 1).getTime();
    const segs = events
        .filter((ev) => ev.start.getTime() < to && ev.end.getTime() > from)
        .map((ev) => ({ ev, s: Math.max(ev.start.getTime(), from), e: Math.min(ev.end.getTime(), to), from }))
        .sort((a, b) => a.s - b.s);
    const cols = [];
    segs.forEach((seg) => {
        let c = cols.findIndex((end) => end <= seg.s);
        if (c === -1) { c = cols.length; cols.push(0); }
        cols[c] = seg.e;
        seg.col = c;
    });
    segs.forEach((seg) => { seg.cols = cols.length; });
    return segs;
}

export function mountScheduleCalendar(host, { device, bookings, navigate }) {
    const events = bookings.map((b) => {
        const start = new Date(b.startAt);
        return { b, start, end: new Date(start.getTime() + (b.durationMin || 0) * 60e3), color: colorFor(b.requester) };
    });
    const people = [...new Set(events.map((e) => e.b.requester))]
        .sort((a, b) => (a === DEMO_USER.name ? -1 : b === DEMO_USER.name ? 1 : a.localeCompare(b)));

    // Open on the nearest booking: the one running now, else the next one to
    // start. Its week is shown, the grid scrolls to it and it is selected so
    // the details below say who booked it.
    const nowMs = Date.now();
    const nearest = events
        .filter((e) => e.end.getTime() > nowMs)
        .sort((a, b) => a.start - b.start)[0] || null;
    let anchor = nearest ? new Date(Math.max(nearest.start.getTime(), nowMs)) : new Date();
    let selected = nearest;
    let focusOn = nearest; // scroll target for the first render only
    let keepScroll = null; // re-render after a click keeps the grid where it was

    function visibleDays() {
        const s = weekStart(anchor);
        return Array.from({ length: 7 }, (_, i) => addDays(s, i));
    }

    function rangeLabel(days) {
        const a = days[0]; const b = days[6];
        return `${a.getDate()} ${MONTHS[a.getMonth()]} – ${b.getDate()} ${MONTHS[b.getMonth()]} ${b.getFullYear()}`;
    }

    function eventHtml(seg) {
        const { ev } = seg;
        const dayPx = 24 * HOUR_PX;
        const height = Math.max(((seg.e - seg.s) / 3600e3) * HOUR_PX, MIN_BLOCK_PX);
        // A short block near midnight is pulled up so its name stays readable.
        const top = Math.min(((seg.s - seg.from) / 3600e3) * HOUR_PX, dayPx - height);
        const w = 100 / seg.cols;
        const isSel = selected && selected.b.id === ev.b.id;
        return `
            <button type="button" class="gcal-ev ${isSel ? 'is-selected' : ''}" data-ev="${escapeHtml(ev.b.id)}"
                style="top:${top}px;height:${height}px;left:calc(${seg.col * w}% + 2px);width:calc(${w}% - 4px);--c:${ev.color}">
                <b>${escapeHtml(ev.b.requester)}</b>
            </button>`;
    }

    function detailHtml() {
        if (!selected) return `<div class="gcal-detail is-empty">${icon('mouse-pointer-click')}<span>Hover a booking for a quick look, click it for the details.</span></div>`;
        const { b, start, end, color } = selected;
        const me = b.requester === DEMO_USER.name;
        return `
            <div class="gcal-detail" style="--c:${color}">
                <span class="gcal-detail-bar"></span>
                <div class="grow">
                    <div class="fw6">${escapeHtml(b.requestName)} <span class="muted id-mono">· ${escapeHtml(b.requestId)}</span></div>
                    <div class="tc-sub">${start.toLocaleDateString('en-GB', { weekday: 'short' })} ${start.getDate()} ${MONTHS[start.getMonth()]} · ${hm(start)}–${hm(end)} · ${b.durationMin} min</div>
                    <div class="gcal-by"><span class="gcal-av" style="background:${color}">${escapeHtml(initials(b.requester))}</span>Booked by <b>${escapeHtml(b.requester)}</b>${me ? ' (you)' : ''}</div>
                </div>
            </div>`;
    }

    function render() {
        const days = visibleDays();
        const today = new Date();
        const daySegs = days.map((d) => segmentsForDay(events, d));
        const inView = daySegs.flat();

        host.innerHTML = `
            <div class="card gcal-card">
                <div class="gcal-toolbar">
                    <button type="button" class="btn btn-outline btn-sm" data-nav="today">Today</button>
                    <button type="button" class="btn btn-outline btn-sm btn-icon" data-nav="-1" aria-label="Previous">${icon('chevron-left')}</button>
                    <button type="button" class="btn btn-outline btn-sm btn-icon" data-nav="1" aria-label="Next">${icon('chevron-right')}</button>
                    <span class="gcal-range">${rangeLabel(days)}</span>
                    ${inView.length ? '' : '<span class="muted text-xs">No booking this week</span>'}
                    <span class="spacer"></span>
                    <button type="button" class="btn btn-primary btn-sm" data-book>${icon('plus')} Book slot</button>
                </div>

                ${people.length ? `<div class="gcal-legend">${people.map((p) => `
                    <span><span class="gcal-av" style="background:${colorFor(p)}">${escapeHtml(initials(p))}</span>${escapeHtml(p)}${p === DEMO_USER.name ? ' (you)' : ''}</span>`).join('')}</div>` : ''}

                <div class="gcal-head" style="grid-template-columns:48px repeat(${days.length}, 1fr)">
                    <div></div>
                    ${days.map((d) => `
                        <div class="gcal-dh ${sameDay(d, today) ? 'is-today' : ''}">
                            ${d.toLocaleDateString('en-GB', { weekday: 'short' })}
                            <b>${d.getDate()}</b>
                        </div>`).join('')}
                </div>
                <div class="gcal-scroll">
                    <div class="gcal-body" style="grid-template-columns:48px repeat(${days.length}, 1fr);height:${24 * HOUR_PX}px">
                        <div class="gcal-hours">${Array.from({ length: 24 }, (_, h) => `<span style="top:${h * HOUR_PX}px">${h ? `${String(h).padStart(2, '0')}:00` : ''}</span>`).join('')}</div>
                        ${days.map((d, i) => {
                            const dayEvents = daySegs[i];
                            const isToday = sameDay(d, today);
                            const weekend = [0, 6].includes(d.getDay());
                            return `
                                <div class="gcal-col ${isToday ? 'is-today' : ''} ${weekend ? 'is-weekend' : ''}">
                                    ${dayEvents.map(eventHtml).join('')}
                                </div>`;
                        }).join('')}
                    </div>
                </div>
                ${detailHtml()}
                <div class="gcal-tip" hidden></div>
            </div>`;
        renderIcons();

        // Start the view near the first booking of the period, else near now / 7:00.
        const scroller = host.querySelector('.gcal-scroll');
        if (keepScroll !== null) {
            scroller.scrollTop = keepScroll;
            keepScroll = null;
        } else if (focusOn) {
            const at = new Date(Math.max(focusOn.start.getTime(), weekStart(anchor).getTime()));
            scroller.scrollTop = Math.max(0, at.getHours() - 1) * HOUR_PX;
            focusOn = null;
        } else {
            const first = inView.reduce((m, seg) => Math.min(m, new Date(seg.s).getHours()), 24);
            const startHour = first < 24 ? first : (days.some((d) => sameDay(d, today)) ? today.getHours() : 8);
            scroller.scrollTop = Math.max(0, startHour - 1) * HOUR_PX;
        }

        host.querySelectorAll('[data-nav]').forEach((b) => b.addEventListener('click', () => {
            const v = b.getAttribute('data-nav');
            anchor = v === 'today' ? new Date() : addDays(anchor, Number(v) * 7);
            render();
        }));
        // Hover: a small card with the booking details next to the block.
        const tip = host.querySelector('.gcal-tip');
        host.querySelectorAll('[data-ev]').forEach((b) => {
            b.addEventListener('mouseenter', () => {
                const ev = events.find((e) => e.b.id === b.getAttribute('data-ev'));
                if (!ev) return;
                tip.innerHTML = `
                    <div class="fw6">${escapeHtml(ev.b.requestName)}</div>
                    <div class="tc-sub">${hm(ev.start)}–${hm(ev.end)} · ${ev.b.durationMin} min</div>
                    <div class="gcal-by"><span class="gcal-av" style="background:${ev.color}">${escapeHtml(initials(ev.b.requester))}</span>${escapeHtml(ev.b.requester)}${ev.b.requester === DEMO_USER.name ? ' (you)' : ''}</div>`;
                tip.hidden = false;
                const r = b.getBoundingClientRect();
                const w = tip.offsetWidth;
                const left = r.right + 8 + w < window.innerWidth ? r.right + 8 : Math.max(8, r.left - 8 - w);
                tip.style.left = `${left}px`;
                tip.style.top = `${Math.max(8, Math.min(r.top, window.innerHeight - tip.offsetHeight - 8))}px`;
            });
            b.addEventListener('mouseleave', () => { tip.hidden = true; });
        });
        scroller.addEventListener('scroll', () => { tip.hidden = true; });

        host.querySelectorAll('[data-ev]').forEach((b) => b.addEventListener('click', () => {
            selected = events.find((e) => e.b.id === b.getAttribute('data-ev')) || null;
            keepScroll = scroller.scrollTop;
            render();
        }));
        host.querySelector('[data-book]').addEventListener('click', () => navigate('/requests/new', { target: device.id }));
    }

    render();
}
