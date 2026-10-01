// ============================================================================
// demo/run-logs.js — CAN / MHU log files of a run. The agent uploads them when
// a run ends; downloading needs the backend, so until it is connected the
// download only says so. Used by the bench session, run and Test Cases pages.
// ============================================================================
import { showToast } from '../components/toast.js';
import { NOT_CONNECTED } from './store.js';

export const LOG_KINDS = ['CAN', 'MHU'];

export function logFileName(run, kind) {
    return `${run.id}_${kind.toLowerCase()}.log`;
}

export function downloadRunLog(run, kind) {
    const name = logFileName(run, kind);
    // Later: GET the file the agent uploaded for this run.
    showToast({ title: `Could not download ${name}`, description: NOT_CONNECTED, variant: 'error' });
}
