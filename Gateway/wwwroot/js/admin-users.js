let currentUserId = null;

// Initialize Page Data on DOM Load
document.addEventListener('DOMContentLoaded', () => {
    // 1. First preference: Get ID from the HTML element attribute (from @Model.Id)
    const container = document.getElementById('userDetailsContainer');
    if (container && container.dataset.userId) {
        currentUserId = container.dataset.userId;
    }

    // 2. Fallback: Parse the GUID directly from the browser URL (handles /Details/{id} or /{id}/Groups)
    if (!currentUserId) {
        const pathSegments = window.location.pathname.split('/');
        const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
        currentUserId = pathSegments.find(segment => guidRegex.test(segment));
    }

    if (currentUserId) {
        loadAssignedGroups();
        loadAvailableGroups();
    }
});

// [BUENO] Search / Filter Table by Email
function filterUsersByEmail() {
    let input = document.getElementById('emailSearchInput').value.toLowerCase();
    let rows = document.querySelectorAll('#usersTable tbody tr');
    rows.forEach(row => {
        let emailCell = row.querySelector('.user-email');
        if (emailCell) {
            let emailText = emailCell.textContent.toLowerCase();
            row.style.display = emailText.includes(input) ? '' : 'none';
        }
    });
}

// Toggle Active Status
async function toggleUserStatus(userId, checkbox) {
    const row = checkbox.closest('tr');
    const badge = row ? row.querySelector('.status-badge') : null;
    const toggleLabel = row ? row.querySelector('.toggle-label') : null;
    const isActive = checkbox.checked;

    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const token = tokenInput ? tokenInput.value : '';

    if (badge) {
        badge.innerText = isActive ? 'Active' : 'Suspended';
        badge.className = isActive ? 'badge badge-active-green status-badge' : 'badge badge-suspended-gray status-badge';
    }

    if (toggleLabel) {
        toggleLabel.innerText = isActive ? 'ON' : 'OFF';
        toggleLabel.className = `me-2 fw-bold small toggle-label ${isActive ? 'text-light' : 'text-secondary'}`;
    }

    fetch(`/Admin/Users/ToggleActive/${userId}`, {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': token
        }
    })
        .then(response => {
            if (!response.ok) throw new Error('Network response error');
            return response.json();
        })
        .catch(err => {
            console.error('Error toggling status:', err);
            checkbox.checked = !isActive;
            if (badge) {
                badge.innerText = !isActive ? 'Active' : 'Suspended';
                badge.className = !isActive ? 'badge badge-active-green status-badge' : 'badge badge-suspended-gray status-badge';
            }
            if (toggleLabel) {
                toggleLabel.innerText = !isActive ? 'ON' : 'OFF';
                toggleLabel.className = `me-2 fw-bold small toggle-label ${!isActive ? 'text-light' : 'text-secondary'}`;
            }
        });
}

// -------------------------------------------------------------
// BACKEND GROUP ENDPOINTS
// -------------------------------------------------------------

// GET /Admin/Users/{userId}/Groups
function loadAssignedGroups() {
    if (!currentUserId) return;

    fetch(`/Admin/Users/${currentUserId}/Groups`)
        .then(response => response.json())
        .then(groups => {
            const listElem = document.getElementById('assigned-groups-list');
            if (!listElem) return;

            listElem.innerHTML = '';

            if (!groups || groups.length === 0) {
                listElem.innerHTML = '<li class="list-group-item bg-dark text-muted border-secondary">No groups assigned</li>';
                return;
            }

            groups.forEach(g => {
                const li = document.createElement('li');
                li.className = 'list-group-item d-flex justify-content-between align-items-center bg-dark text-white border-secondary';
                li.innerHTML = `
                    <span>${g.appName ? g.appName + ' - ' : ''}${g.name || g.groupName}</span>
                    <button type="button" class="btn btn-sm btn-outline-danger" onclick="removeGroup('${g.groupId || g.id}')">Remove</button>
                `;
                listElem.appendChild(li);
            });
        })
        .catch(err => console.error("Error loading assigned groups:", err));
}

// GET /Admin/Users/{userId}/Groups/Available
function loadAvailableGroups() {
    if (!currentUserId) return;

    fetch(`/Admin/Users/${currentUserId}/Groups/Available`)
        .then(response => response.json())
        .then(groups => {
            const dropdown = document.getElementById('available-groups-dropdown');
            if (!dropdown) return;

            dropdown.innerHTML = '';

            if (!groups || groups.length === 0) {
                dropdown.innerHTML = '<option value="">No available groups</option>';
                return;
            }

            groups.forEach(g => {
                const option = document.createElement('option');
                option.value = g.groupId || g.id;
                option.textContent = g.appName ? `${g.appName} - ${g.name}` : (g.name || g.groupName);
                dropdown.appendChild(option);
            });
        })
        .catch(err => console.error("Error loading available groups:", err));
}

// DELETE /Admin/Users/{userId}/Groups/{groupId}
function removeGroup(groupId) {
    if (!currentUserId || !groupId) return;

    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const token = tokenInput ? tokenInput.value : '';

    fetch(`/Admin/Users/${currentUserId}/Groups/${groupId}`, {
        method: 'DELETE',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': token
        }
    })
        .then(response => {
            if (response.ok) {
                loadAssignedGroups();
                loadAvailableGroups();
            } else {
                response.json().then(data => alert(data.message || 'Failed to remove group.')).catch(() => alert('Failed to remove group.'));
            }
        })
        .catch(err => console.error("Error removing group:", err));
}

