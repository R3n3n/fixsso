const TOKEN_KEY = 'sso_token';

const notice = document.getElementById('notice');
const loggedOut = document.getElementById('logged-out');
const loggedIn = document.getElementById('logged-in');

function showNotice(message) {
    notice.textContent = message;
    notice.classList.remove('hidden');
}

function showLoggedOut() {
    loggedIn.classList.add('hidden');
    loggedOut.classList.remove('hidden');
}

async function loadUserInfo() {
    const token = sessionStorage.getItem(TOKEN_KEY);
    if (!token) {
        showLoggedOut();
        return;
    }

    const response = await fetch('/api/userinfo', {
        headers: { Authorization: 'Bearer ' + token }
    });

    if (response.status === 401) {
        sessionStorage.removeItem(TOKEN_KEY);
        const expired = response.headers.get('Token-Expired') === 'true';
        showNotice(expired
            ? 'Your session has expired. Please log in again.'
            : 'Your session is no longer valid. Please log in again.');
        showLoggedOut();
        return;
    }

    if (!response.ok) {
        sessionStorage.removeItem(TOKEN_KEY);
        showNotice('Could not load your profile (HTTP ' + response.status + '). Please log in again.');
        showLoggedOut();
        return;
    }

    const info = await response.json();

    document.getElementById('email').textContent = info.email;
    document.getElementById('tenant-app').textContent = info.tenantApp;
    document.getElementById('groups').textContent = info.groups.length ? info.groups.join(', ') : '(none)';
    document.getElementById('levels').textContent = JSON.stringify(info.levels, null, 2);

    loggedOut.classList.add('hidden');
    loggedIn.classList.remove('hidden');
}

document.getElementById('logout-btn').addEventListener('click', () => {
    sessionStorage.removeItem(TOKEN_KEY);
    window.location.reload();
});

loadUserInfo().catch(() => {
    showNotice('Could not reach the server.');
    showLoggedOut();
});