// ============================================================================
// demo/pages/runs.js — Test History: every run on every device, latest first,
// split into All / MHU / Full Bench and sorted by finish time (latest or
// earliest first). View opens the run page, laid out like the bench session
// page.
// ============================================================================
import { renderIcons } from '../../icons.js';
import { emptyState } from '../../components/ui-states.js';
import { getRuns, getDevice, onStoreChange } from '../store.js';
import { pageHeader } from '../ui.js';
import { historyRowsHtml } from '../run-view.js';

const view = { kind: 'ALL', order: 'desc' };

export async function mount(container, ctx) {
    container.innerHTML = `
        ${pageHeader({ iconName: 'history', title: 'Test History' })}
        <div class="card">
            <div class="card-body">
                <div class="filter-bar kind-tabs-bar history-bar">
                    <span></span>
                    <div class="segmented" id="kind-tabs">
                        <button data-kind="ALL">All</button>
                        <button data-kind="MHU">MHU</button>
                        <button data-kind="FULL_BENCH">Full Bench</button>
                    </div>
                    <div class="inline-8 history-sort">
                        <label class="text-xs muted" for="f-order">Sort</label>
                        <select class="select" id="f-order" style="width:auto">
                            <option value="desc" ${view.order === 'desc' ? 'selected' : ''}>Latest</option>
                            <option value="asc" ${view.order === 'asc' ? 'selected' : ''}>Earliest</option>
                        </select>
                    </div>
                </div>
                <div id="runs-host"></div>
            </div>
        </div>`;

    const host = container.querySelector('#runs-host');
    let lastHtml = '';

    function render() {
        container.querySelectorAll('#kind-tabs button').forEach((b) => b.classList.toggle('active', b.getAttribute('data-kind') === view.kind));
        const rows = getRuns()
            .map((r) => ({ run: r, device: getDevice(r.targetId) || { id: r.targetId, name: r.targetId } }))
            .filter((x) => view.kind === 'ALL' || x.device.kind === view.kind)
            .sort((a, b) => {
                const d = new Date(a.run.finishedAt || a.run.startedAt) - new Date(b.run.finishedAt || b.run.startedAt);
                return view.order === 'asc' ? d : -d;
            });
        const html = rows.length ? `
            <div class="table-wrap">
                <table class="data-table">
                    <thead><tr><th>Device</th><th>Finished</th><th>Status</th><th style="text-align:right">Action</th></tr></thead>
                    <tbody>
                        ${historyRowsHtml(rows)}
                    </tbody>
                </table>
            </div>
            <div class="pagination"><div class="pagination-info">Showing ${rows.length} run${rows.length === 1 ? '' : 's'}</div></div>`
            : emptyState({ icon: 'history', title: 'No run yet', desc: 'Runs appear here after a test on a bench.' });
        if (html === lastHtml) return;            // store changed, this list did not
        lastHtml = html;
        host.innerHTML = html;
        renderIcons();

        host.querySelectorAll('tr[data-open]').forEach((tr) => tr.addEventListener('click', () => ctx.navigate(`/runs/${tr.getAttribute('data-open')}/result`)));
    }

    container.querySelectorAll('#kind-tabs button').forEach((b) => b.addEventListener('click', () => { view.kind = b.getAttribute('data-kind'); render(); }));
    container.querySelector('#f-order').addEventListener('change', (e) => { view.order = e.target.value; render(); });
    render();

    const unsubscribe = onStoreChange(() => { if (container.isConnected) render(); });
    return () => unsubscribe();
}
