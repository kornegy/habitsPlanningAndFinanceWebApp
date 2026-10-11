// Session refresh helper. The refresh token lives only in an HttpOnly cookie, so JavaScript never sees it.
// navigator.locks serialises refreshes across tabs: each tab presents the newest cookie, so a token rotated
// by one tab is never replayed by another (which the server would treat as theft and revoke the session).
const refreshUrl = '/api/v1/auth/refresh';
const logoutUrl = '/api/v1/auth/logout';
const csrfHeaders = { 'X-CSRF': '1' };

async function post(url) {
  const response = await fetch(url, { method: 'POST', credentials: 'same-origin', cache: 'no-store', headers: csrfHeaders });
  return { status: response.status, body: await response.text() };
}

export async function refresh() {
  if (navigator.locks && navigator.locks.request) {
    return await navigator.locks.request('pp-session-refresh', () => post(refreshUrl));
  }
  return await post(refreshUrl);
}

export async function logout() {
  if (navigator.locks && navigator.locks.request) {
    return await navigator.locks.request('pp-session-refresh', () => post(logoutUrl));
  }
  return await post(logoutUrl);
}
