// ============================================================================
// demo/run-view.js — How a run is shown, shared by the bench session page and
// the run page opened from Test History: the progress bar, the Result card
// (green PASS / red FAIL) and the test case results table.
// ============================================================================
import { icon } from '../icons.js';
import { escapeHtml } from '../utils.js';
import { statusBadge, secondsLabel, dayLabel, KIND_ICON } from './ui.js';
import { renderBadge } from '../components/badge.js';
import { caseSeconds, secondsLeft } from './run-sim.js';

// Wall-clock time with seconds, e.g. "14:05:32".
export const clock = (iso) => (iso ? new Date(iso).toLocaleTimeString('en-GB') : '—');

const isRunning = (run) => run && run.status === 'Running';

function counts(run) {
    const cases = run.testCases || [];
    const passed = cases.filter((t) => t.verdict === 'Passed').length;
    const failed = cases.filter((t) => t.verdict === 'Failed').length;
    const notRun = cases.filter((t) => t.verdict === 'Not run').length;
    const end = run.finishedAt ? new Date(run.finishedAt).getTime() : Date.now();
    const sec = Math.max(0, Math.round((end - new Date(run.startedAt).getTime()) / 1000));
    return { cases, passed, failed, notRun, done: passed + failed, sec };
}

/** 'Running' | 'Passed' | 'Failed'. */
export function runResult(run) {
    if (isRunning(run)) return 'Running';
    return run.status === 'Completed' ? 'Passed' : 'Failed';
}

/** PASS / FAIL / Running badge of a run, as in Test History. */
export function resultBadge(run) {
    const r = runResult(run);
    return r === 'Passed' ? renderBadge('PASS', 'badge-green', 'circle-check')
        : r === 'Failed' ? renderBadge('FAIL', 'badge-red', 'circle-x')
            : renderBadge('Running', 'badge-blue', 'loader');
}

/** "01/10/2026, 14:49:49" — when the run finished. */
export const finishedLabel = (iso) => new Date(iso).toLocaleString('en-GB', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });

/**
 * Test History table rows (Device · Finished · Status · Action) for
 * [{ run, device }]; rows and View buttons carry data-open="<run id>".
 */
export function historyRowsHtml(rows) {
    return rows.map(({ run, device }) => `
        <tr class="clickable-row" data-open="${escapeHtml(run.id)}">
            <td><div class="inline-8">${icon(KIND_ICON[device.kind] || 'cpu')}<div><div class="fw6">${escapeHtml(device.name)}</div><div class="id-mono muted">${escapeHtml(device.id)}</div></div></div></td>
            <td class="num nowrap">${run.finishedAt ? finishedLabel(run.finishedAt) : '<span class="muted">—</span>'}</td>
            <td>${resultBadge(run)}</td>
            <td style="text-align:right"><button class="btn btn-outline btn-sm" data-open="${escapeHtml(run.id)}">View</button></td>
        </tr>`).join('');
}

/**
 * Progress bar body. `idleText` is shown when there is no run; `showDay`
 * prefixes a finished run's times with its day.
 */
export function progressHtml(run, { idleText = '', showDay = false } = {}) {
    const bar = (pct, color, label, text) => `
        <div class="row-between" style="margin-bottom:10px">
            <div><b style="${color ? `color:${color}` : ''}">${label}</b> <span class="muted">${text}</span></div>
            <span class="big-pct" style="${color ? `color:${color}` : ''}">${pct}%</span>
        </div>
        <div class="progress" style="height:10px"><div class="progress-bar" style="width:${pct}%;${color ? `background:${color}` : ''}"></div></div>`;
    if (!run) return bar(0, '', 'Ready', idleText ? `· ${escapeHtml(idleText)}` : '');
    const c = counts(run);
    if (isRunning(run)) {
        return bar(run.progress || 0, '', 'Processing',
            `· started ${clock(run.startedAt)} · now ${clock(new Date().toISOString())} · ${c.done} of ${c.cases.length} test cases · about ${secondsLabel(secondsLeft(run))} left`);
    }
    return bar(100, 'var(--color-green)', `${icon('circle-check')} Finished`,
        `· ${showDay ? `${dayLabel(run.startedAt)} · ` : ''}${clock(run.startedAt)} → ${clock(run.finishedAt)} · ${c.done} of ${c.cases.length} test cases · ${secondsLabel(c.sec)}`);
}

/** The Result card: green PASS / red FAIL once the run is over. */
export function resultCardHtml(run) {
    const card = (cls, value, sub, color = '') => `
        <div class="kpi-card${cls}"><div class="kpi-card-label">${icon('gavel')}<span>Result</span></div>
        <div class="kpi-card-value" ${color ? `style="color:${color}"` : ''}>${value}</div>
        <div class="kpi-card-sub">${sub}</div></div>`;
    if (!run) return card('', '—', '&nbsp;');
    const c = counts(run);
    const result = runResult(run);
    if (result === 'Running') return card('', 'Running', c.failed ? `${c.failed} failure so far` : 'Results so far: all passed', 'var(--color-blue)');
    return card(result === 'Passed' ? ' is-pass' : ' is-fail', result === 'Passed' ? 'PASS' : 'FAIL',
        `${clock(run.startedAt)} → ${clock(run.finishedAt)} · ${secondsLabel(c.sec)}`);
}

/** "2 passed · 1 failed · 1 to go" above the results table. */
export function resultsCountText(run) {
    if (!run) return '';
    const c = counts(run);
    return isRunning(run) ? `${c.passed} passed · ${c.failed} failed · ${c.cases.length - c.done} to go`
        : `${c.passed} passed · ${c.failed} failed${c.notRun ? ` · ${c.notRun} not run` : ''}`;
}

/** Rows of the test case results table. */
export function resultsRowsHtml(run) {
    if (!run) return '<tr><td colspan="5" class="muted" style="text-align:center;padding:24px">No run yet</td></tr>';
    return run.testCases.map((t, i) => {
        const notRun = t.verdict === 'Not run';
        const s = notRun ? null : caseSeconds(run, t, i);
        return `
            <tr>
                <td><span class="order-num">${i + 1}</span></td>
                <td><div class="fw6">${escapeHtml(t.name)}</div>${t.name !== t.id ? `<div class="id-mono muted">${escapeHtml(t.id)}</div>` : ''}</td>
                <td class="num nowrap">${t.startedAt && !notRun ? clock(t.startedAt) : '—'}</td>
                <td class="num">${s == null ? '—' : secondsLabel(s)}</td>
                <td>${statusBadge(t.verdict === 'Running' ? 'Processing' : t.verdict)}</td>
            </tr>`;
    }).join('');
}

/** The results table card; the bench page and the run page fill the same ids. */
export function resultsCardHtml() {
    return `
        <div class="card">
            <div class="card-header"><div class="card-title">${icon('list-ordered')} Test case results</div><span class="text-xs muted" id="res-count"></span></div>
            <div class="table-wrap">
                <table class="data-table">
                    <thead><tr><th style="width:60px">Order</th><th>Test case</th><th>Start</th><th>Duration</th><th>Verdict</th></tr></thead>
                    <tbody id="res-body"></tbody>
                </table>
            </div>
        </div>`;
}
