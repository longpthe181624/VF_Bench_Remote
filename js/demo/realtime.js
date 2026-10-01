import { APP_CONFIG } from './config.js';
import { getAccessToken } from './api-client.js';

let connection = null;

export async function startRealtime({ onBenchUpdated, onRunFinished, onCommandUpdated }) {
    if (!window.signalR || connection) return connection;
    connection = new window.signalR.HubConnectionBuilder()
        .withUrl(APP_CONFIG.signalRHubUrl, { accessTokenFactory: getAccessToken })
        .withAutomaticReconnect()
        .build();
    connection.on('benchUpdated', onBenchUpdated);
    connection.on('runFinished', onRunFinished);
    connection.on('commandUpdated', onCommandUpdated);
    await connection.start();
    return connection;
}

export async function stopRealtime() {
    const current = connection;
    connection = null;
    if (current) await current.stop();
}

