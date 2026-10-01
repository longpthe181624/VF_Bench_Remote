// ============================================================================
// demo/pages/run-result.js — One run, opened from Test History (or View result
// on the bench page). Same layout as the bench session page without its
// controls: Log CAN / Log MHU, the progress bar, the Result card and the test
// case results. A run still going is moved on by the agent (via the backend);
// this page only re-reads it from the store on every change.
// ============================================================================
import { icon, renderIcons } from '../../icons.js';
import { escapeHtml } from '../../utils.js';
import { emptyState } from '../../components/ui-states.js';
import { getRun, getDevice, onStoreChange } from '../store.js';
import { pageHeader, KIND_ICON, KIND_LABEL } from '../ui.js';
import { downloadRunLog } from '../run-logs.js';
import { progressHtml, resultCardHtml, resultsCountText, resultsRowsHtml, resultsCardHtml } from '../run-view.js';

export async function mount(container, ctx) {
    const found = getRun(ctx.params.id);
    if (!found) {
        container.innerHTML = emptyState({ icon: 'search-x', title: 'Run not found', desc: `No run with ID ${escapeHtml(ctx.params.id)}.`, action: '<button class="btn btn-outline btn-sm" data-back>Back to Test History</button>' });
        container.querySelector('[data-back]')?.addEventListener('click', () => ctx.navigate('/runs'));
        return;
    }
    let run = found;
    const device = getDevice(run.targetId) || { id: run.targetId, name: run.targetId };

    container.innerHTML = `
        ${pageHeader({
            iconName: KIND_ICON[device.kind] || 'cpu',
            title: escapeHtml(device.name),
            meta: `
                <span class="id-mono">${escapeHtml(device.id)}</span>
                <span class="sep">·</span><span>${KIND_LABEL[device.kind] || 'Device'}</span>
                <span class="sep">·</span><span class="id-mono">${escapeHtml(run.id)}</span>`,
            actions: `
                <button class="btn btn-outline" id="btn-log-can">${icon('download')} Log CAN</button>
                <button class="btn btn-outline" id="btn-log-mhu">${icon('download')} Log MHU</button>`,
        })}
        <div class="card section-gap"><div class="card-body" id="prog-host"></div></div>
        <div class="section-gap" id="result-host"></div>
        ${resultsCardHtml()}`;

    const $ = (sel) => container.querySelector(sel);

    function paint() {
        $('#prog-host').innerHTML = progressHtml(run, { showDay: true });
        $('#result-host').innerHTML = resultCardHtml(run);
        $('#res-count').textContent = resultsCountText(run);
        $('#res-body').innerHTML = resultsRowsHtml(run);
        renderIcons();
    }

    // Logs exist once the run has ended.
    const paintLogs = () => {
        const has = (run.logs || []).length > 0;
        $('#btn-log-can').disabled = !has;
        $('#btn-log-mhu').disabled = !has;
    };
    $('#btn-log-can').addEventListener('click', () => { if ((run.logs || []).length) downloadRunLog(run, 'CAN'); });
    $('#btn-log-mhu').addEventListener('click', () => { if ((run.logs || []).length) downloadRunLog(run, 'MHU'); });

    paint();
    paintLogs();
    const unsubscribe = onStoreChange(() => {
        if (!container.isConnected) return;
        run = getRun(run.id) || run;
        paint();
        paintLogs();
    });

    return () => unsubscribe();
}
