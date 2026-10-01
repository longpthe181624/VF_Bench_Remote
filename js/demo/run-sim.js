// ============================================================================
// demo/run-sim.js — Time helpers for a run in progress, used by run-view.js.
// The simulated agent that used to move runs forward is gone: progress will
// come from the real agent (MQTT → backend → SignalR) once it is connected.
// ============================================================================

export const TICK_MS = 1200;       // agent step length assumed for "time left"
export const STEPS_PER_CASE = 9;

/** Seconds left at the agent's pace (one step per tick). */
export function secondsLeft(run) {
    const totalSteps = run.testCases.length * STEPS_PER_CASE;
    const doneSteps = (run.currentIndex || 0) * STEPS_PER_CASE + (run.currentStep || 0);
    return Math.max(1, Math.round(((totalSteps - doneSteps) * TICK_MS) / 1000));
}

/** Seconds the case has taken so far (finished: its duration). */
export function caseSeconds(run, tc, i) {
    if (tc.durationSec != null && tc.verdict !== 'Running') return tc.durationSec;
    if (i !== run.currentIndex || !tc.startedAt) return null;
    return Math.max(0, Math.floor((Date.now() - new Date(tc.startedAt).getTime()) / 1000));
}
