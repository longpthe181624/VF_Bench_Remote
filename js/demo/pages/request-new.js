// ============================================================================
// demo/pages/request-new.js — Book Test wizard (2 steps): pick the bench and
// the time, then review. Booking only reserves time; test cases are picked
// with the Run button on the device once the booking starts.
// State lives in this module while the wizard is open; on Book it is written
// to the demo store and the user lands on the request detail page.
// ============================================================================
import { icon, renderIcons } from '../../icons.js';
import { escapeHtml } from '../../utils.js';
import { showToast } from '../../components/toast.js';
import { confirmDialog } from '../../components/modal.js';
import { getDevices, getDevice, createRequest, createBooking, findBookingConflict, isBackendConnected, NOT_CONNECTED } from '../store.js';
import { DEMO_USER } from '../data.js';
import { pageHeader, minutesLabel, timeLabel } from '../ui.js';

const STEPS = ['Request Information', 'Review'];

const pad = (n) => String(n).padStart(2, '0');
const dateStr = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const timeStr = (d) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
const toDate = (date, time) => new Date(`${date}T${time}:00`);

function initialState(preselectTarget) {
    const target = preselectTarget ? getDevice(preselectTarget) : null;
    // Test time defaults to the next full hour, for one hour.
    const start = new Date(); start.setMinutes(0, 0, 0); start.setHours(start.getHours() + 1);
    const end = new Date(start.getTime() + 3600e3);
    return {
        step: 0,
        name: 'VF6 Brake Warning Regression',
        // Always the signed-in account; the form does not let it be changed.
        requester: DEMO_USER.name,
        // Booking a given device: step 1 shows that bench, locked like the
        // name. Without a device the user picks the bench in step 1. The
        // project follows the bench and is stored with the request.
        project: target ? (target.projects || [])[0] || '' : 'VF6 MY26',
        benchLocked: !!target,
        startDate: dateStr(start), startTime: timeStr(start),
        endDate: dateStr(end), endTime: timeStr(end),
        targetType: target ? target.kind : 'FULL_BENCH',
        targetId: target ? target.id : '',
    };
}

