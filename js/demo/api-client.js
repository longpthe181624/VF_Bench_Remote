import { APP_CONFIG } from './config.js';

const TOKEN_KEY = 'bench_console_auth_v1';
let session = readSession();

function readSession() {
    try { return JSON.parse(localStorage.getItem(TOKEN_KEY) || 'null'); }
    catch { return null; }
}

function saveSession(value) {
    session = value;
    if (value) localStorage.setItem(TOKEN_KEY, JSON.stringify(value));
    else localStorage.removeItem(TOKEN_KEY);
    window.dispatchEvent(new CustomEvent('bench-auth-changed'));
}

export const getSession = () => session;
export const getAccessToken = () => session?.accessToken || '';
export const getCurrentUser = () => session?.nguoiDung || null;
export const hasPermission = (permission) => (getCurrentUser()?.quyen || []).includes(permission)
    || (getCurrentUser()?.vaiTro || []).includes('Admin');

async function parseResponse(response) {
    if (response.status === 204) return null;
    const type = response.headers.get('content-type') || '';
    if (type.includes('application/json')) return response.json();
    return response.text();
}

async function refreshAccessToken() {
    if (!session?.refreshToken) return false;
    const response = await fetch(`${APP_CONFIG.apiBaseUrl}/api/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: session.refreshToken }),
    });
    if (!response.ok) { saveSession(null); return false; }
    saveSession(await response.json());
    return true;
}

export async function api(path, options = {}, retry = true) {
    const headers = new Headers(options.headers || {});
    if (!(options.body instanceof FormData) && options.body != null && !headers.has('Content-Type')) {
        headers.set('Content-Type', 'application/json');
    }
    if (getAccessToken()) headers.set('Authorization', `Bearer ${getAccessToken()}`);
    const response = await fetch(`${APP_CONFIG.apiBaseUrl}${path}`, { ...options, headers });
    if (response.status === 401 && retry && await refreshAccessToken()) return api(path, options, false);
    const body = await parseResponse(response);
    if (!response.ok) {
        const error = new Error(body?.error || `Request failed (${response.status})`);
        error.status = response.status;
        error.body = body;
        throw error;
    }
    return body;
}

export async function login(email, password, maTotp = null, maKhoiPhuc = null) {
    const response = await fetch(`${APP_CONFIG.apiBaseUrl}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, matKhau: password, maTotp, maKhoiPhuc }),
    });
    const body = await parseResponse(response);
    if (!response.ok) throw new Error(body?.error || 'Đăng nhập không thành công.');
    if (!body.canMaTotp) saveSession(body);
    return body;
}

export async function restoreSession() {
    if (!session?.accessToken) return null;
    try {
        const user = await api('/api/auth/me');
        saveSession({ ...session, nguoiDung: user });
        return user;
    } catch {
        saveSession(null);
        return null;
    }
}

export function logout() { saveSession(null); }

