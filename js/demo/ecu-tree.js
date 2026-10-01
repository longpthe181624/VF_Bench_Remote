// ============================================================================
// demo/ecu-tree.js — ECU diagram of a Full Bench, drawn like VDSA: the gateway
// on top, one colored trunk per CAN line, ECUs hanging off each trunk.
// Only lines the tool returned are drawn; the legend always lists all lines
// with their ECU count so a missing line reads as "0", not as forgotten.
// ============================================================================
import { escapeHtml } from '../utils.js';
import { CAN_LINES } from './data.js';

// A line with more ECUs than this is split to both sides of its trunk.
const ONE_SIDE_MAX = 10;

/** "XGW · 2 CAN lines · 11 ECUs" (singular when there is one). */
export function scanSummary(scan) {
    const lines = Object.keys(scan.lines || {}).length;
    const ecus = countEcus(scan);
    return `${escapeHtml(scan.gateway || 'Gateway')} · ${lines} CAN line${lines === 1 ? '' : 's'} · ${ecus} ECU${ecus === 1 ? '' : 's'}`;
}

export function countEcus(scan) {
    return Object.values(scan.lines || {}).reduce((sum, list) => sum + list.length, 0);
}

export function ecuTreeHtml(scan) {
    const present = CAN_LINES.filter((l) => (scan.lines[l.key] || []).length);
    const box = (code) => `<span class="ecu">${escapeHtml(code)}</span>`;

    const legend = CAN_LINES.map((l) => {
        const n = (scan.lines[l.key] || []).length;
        return `<span class="${n ? '' : 'is-empty'}"><i style="background:${l.color}"></i>${l.key} <b>${n}</b></span>`;
    }).join('');

    const groups = present.map((l) => {
        const list = scan.lines[l.key];
        if (list.length <= ONE_SIDE_MAX) {
            return `
                <div class="ecu-group" style="--c:${l.color}">
                    <span class="ecu-trunk"></span>
                    <div class="ecu-col right">${list.map(box).join('')}</div>
                </div>`;
        }
        const half = Math.floor(list.length / 2);
        return `
            <div class="ecu-group" style="--c:${l.color}">
                <div class="ecu-col left">${list.slice(0, half).map(box).join('')}</div>
                <span class="ecu-trunk"></span>
                <div class="ecu-col right">${list.slice(half).map(box).join('')}</div>
            </div>`;
    }).join('');

    return `
        <div class="ecu-legend">${legend}</div>
        <div class="ecu-tree">
            <div class="ecu-gw">${escapeHtml(scan.gateway || 'Gateway')}</div>
            <div class="ecu-groups">${groups}</div>
        </div>`;
}
