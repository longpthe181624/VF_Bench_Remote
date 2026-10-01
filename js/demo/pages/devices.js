// ============================================================================
// demo/pages/devices.js — Device list for MHUs and Full Benches.
// Card layout mirrors the original Devices screen; adds kind tabs + filters.
// ============================================================================
import { icon, renderIcons } from '../../icons.js';
import { escapeHtml, relativeTime } from '../../utils.js';
import { emptyState } from '../../components/ui-states.js';
import { getDevices, onStoreChange } from '../store.js';
import { deviceBadges, deviceRunInfo, locationLabel, KIND_ICON, pageHeader } from '../ui.js';
import { openRegisterDeviceFlow } from '../register-device.js';
import { handleJoin } from '../quick-run.js';

const query = { kind: 'ALL' };

export async function mount(container, ctx) {
    if (ctx.query.kind) query.kind = ['ALL', 'MHU', 'FULL_BENCH'].includes(ctx.query.kind) ? ctx.query.kind : 'ALL';

    container.innerHTML = `
        ${pageHeader({
            iconName: 'cpu',
            title: 'Devices',
            subtitle: 'MHUs and full benches available for remote testing.',
            actions: `<button class="btn btn-primary" id="btn-register">${icon('plus')} Register Device</button>`,
        })}

        <div class="card">
            <div class="card-body">
                <div class="filter-bar kind-tabs-bar">
                    <div class="segmented" id="kind-tabs">
                        <button data-kind="ALL">All</button>
                        <button data-kind="MHU">MHU</button>
                        <button data-kind="FULL_BENCH">Full Bench</button>
                    </div>
                </div>
                <div id="dev-host"></div>
            </div>
        </div>`;
    renderIcons();

    const host = container.querySelector('#dev-host');

    function syncControls() {
        container.querySelectorAll('#kind-tabs button').forEach((b) => {
            b.classList.toggle('active', b.getAttribute('data-kind') === query.kind);
        });
    }

    function cardHtml(d) {
        const run = deviceRunInfo(d);
        return `
            <div class="dev-card ${d.connection === 'Offline' ? 'is-offline' : ''}" data-id="${d.id}">
                <div class="dev-card-top">
                    <div class="dev-icon">${icon(KIND_ICON[d.kind] || 'car')}</div>
                    <div class="inline-8">${deviceBadges(d)}</div>
                </div>
                <div class="dev-name">${escapeHtml(d.name)}</div>
                <div class="dev-lines">
                    <div>${icon('map-pin')}${locationLabel(d)}</div>
                    <div>${icon('clock')}Last seen ${d.lastSeenAt ? relativeTime(d.lastSeenAt) : 'never'}</div>
                </div>
                <div class="dev-actions">
                    <button class="btn btn-sm run-btn is-${run.mode}" data-action="join" data-id="${d.id}" ${run.mode === 'off' ? 'disabled' : ''}>${icon('log-in')} Join</button>
                    <button class="btn btn-outline btn-sm" data-action="detail" data-id="${d.id}">View detail</button>
                </div>
                <div class="dev-hint is-${run.mode}">${icon(run.icon)}<span>${run.hint}</span></div>
            </div>`;
    }

    let lastHtml = '';
    function render() {
        const rows = getDevices(query);
        const html = rows.length
            ? `<div class="dev-grid">${rows.map(cardHtml).join('')}</div>
               <div class="pagination"><div class="pagination-info">Showing ${rows.length} device${rows.length === 1 ? '' : 's'}</div></div>`
            : emptyState({ icon: 'cpu', title: 'No device of this type', desc: 'Register a new device to see it here.' });
        if (html === lastHtml) return;            // states and hints unchanged
        lastHtml = html;
        host.innerHTML = html;
        renderIcons();

        host.querySelectorAll('.dev-card').forEach((card) => {
            card.addEventListener('click', (e) => {
                const btn = e.target.closest('[data-action]');
                const id = card.getAttribute('data-id');
                if (btn && btn.getAttribute('data-action') === 'join') {
                    e.stopPropagation();
                    handleJoin(rows.find((x) => x.id === id), { navigate: ctx.navigate });
                    return;
                }
                ctx.navigate(`/devices/${id}`);
            });
        });
    }

    container.querySelectorAll('#kind-tabs button').forEach((b) => {
        b.addEventListener('click', () => { query.kind = b.getAttribute('data-kind'); syncControls(); render(); });
    });
    container.querySelector('#btn-register').addEventListener('click', () => {
        openRegisterDeviceFlow({ onCreated: (d) => { query.kind = d.kind; syncControls(); render(); } });
    });

    syncControls();
    render();

    // Registering, booking, a run starting or ending, and the clock all change cards.
    const unsubscribe = onStoreChange(() => { if (container.isConnected) render(); });
    return () => unsubscribe();
}
