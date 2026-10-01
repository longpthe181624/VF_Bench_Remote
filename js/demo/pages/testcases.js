// ============================================================================
// demo/pages/testcases.js — Files in three tabs. Log CAN / Log MHU: the log
// files the store wrote when each run ended, sorted by date and time and
// filtered to an exact date and, optionally, hour:minute. Test cases: the
// test case library (store.getLibraryTestCases), sorted by name, filtered by
// category and searched by name. A dropped file is named and given a category
// before it is saved; the store refuses a duplicate name or no category.
// All files are demo content generated in the browser.
// ============================================================================
import { icon, renderIcons } from '../../icons.js';
import { escapeHtml } from '../../utils.js';
import { emptyState } from '../../components/ui-states.js';
import { showToast } from '../../components/toast.js';
import { getLibraryTestCases, addTestCaseFile, getRunLogs, onStoreChange, TEST_CASE_EXT, NOT_CONNECTED } from '../store.js';
import { pageHeader } from '../ui.js';
import { renderBadge } from '../../components/badge.js';
import { downloadRunLog } from '../run-logs.js';

const TABS = [
    { key: 'TC', label: 'Test cases' },
    { key: 'CAN', label: 'Log CAN' },
    { key: 'MHU', label: 'Log MHU' },
];
const view = { tab: 'TC', order: 'desc', date: '', time: '' };
const TC_EXT = TEST_CASE_EXT;
export const CATEGORIES = [
    { name: 'Disable warning', cls: 'badge-red', icon: 'bell-off' },
    { name: 'Vivi', cls: 'badge-blue', icon: 'mic' },
    { name: 'Warning message', cls: 'badge-orange', icon: 'triangle-alert' },
    { name: 'Other', cls: 'badge-gray', icon: 'ellipsis' },
];
// Test cases tab: category filter ('' = all) and name search.
const tcView = { cat: '', q: '' };
const catBadge = (c) => {
    const m = CATEGORIES.find((x) => x.name === c);
    return m ? renderBadge(escapeHtml(m.name), m.cls, m.icon) : '<span class="muted">—</span>';
};

const pad = (n) => String(n).padStart(2, '0');
// Local "YYYY-MM-DD" and "HH:MM" of a timestamp, to match the date / time inputs.
const localDate = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const localTime = (d) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
const exactTime = (iso) => new Date(iso).toLocaleString('en-GB', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });

/** Log rows of a tab ('CAN' | 'MHU'): the files written when each run ended. */
function logRows(kind) {
    return getRunLogs(kind).map((l) => ({ file: l.name, run: l.run, at: l.createdAt }));
}

