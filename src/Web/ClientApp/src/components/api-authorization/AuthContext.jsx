import { createContext, useContext, useState, useEffect } from 'react';
import { IdentityClient } from '../../web-api-client';

const AuthContext = createContext(null);

const client = new IdentityClient();

async function antiforgeryToken() {
  const response = await fetch('/api/identity/antiforgery', { credentials: 'same-origin' });
  if (!response.ok) throw new Error('Unable to establish the session request.');
  return (await response.json()).requestToken;
}

export function AuthProvider({ children }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    client.context()
      .then(() => setIsAuthenticated(true))
      .catch(() => setIsAuthenticated(false))
      .finally(() => setIsLoading(false));
  }, []);

  const login = async (email, password) => {
    const requestToken = await antiforgeryToken();
    const response = await fetch('/api/identity/sessions', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': requestToken },
      body: JSON.stringify({ email, password }),
    });
    if (response.status !== 204) throw new Error('Unable to sign in.');
    await client.context();
    setIsAuthenticated(true);
  };

  const register = async () => {
    throw new Error('Organization registration is not available from this client yet.');
  };

  const logout = async () => {
    const requestToken = await antiforgeryToken();
    const response = await fetch('/api/identity/sessions/current', {
      method: 'DELETE',
      credentials: 'same-origin',
      headers: { 'X-CSRF-TOKEN': requestToken },
    });
    if (response.status !== 204) throw new Error('Unable to sign out.');
    setIsAuthenticated(false);
  };

  return (
    <AuthContext.Provider value={{ isAuthenticated, isLoading, login, register, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export const useAuth = () => useContext(AuthContext);
