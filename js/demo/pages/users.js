import { api, getCurrentUser, hasPermission } from '../api-client.js';
import { icon, renderIcons } from '../../icons.js';
import { escapeHtml } from '../../utils.js';
import { emptyState } from '../../components/ui-states.js';
import { showToast } from '../../components/toast.js';
import { pageHeader } from '../ui.js';

export async function mount(container) {
    if (!hasPermission('USER.VIEW')) {
        container.innerHTML = emptyState({ icon: 'shield-x', title: 'Access denied', desc: 'Your account cannot manage users.' });
        renderIcons();
        return;
    }
    container.innerHTML = `${pageHeader({ iconName: 'users', title: 'Users', actions: hasPermission('USER.CREATE') ? `<button class="btn btn-primary" id="new-user">${icon('user-plus')} Create account</button>` : '' })}<div id="users-host" class="card section-gap"><div class="card-body muted">Loading users…</div></div>`;
    renderIcons();
    const host = container.querySelector('#users-host');

    async function load() {
        try {
            const users = await api('/api/users');
            host.innerHTML = `<div class="table-wrap"><table class="data-table"><thead><tr><th>Name</th><th>Email</th><th>Roles</th><th>Status</th></tr></thead><tbody>${users.map((u) => `<tr><td class="fw6">${escapeHtml(u.hoTen)}</td><td>${escapeHtml(u.email)}</td><td>${(u.vaiTro || []).map((r) => `<span class="tag">${escapeHtml(r)}</span>`).join(' ') || '—'}</td><td>${u.dangBiKhoa ? '<span class="badge badge-red">Locked</span>' : '<span class="badge badge-green">Active</span>'}</td></tr>`).join('')}</tbody></table></div>`;
        } catch (e) { host.innerHTML = `<div class="card-body error-state">${escapeHtml(e.message)}</div>`; }
    }

    container.querySelector('#new-user')?.addEventListener('click', () => {
        host.innerHTML = `<div class="card-body" style="max-width:620px"><h3 style="margin-bottom:18px">Create account</h3><form id="create-user-form">
            <div class="grid-halves"><div class="form-group"><label class="form-label">Full name</label><input class="input" name="name" required></div><div class="form-group"><label class="form-label">Email</label><input class="input" name="email" type="email" required></div></div>
            <div class="form-group"><label class="form-label">Temporary password</label><input class="input" name="password" type="password" minlength="8" required></div>
            <div class="form-group"><label class="form-label">Role</label><select class="select" name="role"><option value="Engineer">Engineer</option><option value="Viewer">Viewer</option><option value="Admin">Admin</option></select></div>
            <div class="form-error" id="create-user-error"></div><div class="inline-8" style="justify-content:flex-end"><button class="btn btn-outline" type="button" id="cancel-user">Cancel</button><button class="btn btn-primary" type="submit">${icon('user-plus')} Create account</button></div>
        </form></div>`;
        renderIcons();
        host.querySelector('#cancel-user').addEventListener('click', load);
        host.querySelector('#create-user-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            const form = new FormData(event.currentTarget);
            const button = event.currentTarget.querySelector('[type=submit]');
            button.disabled = true;
            try {
                await api('/api/users', { method: 'POST', body: JSON.stringify({ email: form.get('email'), hoTen: form.get('name'), matKhau: form.get('password'), vaiTro: [form.get('role')] }) });
                showToast({ title: 'Account created', description: `${form.get('email')} can now sign in.`, variant: 'success' });
                await load();
            } catch (e) { host.querySelector('#create-user-error').textContent = e.message; button.disabled = false; }
        });
    });
    await load();
}