export async function mount(container, ctx) {
    container.innerHTML = `
        ${pageHeader({ iconName: 'file-code', title: 'Test Cases' })}
        <div class="card">
            <div class="card-body">
                <div class="tc-tabs-row">
                    <div class="segmented" id="tc-tabs">
                        ${TABS.map((t) => `<button data-tab="${t.key}" class="${t.key === view.tab ? 'active' : ''}">${t.label}</button>`).join('')}
                    </div>
                </div>
                <div class="filter-bar" id="log-filters">
                    <div class="spacer"></div>
                    <input class="input" type="date" id="f-date" value="${view.date}" style="width:auto" title="Date">
                    <input class="input" type="time" id="f-time" value="${view.time}" style="width:auto" title="Hour and minute">
                    <button class="btn btn-ghost btn-sm" id="f-clear">${icon('rotate-ccw')} Clear</button>
                    <label class="text-xs muted" for="f-order">Sort</label>
                    <select class="select" id="f-order" style="width:auto">
                        <option value="desc" ${view.order === 'desc' ? 'selected' : ''}>Latest</option>
                        <option value="asc" ${view.order === 'asc' ? 'selected' : ''}>Earliest</option>
                    </select>
                </div>
                <div id="tc-host"></div>
            </div>
        </div>`;
    renderIcons();

    const host = container.querySelector('#tc-host');

    let lastLogHtml = '';
    function render() {
        // .filter-bar sets display, so the hidden attribute would not hide it.
        container.querySelector('#log-filters').style.display = view.tab === 'TC' ? 'none' : '';
        if (view.tab === 'TC') { lastLogHtml = ''; return renderTestCases(); }
        const rows = logRows(view.tab).filter((r) => {
            const d = new Date(r.at);
            return (!view.date || localDate(d) === view.date) && (!view.time || localTime(d) === view.time);
        }).sort((a, b) => {
            const d = new Date(a.at) - new Date(b.at);
            return view.order === 'asc' ? d : -d;
        });
        const html = rows.length ? `
            <div class="table-wrap">
                <table class="data-table">
                    <thead><tr><th>File</th><th>Device</th><th>Date &amp; time</th><th style="width:60px"></th></tr></thead>
                    <tbody>
                        ${rows.map((r, i) => `
                            <tr>
                                <td class="fw6 id-mono">${escapeHtml(r.file)}</td>
                                <td class="id-mono">${escapeHtml(r.run.targetId)}</td>
                                <td class="num nowrap">${exactTime(r.at)}</td>
                                <td><button class="btn btn-ghost btn-icon btn-sm" data-dl="${i}" title="Download">${icon('download')}</button></td>
                            </tr>`).join('')}
                    </tbody>
                </table>
            </div>`
            : view.date || view.time
                ? emptyState({ icon: 'calendar-x', title: 'No file at this time', desc: 'Pick another date or time, or clear the filter.' })
                : emptyState({ icon: 'file-x', title: 'No file yet', desc: 'Files appear here after a run on a bench.' });
        if (html === lastLogHtml) return;
        lastLogHtml = html;
        host.innerHTML = html;
        renderIcons();

        host.querySelectorAll('[data-dl]').forEach((b) => b.addEventListener('click', () => {
            downloadRunLog(rows[Number(b.getAttribute('data-dl'))].run, view.tab);
        }));
    }

    // A dropped file waiting for its name and category: { ext, size, base, category }.
    let pending = null;
    let lastLibrary = '';

    function renderTestCases() {
        const all = getLibraryTestCases();
        lastLibrary = JSON.stringify(all);
        const files = all.filter((f) => !tcView.cat || f.category === tcView.cat).sort((a, b) => a.name.localeCompare(b.name));
        host.innerHTML = `
            <div class="grid-2col tc-files">
                <div>
                    <div class="filter-bar">
                        <label class="text-xs muted" for="tc-cat">Category</label>
                        <select class="select" id="tc-cat" style="width:auto">
                            ${[{ name: '' }, ...CATEGORIES].map((c) => `<option value="${escapeHtml(c.name)}" ${tcView.cat === c.name ? 'selected' : ''}>${c.name ? escapeHtml(c.name) : 'All'}</option>`).join('')}
                        </select>
                        <div class="spacer"></div>
                        <div class="search-input-wrap" style="width:240px">${icon('search')}<input class="input" id="tc-q" placeholder="Search by name" value="${escapeHtml(tcView.q)}"></div>
                    </div>
                    <div class="table-wrap">
                        <table class="data-table">
                            <thead><tr><th>File</th><th style="width:170px">Category</th><th style="width:60px"></th></tr></thead>
                            <tbody>
                                ${files.length ? files.map((f, i) => `
                                    <tr data-name="${escapeHtml(f.name.toLowerCase())}">
                                        <td class="fw6 id-mono">${icon('file-archive')} ${escapeHtml(f.name)}</td>
                                        <td>${catBadge(f.category)}</td>
                                        <td><button class="btn btn-ghost btn-icon btn-sm" data-file="${i}" title="Download">${icon('download')}</button></td>
                                    </tr>`).join('')
                                    : `<tr><td colspan="3" class="muted" style="text-align:center;padding:24px">${tcView.cat ? 'No file in this category' : 'No test case file yet'}</td></tr>`}
                                <tr id="tc-nomatch" hidden><td colspan="3" class="muted" style="text-align:center;padding:24px">No file matches this name</td></tr>
                            </tbody>
                        </table>
                    </div>
                </div>
                <div class="tc-upload ${pending ? 'has-pending' : ''}">
                    <div class="form-label">Upload test case</div>
                    <div class="dropzone" id="tc-drop">
                        ${icon('upload-cloud')}
                        <div class="fw6">Drag a .zip or .7z here, or <span style="color:var(--accent)">browse</span></div>
                        <input type="file" id="tc-file" accept="${TC_EXT.join(',')}" hidden>
                    </div>
                    <div class="form-error" id="tc-err"></div>
                    ${pending ? `
                        <div class="card" style="box-shadow:none;margin-top:12px">
                            <div class="card-body">
                                <div class="form-group"><label class="form-label" for="tc-name">Name<span class="required">*</span></label>
                                    <div class="input-prefix">
                                        <input class="input id-mono" id="tc-name" value="${escapeHtml(pending.base)}" style="border-radius:var(--radius-sm) 0 0 var(--radius-sm)">
                                        <span class="suffix">${escapeHtml(pending.ext)}</span>
                                    </div>
                                    <div class="form-hint">${(pending.size / 1024).toFixed(0)} KB</div>
                                    <div class="form-error" id="tc-name-err"></div></div>
                                <div class="form-group"><label class="form-label">Category<span class="required">*</span></label>
                                    <div class="grid-halves" style="gap:8px">
                                        ${CATEGORIES.map((c) => `<button class="btn ${pending.category === c.name ? 'btn-primary' : 'btn-outline'}" data-pick="${escapeHtml(c.name)}" style="justify-content:flex-start">${icon(c.icon)} ${escapeHtml(c.name)}</button>`).join('')}
                                    </div></div>
                                <div class="row-between">
                                    <button class="btn btn-outline" id="tc-cancel">Cancel</button>
                                    <button class="btn btn-primary" id="tc-save" ${pending.category ? '' : 'disabled'}>${icon('save')} Save</button>
                                </div>
                            </div>
                        </div>` : ''}
                </div>
            </div>`;
        renderIcons();

        host.querySelector('#tc-cat').addEventListener('change', (e) => { tcView.cat = e.target.value; renderTestCases(); });
        // Search only hides rows, so typing keeps the focus in the box.
        const search = () => {
            const q = tcView.q.trim().toLowerCase();
            let shown = 0;
            host.querySelectorAll('tbody tr[data-name]').forEach((tr) => {
                const hit = !q || tr.getAttribute('data-name').includes(q);
                tr.hidden = !hit;
                if (hit) shown += 1;
            });
            host.querySelector('#tc-nomatch').hidden = !(files.length && !shown);
        };
        host.querySelector('#tc-q').addEventListener('input', (e) => { tcView.q = e.target.value; search(); });
        search();
        host.querySelectorAll('[data-file]').forEach((b) => b.addEventListener('click', () => {
            const name = files[Number(b.getAttribute('data-file'))].name;
            // Later: GET /api/test-cases/{id}/download.
            showToast({ title: `Could not download ${name}`, description: NOT_CONNECTED, variant: 'error' });
        }));

        // Drop / browse: the file only becomes pending; it is saved from the form.
        const drop = host.querySelector('#tc-drop');
        const input = host.querySelector('#tc-file');
        const take = (list) => {
            const f = list[0];
            if (!f) return;
            const ext = TC_EXT.find((x) => f.name.toLowerCase().endsWith(x));
            if (!ext) { host.querySelector('#tc-err').textContent = `${f.name}: only .zip or .7z files`; return; }
            pending = { ext, size: f.size, base: f.name.slice(0, -ext.length), category: '' };
            renderTestCases();
            host.querySelector('#tc-name')?.focus();
        };
        drop.addEventListener('click', () => input.click());
        input.addEventListener('change', () => take(input.files));
        drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('is-dragover'); });
        drop.addEventListener('dragleave', () => drop.classList.remove('is-dragover'));
        drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('is-dragover'); take(e.dataTransfer.files); });

        if (!pending) return;
        host.querySelector('#tc-name').addEventListener('input', (e) => { pending.base = e.target.value; });
        host.querySelectorAll('[data-pick]').forEach((b) => b.addEventListener('click', () => { pending.category = b.getAttribute('data-pick'); renderTestCases(); }));
        host.querySelector('#tc-cancel').addEventListener('click', () => { pending = null; renderTestCases(); });
        host.querySelector('#tc-save').addEventListener('click', () => {
            const base = pending.base.trim();
            const name = `${base}${pending.ext}`;
            const err = host.querySelector('#tc-name-err');
            if (!base) { err.textContent = 'Enter a name'; return; }
            const res = addTestCaseFile({ name, size: pending.size, category: pending.category });
            if (res.error) { err.textContent = res.error; return; }
            showToast({ title: 'Test case uploaded', description: `${name} · ${pending.category}`, variant: 'success' });
            pending = null;
            renderTestCases();
        });
    }

    container.querySelectorAll('#tc-tabs button').forEach((b) => b.addEventListener('click', () => {
        view.tab = b.getAttribute('data-tab');
        container.querySelectorAll('#tc-tabs button').forEach((x) => x.classList.toggle('active', x === b));
        render();
    }));
    container.querySelector('#f-order').addEventListener('change', (e) => { view.order = e.target.value; render(); });
    container.querySelector('#f-date').addEventListener('change', (e) => { view.date = e.target.value; render(); });
    container.querySelector('#f-time').addEventListener('change', (e) => { view.time = e.target.value; render(); });
    container.querySelector('#f-clear').addEventListener('click', () => {
        view.date = ''; view.time = '';
        container.querySelector('#f-date').value = '';
        container.querySelector('#f-time').value = '';
        render();
    });

    render();

    // A run ending writes logs; an upload adds to the library. The open tab is
    // redrawn only when its own data changed (a half-filled upload form stays).
    const unsubscribe = onStoreChange(() => {
        if (!container.isConnected) return;
        if (view.tab !== 'TC') render();
        else if (!pending && JSON.stringify(getLibraryTestCases()) !== lastLibrary) renderTestCases();
    });
    return () => unsubscribe();
}