export async function mount(container, ctx) {
    const state = initialState(ctx.query.target);

    container.innerHTML = `
        ${pageHeader({
            iconName: 'calendar-plus',
            title: 'Book Test',
            subtitle: 'Book a device for your test, attach test cases and choose when it runs.',
            actions: `<button class="btn btn-outline" id="btn-cancel">Cancel</button>`,
        })}
        <div class="card" style="margin-bottom:16px"><div class="stepper" id="stepper"></div></div>
        <div class="card">
            <div class="card-body wizard-body" id="step-host"></div>
            <div class="wizard-foot">
                <button class="btn btn-outline" id="btn-back">${icon('arrow-left')} Back</button>
                <div class="spacer"></div>
                <button class="btn btn-primary" id="btn-next">Continue ${icon('arrow-right')}</button>
            </div>
        </div>`;
    renderIcons();

    const stepHost = container.querySelector('#step-host');
    const backBtn = container.querySelector('#btn-back');
    const nextBtn = container.querySelector('#btn-next');

    // ---------------------------------------------------------------- steps
    function stepperHtml() {
        return STEPS.map((label, i) => {
            const cls = i < state.step ? 'done' : i === state.step ? 'current' : '';
            const num = i < state.step ? icon('check') : i + 1;
            return `${i ? `<div class="step-line ${i <= state.step ? 'done' : ''}"></div>` : ''}
                    <div class="step ${cls}"><span class="step-num">${num}</span>${label}</div>`;
        }).join('');
    }

    function step1() {
        return `
            <div class="grid-halves">
                <div>
                    <div class="form-group"><label class="form-label" for="w-name">Request Name<span class="required">*</span></label>
                        <input class="input" id="w-name" value="${escapeHtml(state.name)}"><div class="form-error" data-err="name"></div></div>
                    <div class="form-group"><label class="form-label" for="w-requester">Name</label>
                        <input class="input" id="w-requester" value="${escapeHtml(state.requester)}" readonly disabled>
                        <div class="form-hint">Your account. It cannot be changed.</div></div>
                    <div class="form-group"><label class="form-label" for="w-bench">Bench</label>
                        ${state.benchLocked
                            ? `<input class="input" id="w-bench" value="${escapeHtml(benchLabel(getDevice(state.targetId)))}" readonly disabled>
                               <div class="form-hint">The device you are booking.</div>`
                            : `<select class="select" id="w-bench">
                                   <option value="">Select a device…</option>
                                   ${getDevices().map((d) => `<option value="${escapeHtml(d.id)}" ${d.id === state.targetId ? 'selected' : ''}>${escapeHtml(benchLabel(d))}</option>`).join('')}
                               </select>
                               <div class="form-error" data-err="bench"></div>`}</div>
                </div>
                <div>
                    <div class="form-group"><label class="form-label">Start<span class="required">*</span></label>
                        <div class="form-row" style="margin:0">
                            <input class="input" type="date" id="w-start-date" value="${state.startDate}">
                            <input class="input" type="time" id="w-start-time" value="${state.startTime}">
                        </div></div>
                    <div class="form-group"><label class="form-label">End<span class="required">*</span></label>
                        <div class="form-row" style="margin:0">
                            <input class="input" type="date" id="w-end-date" value="${state.endDate}">
                            <input class="input" type="time" id="w-end-time" value="${state.endTime}">
                        </div>
                        <div class="form-error" data-err="window"></div>
                        <div class="form-hint" id="w-window-hint">${windowHint()}</div></div>
                </div>
            </div>`;
    }

    function step2() {
        return `
            <div class="card" style="box-shadow:none">
                <div class="card-header"><div class="card-title">${icon('calendar')} Booking</div></div>
                <div class="card-body"><div class="sum-list">
                    <div class="sum-row"><span class="k">Bench</span><span class="v">${escapeHtml(benchLabel(getDevice(state.targetId)))}</span></div>
                    <div class="sum-row"><span class="k">Test time</span><span class="v">${escapeHtml(windowText())}</span></div>
                    <div class="sum-row"><span class="k">Duration</span><span class="v">${minutesLabel(windowMinutes())}</span></div>
                </div></div>
            </div>
            <div class="note blue" style="margin-top:16px">${icon('info')}<span>At the booked time, press Run on the device to pick test cases and start.</span></div>`;
    }

    // -------------------------------------------------------------- helpers
    /** A booking on the bench that overlaps the chosen test time, if any. */
    function windowConflict() {
        if (!state.targetId || !(windowMinutes() > 0)) return null;
        return findBookingConflict(state.targetId,
            toDate(state.startDate, state.startTime).toISOString(), toDate(state.endDate, state.endTime).toISOString());
    }

    function validateStep(showErrors) {
        const errors = {};
        if (state.step === 0 && !state.name.trim()) errors.name = 'Request name is required';
        if (state.step === 0 && !state.targetId) errors.bench = 'Select the device to book';
        if (state.step === 0 && !(windowMinutes() > 0)) errors.window = 'End must be after start';
        else if (state.step === 0 && windowConflict()) {
            const c = windowConflict();
            errors.window = `Overlaps ${c.requester}'s booking ${timeLabel(c.startAt)}–${timeLabel(new Date(new Date(c.startAt).getTime() + c.durationMin * 60e3).toISOString())}`;
        }
        if (showErrors) {
            Object.entries(errors).forEach(([k, v]) => {
                const el = stepHost.querySelector(`[data-err="${k}"]`);
                if (el) el.textContent = v;
            });
        }
        return errors;
    }

    // --------------------------------------------------------------- render
    function render() {
        container.querySelector('#stepper').innerHTML = stepperHtml();
        const builders = [step1, step2];
        stepHost.innerHTML = builders[state.step]();
        backBtn.disabled = state.step === 0;
        nextBtn.innerHTML = state.step === STEPS.length - 1 ? `${icon('calendar-check')} Book` : `Continue ${icon('arrow-right')}`;
        renderIcons();
        bindStep();
    }

    function bindStep() {
        const on = (sel, ev, fn) => stepHost.querySelector(sel)?.addEventListener(ev, fn);

        if (state.step === 0) {
            on('#w-name', 'input', (e) => { state.name = e.target.value; });
            if (!state.benchLocked) on('#w-bench', 'change', (e) => {
                const d = getDevice(e.target.value);
                state.targetId = d ? d.id : '';
                if (d) { state.targetType = d.kind; state.project = (d.projects || [])[0] || ''; }
            });
            const onTime = (key) => (e) => {
                state[key] = e.target.value;
                const err = stepHost.querySelector('[data-err="window"]');
                if (err) err.textContent = windowMinutes() > 0 ? '' : 'End must be after start';
                const hint = stepHost.querySelector('#w-window-hint');
                if (hint) hint.textContent = windowHint();
            };
            on('#w-start-date', 'change', onTime('startDate'));
            on('#w-start-time', 'change', onTime('startTime'));
            on('#w-end-date', 'change', onTime('endDate'));
            on('#w-end-time', 'change', onTime('endTime'));
        }

    }

    function benchLabel(d) {
        return d ? `${d.name} · ${d.id}` : '—';
    }

    // Test time window chosen in step 1.
    function windowMinutes() {
        return Math.round((toDate(state.endDate, state.endTime) - toDate(state.startDate, state.startTime)) / 60e3);
    }
    function windowText() {
        const same = state.startDate === state.endDate;
        return `${state.startDate} ${state.startTime} → ${same ? '' : `${state.endDate} `}${state.endTime}`;
    }
    function windowHint() {
        const m = windowMinutes();
        return m > 0 ? `Duration ${minutesLabel(m)}` : '';
    }

    // ------------------------------------------------------------ submitting
    function buildPayload(status) {
        const scheduledAt = toDate(state.startDate, state.startTime).toISOString();
        return {
            name: state.name.trim(), requester: DEMO_USER.name,
            project: state.project,
            windowStart: toDate(state.startDate, state.startTime).toISOString(),
            windowEnd: toDate(state.endDate, state.endTime).toISOString(),
            ecu: '—', currentSoftware: '—', requiredSoftware: '—',
            softwareSource: '—', flashBeforeTest: false, softwarePackage: '—', checksum: '—',
            notes: '', testCaseIds: [],
            targetType: state.targetType, targetId: state.targetId,
            executionMode: 'Booked', scheduledAt,
            estimatedMin: windowMinutes(), status,
        };
    }

    async function submit() {
        const ok = await confirmDialog({
            title: 'Book this slot?',
            description: `${state.name} will be booked on ${state.targetId} for ${windowText()}.`,
            confirmText: 'Book', variant: 'default',
        });
        if (!ok) return;
        if (!isBackendConnected()) { showToast({ title: 'Could not book', description: NOT_CONNECTED, variant: 'error' }); return; }
        // Someone may have booked the slot meanwhile: the store checks again.
        if (windowConflict()) {
            showToast({ title: 'Slot no longer free', description: 'Another booking now overlaps this time. Pick another time.', variant: 'error' });
            state.step = 0;
            render();
            validateStep(true);
            return;
        }
        const payload = buildPayload('Scheduled');
        const created = createRequest(payload);
        const res = createBooking({
            targetId: payload.targetId, requestId: created.id, requestName: created.name,
            requester: created.requester, startAt: payload.scheduledAt, durationMin: windowMinutes(),
        });
        if (res.error) { showToast({ title: 'Could not book', description: res.error, variant: 'error' }); return; }
        showToast({ title: 'Slot booked', description: `${created.id} — ${created.name}`, variant: 'success' });
        // Back to the device: the new booking is on its calendar.
        ctx.navigate(`/devices/${payload.targetId}`);
    }

    // ----------------------------------------------------------------- wiring
    backBtn.addEventListener('click', () => { if (state.step > 0) { state.step -= 1; render(); } });
    nextBtn.addEventListener('click', () => {
        const errors = validateStep(true);
        if (Object.keys(errors).length) return;
        if (state.step === STEPS.length - 1) return submit();
        state.step += 1;
        render();
    });
    container.querySelector('#btn-cancel').addEventListener('click', () => ctx.navigate(state.benchLocked ? `/devices/${state.targetId}` : '/devices'));

    render();
}