// POST /Admin/Users/{userId}/Groups
function assignGroup() {
    const dropdown = document.getElementById('available-groups-dropdown');
    const groupId = dropdown ? dropdown.value : null;

    if (!currentUserId || !groupId) {
        alert('Please select a valid group.');
        return;
    }

    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const token = tokenInput ? tokenInput.value : '';

    fetch(`/Admin/Users/${currentUserId}/Groups`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({ groupId: parseInt(groupId, 10) })
    })
        .then(response => {
            if (response.ok) {
                loadAssignedGroups();
                loadAvailableGroups();
            } else {
                response.json().then(data => alert(data.message || 'Failed to assign group.')).catch(() => alert('Failed to assign group.'));
            }
        })
        .catch(err => console.error("Error assigning group:", err));
}

// -------------------------------------------------------------
// Shared helper
// -------------------------------------------------------------
function getAntiForgeryToken(scope) {
    const input = (scope || document).querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
}

// -------------------------------------------------------------
// Create User (modal)
// -------------------------------------------------------------
function clearCreateUserErrors() {
    ['emailError', 'passwordError', 'confirmPasswordError', 'createFormError'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.classList.add('d-none');
    });
}

function showCreateUserError(field, message) {
    const targetId = {
        email: 'emailError',
        password: 'passwordError',
        confirmPassword: 'confirmPasswordError'
    }[field] || 'createFormError';

    const el = document.getElementById(targetId);
    if (!el) return;

    el.textContent = message;
    el.classList.remove('d-none');
}

async function handleCreateUserSubmit(event) {
    event.preventDefault();

    const form = event.target;
    const email = form.querySelector('#createEmail').value.trim();
    const password = form.querySelector('#createPassword').value;
    const confirmPassword = form.querySelector('#createConfirmPassword').value;
    const submitBtn = form.querySelector('[type="submit"]');

    clearCreateUserErrors();

    if (!email) {
        showCreateUserError('email', 'Email is required.');
        return;
    }

    if (password.length < 6) {
        showCreateUserError('password', 'Password must be at least 6 characters.');
        return;
    }

    if (password !== confirmPassword) {
        showCreateUserError('confirmPassword', 'Passwords do not match!');
        return;
    }

    if (submitBtn) submitBtn.disabled = true;

    try {
        const response = await fetch('/Admin/Users/Create', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                'X-Requested-With': 'XMLHttpRequest',
                'RequestVerificationToken': getAntiForgeryToken(form)
            },
            body: new URLSearchParams({
                Email: email,
                Password: password,
                ConfirmPassword: confirmPassword
            })
        });

        let data = {};
        try { data = await response.json(); } catch { /* non-JSON response */ }

        if (response.ok && data.success) {
            // Back to page 1 (newest users first) so the new user is visible
            window.location.href = '/Admin/Users';
            return;
        }

        showCreateUserError(data.field, data.message || 'Failed to create user.');
    } catch (err) {
        console.error('Error creating user:', err);
        showCreateUserError(null, 'Network error. Please try again.');
    } finally {
        if (submitBtn) submitBtn.disabled = false;
    }
}

// Reset the Create User form every time the modal is closed
document.addEventListener('DOMContentLoaded', () => {
    const createModalEl = document.getElementById('createUserModal');
    if (createModalEl) {
        createModalEl.addEventListener('hidden.bs.modal', () => {
            const form = document.getElementById('createUserForm');
            if (form) form.reset();
            clearCreateUserErrors();
        });
    }
});

// -------------------------------------------------------------
// Delete User (modal)
// -------------------------------------------------------------
function setDeleteUserTarget(userId, email) {
    const idInput = document.getElementById('deleteUserId');
    const emailLabel = document.getElementById('deleteUserEmail');
    if (idInput) idInput.value = userId;
    if (emailLabel) emailLabel.textContent = email;
}

// -------------------------------------------------------------
// Password Reset Flow (uses the REAL temporary password from the server)
// -------------------------------------------------------------
function copyTempPasswordToClipboard() {
    const tempPassText = document.getElementById('tempPasswordDisplay')?.textContent.trim();
    if (tempPassText) {
        navigator.clipboard.writeText(tempPassText).then(() => {
            alert('Temporary password copied to clipboard!');
        }).catch(err => {
            console.error('Failed to copy: ', err);
        });
    }
}

async function triggerPasswordReset(userId) {
    const targetId = userId || currentUserId;
    if (!targetId) return;

    try {
        const response = await fetch(`/Admin/Users/ResetPassword/${targetId}`, {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                'RequestVerificationToken': getAntiForgeryToken()
            }
        });

        let data = {};
        try { data = await response.json(); } catch { /* non-JSON response */ }

        if (!response.ok || !data.success || !data.tempPassword) {
            alert(data.message || 'Failed to reset password.');
            return;
        }

        const tempDisplay = document.getElementById('tempPasswordDisplay');
        if (tempDisplay) tempDisplay.textContent = data.tempPassword;

        const tempSection = document.getElementById('tempPasswordSection');
        if (tempSection) tempSection.classList.remove('d-none');

        const successModalEl = document.getElementById('resetSuccessModal');
        if (successModalEl) {
            bootstrap.Modal.getOrCreateInstance(successModalEl).show();
        }
    } catch (err) {
        console.error('Error triggering password reset:', err);
        alert('Network error. Please try again.');
    }
}
