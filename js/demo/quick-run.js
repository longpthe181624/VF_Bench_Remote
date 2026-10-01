// ============================================================================
// demo/quick-run.js — "Join" from a device card or the device page opens the
// bench session page, where the user uploads test cases and presses Run.
// Both rules (may I join, may I run) live in the store; this file only wires
// them to navigation and toasts.
// ============================================================================
import { showToast } from '../components/toast.js';
import { joinDecision, startRun } from './store.js';

/**
 * Join button handler, shared by every Join button. Offline does nothing;
 * a bench held by someone else only says it is busy; otherwise the bench
 * session page opens (it picks up the user's own run if one is going).
 */
export function handleJoin(device, { navigate }) {
    const decision = joinDecision(device);
    if (decision.state.state === 'Offline') return;
    if (!decision.allowed) {
        showToast({ title: 'Device is busy', description: decision.message, variant: 'error' });
        return;
    }
    navigate(`/devices/${device.id}/session`);
}

/**
 * Starts a run from the files dropped in a bench session ({ id, durationMin }
 * each). Returns { run } or { error } from the store.
 */
export function startQuickRun(device, cases) {
    return startRun(device.id, cases);
}
