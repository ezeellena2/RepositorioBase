async function request(method, path, body) {
  let res;
  try {
    res = await fetch(path, {
      method,
      credentials: 'include',
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined
    });
  } catch {
    throw { code: 'network', message: 'No pudimos conectarnos. Probá de nuevo.' };
  }
  if (res.status === 204 || res.status === 202) return null;
  const data = await res.json().catch(() => null);
  if (!res.ok) {
    throw data?.error ?? { code: 'unknown', message: 'Algo falló. Probá de nuevo.' };
  }
  return data;
}

export const api = {
  register: (payload) => request('POST', '/api/auth/register', payload),
  confirm: (token) => request('POST', '/api/auth/confirm', { token }),
  login: (payload) => request('POST', '/api/auth/login', payload),
  logout: () => request('POST', '/api/auth/logout'),
  me: () => request('GET', '/api/me'),
  setTenant: (tenantId) => request('PUT', '/api/me/tenant', { tenantId })
};
