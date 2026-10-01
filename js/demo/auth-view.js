import { login } from './api-client.js';
import { icon, renderIcons } from '../icons.js';

export function mountLogin(container, { onAuthenticated }) {
    let needsTotp = false;
    let savedEmail = '';
    let savedPassword = '';

    const render = () => {
        container.innerHTML = `
            <div class="login-card card">
                <div class="card-body" style="padding:32px">
                    <div class="login-brand">
                        <div class="login-logo"><img src="img/vinfast-logo.jpg" alt="VinFast" class="login-logo-img"></div>
                        <div><h1>Bench Console</h1><p>Remote Testing Web</p></div>
                    </div>
                    <form id="login-form">
                        <div class="login-error" id="login-error" hidden></div>
                        <div class="form-group">
                            <label class="form-label" for="login-email">Email</label>
                            <input class="input" id="login-email" type="email" autocomplete="username" required value="${savedEmail}">
                        </div>
                        <div class="form-group">
                            <label class="form-label" for="login-password">Password</label>
                            <input class="input" id="login-password" type="password" autocomplete="current-password" required value="${savedPassword}">
                        </div>
                        ${needsTotp ? `<div class="form-group">
                            <label class="form-label" for="login-totp">Authenticator code</label>
                            <input class="input id-mono" id="login-totp" inputmode="numeric" autocomplete="one-time-code" maxlength="8" required autofocus>
                            <div class="form-hint">Enter the code from your authenticator app.</div>
                        </div>` : ''}
                        <button class="btn btn-primary" id="login-submit" type="submit" style="width:100%;justify-content:center">
                            ${icon('log-in')} ${needsTotp ? 'Verify and sign in' : 'Sign in'}
                        </button>
                    </form>
                    <div class="note" style="margin-top:18px">${icon('info')}<span>Accounts are created by a Bench Console administrator.</span></div>
                </div>
            </div>`;
        renderIcons();

        container.querySelector('#login-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            const button = container.querySelector('#login-submit');
            const error = container.querySelector('#login-error');
            savedEmail = container.querySelector('#login-email').value.trim();
            savedPassword = container.querySelector('#login-password').value;
            const totp = container.querySelector('#login-totp')?.value.trim() || null;
            error.hidden = true;
            button.disabled = true;
            try {
                const result = await login(savedEmail, savedPassword, totp);
                if (result.canMaTotp) { needsTotp = true; render(); return; }
                await onAuthenticated(result.nguoiDung);
            } catch (e) {
                error.textContent = e.message || 'Could not sign in.';
                error.hidden = false;
            } finally {
                if (button.isConnected) button.disabled = false;
            }
        });
    };
    render();
}

